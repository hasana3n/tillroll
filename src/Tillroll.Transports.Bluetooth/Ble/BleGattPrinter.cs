using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Tillroll.Emulator;
using Tillroll.Logging;

namespace Tillroll.Transports.Bluetooth.Ble;

/// <summary>
/// The platform-neutral core of a BLE printer. The Windows and Linux servers report GATT events here
/// (writes, subscriptions, disconnects); this class feeds the written bytes to the printer in order, one
/// queue per phone, and turns the printer's replies into MTU-sized notifications on the service the
/// phone wrote to.
/// </summary>
public sealed class BleGattPrinter : IAsyncDisposable
{
    /// <summary>
    /// Client id for subscriptions whose device isn't known (BlueZ's StartNotify doesn't say who called it).
    /// A reply for a phone with no subscription of its own goes to this subscriber instead.
    /// </summary>
    public const string AnyClient = "*";

    private const int MaxBatchBytes = 64 * 1024;

    private readonly Action<byte[], IPrinterResponder> _feed;
    private readonly Action<IPrinterResponder> _release;
    private readonly Action<IPrinterResponder> _register;
    private readonly Action<IPrinterResponder> _unregister;
    private readonly IBleNotifier _notifier;
    private readonly object _gate = new();
    private readonly Dictionary<string, Client> _clients = new();
    private readonly List<Task> _retiring = new(); // consumers of disconnected clients still finishing up

    public BleGattPrinter(Action<byte[], IPrinterResponder> feed, Action<IPrinterResponder> release,
        Action<IPrinterResponder> register, Action<IPrinterResponder> unregister, IBleNotifier notifier)
    {
        _feed = feed;
        _release = release;
        _register = register;
        _unregister = unregister;
        _notifier = notifier;
    }

    public BleGattPrinter(ReceiptPrinter printer, IBleNotifier notifier)
        : this((bytes, responder) => printer.FeedEscPos(bytes, responder), printer.ReleaseOwnership,
            printer.RegisterResponder, printer.UnregisterResponder, notifier)
    {
    }

    /// <summary>Phones currently known (they wrote or subscribed and haven't disconnected).</summary>
    public int ConnectedClients
    {
        get { lock (_gate) return _clients.Keys.Count(id => id != AnyClient); }
    }

    /// <summary>Raised when <see cref="ConnectedClients"/> changes. May fire on any thread.</summary>
    public event Action? ClientsChanged;

    /// <summary>A phone wrote ESC/POS bytes to <paramref name="profile"/>'s write characteristic. Returns at once.</summary>
    public void OnWrite(string clientId, BlePrinterProfile profile, ReadOnlySpan<byte> data, int attMtu)
    {
        if (data.IsEmpty)
            return;

        var client = GetOrAddClient(clientId, attMtu);
        Interlocked.Increment(ref client.PendingWrites);
        if (!client.Incoming.Writer.TryWrite(new Write(profile, data.ToArray())))
            Interlocked.Decrement(ref client.PendingWrites); // client is closing; nothing more is accepted
    }

    /// <summary>A phone enabled notifications on <paramref name="profile"/>'s notify characteristic.</summary>
    public void OnSubscribed(string clientId, BlePrinterProfile profile, int attMtu)
    {
        var client = GetOrAddClient(clientId, attMtu);
        Responder responder;
        lock (_gate)
        {
            if (!client.Subscriptions.Add(profile))
                return;
            responder = client.ResponderFor(profile, this);
        }
        Logger.Info($"BLE client {clientId} subscribed to {profile.Name}");
        _register(responder); // receives Automatic Status Back while subscribed
    }

    /// <summary>A phone disabled notifications on <paramref name="profile"/>.</summary>
    public void OnUnsubscribed(string clientId, BlePrinterProfile profile)
    {
        Responder responder;
        lock (_gate)
        {
            if (!_clients.TryGetValue(clientId, out var client) || !client.Subscriptions.Remove(profile))
                return;
            responder = client.ResponderFor(profile, this);
        }
        Logger.Info($"BLE client {clientId} unsubscribed from {profile.Name}");
        _unregister(responder);
        _release(responder);
    }

    /// <summary>
    /// A phone disconnected. Bytes it already sent are still printed (phones often disconnect right after
    /// their last write); then its hold on the printer is released and its subscriptions are dropped.
    /// </summary>
    public void OnDisconnected(string clientId)
    {
        Client? client;
        lock (_gate)
        {
            if (!_clients.Remove(clientId, out client))
                return;
            _retiring.Add(RetireAsync(client));
        }
        Logger.Info($"BLE client {clientId} disconnected");
        ClientsChanged?.Invoke();
    }

    /// <summary>Completes when everything <paramref name="clientId"/> wrote so far is fed and its replies are sent.</summary>
    public async Task DrainAsync(string clientId)
    {
        Client? client;
        lock (_gate) _clients.TryGetValue(clientId, out client);
        if (client is null)
            return;

        while (Volatile.Read(ref client.PendingWrites) > 0 || Volatile.Read(ref client.PendingNotifications) > 0)
            await Task.Delay(5).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] waits;
        lock (_gate)
        {
            foreach (var client in _clients.Values)
                _retiring.Add(RetireAsync(client));
            _clients.Clear();
            waits = _retiring.ToArray();
        }
        await Task.WhenAll(waits).ConfigureAwait(false);
    }

    private Client GetOrAddClient(string clientId, int attMtu)
    {
        bool added = false;
        Client client;
        lock (_gate)
        {
            if (!_clients.TryGetValue(clientId, out client!))
            {
                client = new Client(clientId);
                client.Consumer = Task.Run(() => ConsumeAsync(client));
                client.Sender = Task.Run(() => SendAsync(client));
                _clients.Add(clientId, client);
                added = clientId != AnyClient;
            }
            if (attMtu > 0)
                client.AttMtu = attMtu;
        }
        if (added)
        {
            Logger.Info($"BLE client {clientId} connected");
            ClientsChanged?.Invoke();
        }
        return client;
    }

    private async Task RetireAsync(Client client)
    {
        client.Incoming.Writer.TryComplete();
        await client.Consumer.ConfigureAwait(false); // finish feeding what was already received
        client.Outgoing.Writer.TryComplete();
        await client.Sender.ConfigureAwait(false);

        Responder[] responders;
        lock (_gate) responders = client.Responders.Values.ToArray();
        foreach (var responder in responders)
        {
            if (client.Subscriptions.Contains(responder.Profile))
                _unregister(responder);
            _release(responder);
        }
    }

    /// <summary>Feeds one client's writes in order, batching consecutive writes to the same service.</summary>
    private async Task ConsumeAsync(Client client)
    {
        var reader = client.Incoming.Reader;
        var batch = new MemoryStream();
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryPeek(out var first))
            {
                batch.SetLength(0);
                int taken = 0;
                while (batch.Length < MaxBatchBytes && reader.TryPeek(out var next) && next.Profile == first.Profile && reader.TryRead(out next))
                {
                    batch.Write(next.Data);
                    taken++;
                }

                Responder responder;
                lock (_gate) responder = client.ResponderFor(first.Profile, this);
                try
                {
                    _feed(batch.ToArray(), responder);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, $"BLE feed failed for client {client.Id}");
                }
                finally
                {
                    Interlocked.Add(ref client.PendingWrites, -taken);
                }
            }
        }
    }

    /// <summary>Sends one client's notifications in order.</summary>
    private async Task SendAsync(Client client)
    {
        await foreach (var note in client.Outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await _notifier.NotifyAsync(note.Profile, client.Id, note.Chunk).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"BLE notification to {client.Id} failed");
            }
            finally
            {
                Interlocked.Decrement(ref client.PendingNotifications);
            }
        }
    }

    /// <summary>Queues a reply from the printer to the phone that sent the job (or the "*" subscriber).</summary>
    private void Reply(Client writer, BlePrinterProfile profile, byte[] data)
    {
        Client? target = null;
        lock (_gate)
        {
            if (writer.Subscriptions.Contains(profile))
                target = writer;
            else if (_clients.TryGetValue(AnyClient, out var any) && any.Subscriptions.Contains(profile))
                target = any;
        }

        if (target is null)
        {
            Logger.Info($"BLE reply dropped: client {writer.Id} not subscribed to {profile.Name} notifications");
            return;
        }

        foreach (var chunk in BleChunker.Split(data, target.AttMtu))
        {
            Interlocked.Increment(ref target.PendingNotifications);
            if (!target.Outgoing.Writer.TryWrite(new Notification(profile, chunk)))
                Interlocked.Decrement(ref target.PendingNotifications);
        }
    }

    private readonly record struct Write(BlePrinterProfile Profile, byte[] Data);

    private readonly record struct Notification(BlePrinterProfile Profile, ReadOnlyMemory<byte> Chunk);

    private sealed class Client
    {
        public Client(string id) => Id = id;

        public string Id { get; }
        public int AttMtu = BleChunker.DefaultAttMtu;
        public int PendingWrites;
        public int PendingNotifications;
        public readonly Channel<Write> Incoming = Channel.CreateUnbounded<Write>(new UnboundedChannelOptions { SingleReader = true });
        public readonly Channel<Notification> Outgoing = Channel.CreateUnbounded<Notification>(new UnboundedChannelOptions { SingleReader = true });
        public readonly HashSet<BlePrinterProfile> Subscriptions = new();
        public readonly Dictionary<BlePrinterProfile, Responder> Responders = new();
        public Task Consumer = Task.CompletedTask;
        public Task Sender = Task.CompletedTask;

        /// <summary>One responder per (phone, service); call under the engine's lock.</summary>
        public Responder ResponderFor(BlePrinterProfile profile, BleGattPrinter engine)
        {
            if (!Responders.TryGetValue(profile, out var responder))
                Responders[profile] = responder = new Responder(engine, this, profile);
            return responder;
        }
    }

    /// <summary>The printer's reply channel for one (phone, service) pair.</summary>
    private sealed class Responder : IPrinterResponder
    {
        private readonly BleGattPrinter _engine;
        private readonly Client _client;

        public Responder(BleGattPrinter engine, Client client, BlePrinterProfile profile)
        {
            _engine = engine;
            _client = client;
            Profile = profile;
        }

        public BlePrinterProfile Profile { get; }

        public void Send(byte[] data) => _engine.Reply(_client, Profile, data);
    }
}
