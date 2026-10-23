using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossEscPos.Emulator;
using CrossEscPos.Logging;
using CrossEscPos.Transports.Bluetooth.Ble;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace CrossEscPos.Transports.Bluetooth.Windows;

/// <summary>
/// BLE GATT peripheral on Windows (WinRT GattServiceProvider) exposing the three thermal-printer service
/// layouts in <see cref="BlePrinterProfiles"/>. Only 0x18F0 goes in the advertisement; Windows still has to
/// "advertise" every provider for its service to be published, so the others advertise non-discoverably.
/// GATT events are handed to the shared <see cref="BleGattPrinter"/> engine.
/// </summary>
internal sealed class WinRtBleServer : IBluetoothPrinterServer, IBleNotifier
{
    private static readonly TimeSpan AdvertisingStartTimeout = TimeSpan.FromSeconds(10);

    private readonly ReceiptPrinter _printer;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _gate = new();
    private readonly List<Published> _published = new();
    private readonly Dictionary<string, GattSession> _sessions = new();
    private BleGattPrinter? _engine;
    private Radio? _radio;

    public WinRtBleServer(ReceiptPrinter printer, BluetoothPrinterOptions options)
    {
        _printer = printer;
        _ = options; // Windows always advertises the PC name over BLE; the printer name isn't used here.
        StatusText = "Stopped.";
    }

    public string DisplayName => BluetoothPrinter.BleDisplayName;
    public bool IsRunning { get; private set; }
    public int ConnectedClients => _engine?.ConnectedClients ?? 0;
    public string StatusText { get; private set; }
    public event Action? StatusChanged;

    /// <summary>Each published service's advertisement status as Windows reports it (diagnostics and tests).</summary>
    internal IReadOnlyDictionary<string, string> PublishedServiceStatuses
    {
        get
        {
            lock (_gate)
                return _published.ToDictionary(p => p.Profile.Name, p => p.Provider.AdvertisementStatus.ToString());
        }
    }

    public async Task<BluetoothSupport> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
                return BluetoothSupport.Ok(StatusText);

            var probe = await WindowsBluetoothProbe.ProbeAsync(needClassic: false, needPeripheral: true).ConfigureAwait(false);
            if (!probe.Support.IsSupported)
            {
                SetStatus(false, probe.Support.Message);
                return probe.Support;
            }

            try
            {
                _engine = new BleGattPrinter(_printer, this);
                _engine.ClientsChanged += OnClientsChanged;

                foreach (var profile in BlePrinterProfiles.All)
                    _published.Add(await PublishAsync(profile).ConfigureAwait(false));

                var failure = await StartAdvertisingAsync(cancellationToken).ConfigureAwait(false);
                if (failure is not null)
                {
                    await TearDownAsync().ConfigureAwait(false);
                    SetStatus(false, failure.Message);
                    return failure;
                }

                _radio = probe.Radio;
                if (_radio is not null)
                    _radio.StateChanged += OnRadioStateChanged;

                Logger.Info("Bluetooth LE printer services published and advertising 0x18F0");
                SetStatus(true, RunningStatus());
                return BluetoothSupport.Ok(StatusText);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Failed to start the Bluetooth LE printer");
                await TearDownAsync().ConfigureAwait(false);
                var support = ex is UnauthorizedAccessException
                    ? new BluetoothSupport(BluetoothSupportState.PermissionDenied,
                        "Windows denied access to Bluetooth. Allow it in Settings > Privacy & security > Radios.")
                    : new BluetoothSupport(BluetoothSupportState.Error, $"Couldn't publish the BLE printer service: {ex.Message}");
                SetStatus(false, support.Message);
                return support;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning && _engine is null)
                return;
            await TearDownAsync().ConfigureAwait(false);
            SetStatus(false, "Stopped.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    // ---- IBleNotifier -----------------------------------------------------------------------------

    public async Task NotifyAsync(BlePrinterProfile profile, string clientId, ReadOnlyMemory<byte> chunk)
    {
        Published? published;
        lock (_gate) published = _published.FirstOrDefault(p => p.Profile == profile);
        if (published is null)
            return;

        var client = published.Notify.SubscribedClients.FirstOrDefault(c => c.Session.DeviceId.Id == clientId);
        if (client is null)
        {
            Logger.Info($"BLE notification dropped: {clientId} is no longer subscribed to {profile.Name}");
            return;
        }

        var writer = new DataWriter();
        writer.WriteBytes(chunk.ToArray());
        var result = await published.Notify.NotifyValueAsync(writer.DetachBuffer(), client);
        if (result.Status != GattCommunicationStatus.Success)
            Logger.Info($"BLE notification to {clientId} failed: {result.Status}");
    }

    // ---- publishing -------------------------------------------------------------------------------

    private async Task<Published> PublishAsync(BlePrinterProfile profile)
    {
        var created = await GattServiceProvider.CreateAsync(profile.ServiceUuid);
        if (created.Error != BluetoothError.Success)
            throw new InvalidOperationException($"creating service {profile.Name} failed ({created.Error})");
        var provider = created.ServiceProvider;

        var writeProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse;
        if (profile.SharedCharacteristic)
            writeProperties |= GattCharacteristicProperties.Notify | GattCharacteristicProperties.Read;

        var write = await CreateCharacteristicAsync(provider, profile.WriteUuid, writeProperties, "ESC/POS in");
        var notify = profile.SharedCharacteristic
            ? write
            : await CreateCharacteristicAsync(provider, profile.NotifyUuid, GattCharacteristicProperties.Notify, "Printer replies");

        var published = new Published(profile, provider, write, notify);
        write.WriteRequested += published.OnWriteRequested = (sender, args) => OnWriteRequested(profile, args);
        if (profile.SharedCharacteristic)
            write.ReadRequested += published.OnReadRequested = (sender, args) => OnReadRequested(args);
        notify.SubscribedClientsChanged += published.OnSubscribersChanged = (sender, _) => OnSubscribersChanged(published);
        provider.AdvertisementStatusChanged += published.OnAdvertisementStatusChanged = (sender, args) =>
            Logger.Info($"BLE service {profile.Name} advertisement status: {args.Status} ({args.Error})");
        return published;
    }

    private static async Task<GattLocalCharacteristic> CreateCharacteristicAsync(GattServiceProvider provider, Guid uuid,
        GattCharacteristicProperties properties, string description)
    {
        var result = await provider.Service.CreateCharacteristicAsync(uuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = properties,
            ReadProtectionLevel = GattProtectionLevel.Plain,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = description,
        });
        if (result.Error != BluetoothError.Success)
            throw new InvalidOperationException($"creating characteristic {uuid} failed ({result.Error})");
        return result.Characteristic;
    }

    /// <summary>Starts advertising every provider; returns a failure only if the advertised (0x18F0) one fails.</summary>
    private async Task<BluetoothSupport?> StartAdvertisingAsync(CancellationToken cancellationToken)
    {
        foreach (var published in _published)
        {
            published.Provider.StartAdvertising(new GattServiceProviderAdvertisingParameters
            {
                IsConnectable = true,
                IsDiscoverable = published.Profile.Advertised,
            });
        }

        // Only the advertised (0x18F0) provider ever leaves "Created": Windows merges discoverable services into
        // one advertisement, and the compatibility services are registered non-discoverably because their
        // 128-bit UUIDs don't fit next to 0x18F0. So wait for that one alone.
        var primary = _published.First(p => p.Profile.Advertised);
        var deadline = DateTime.UtcNow + AdvertisingStartTimeout;
        int attempt = 1;
        while (DateTime.UtcNow < deadline && !IsStarted(primary.Provider.AdvertisementStatus))
        {
            if (primary.Provider.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Aborted)
            {
                // Windows aborts when the adapter's advertising is briefly busy (an advertisement that was
                // just stopped, Swift Pair, Nearby Sharing, …). Back off and try again within the time limit.
                Logger.Info($"BLE advertising aborted (attempt {attempt}); retrying");
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(2000, 250 * attempt)), cancellationToken).ConfigureAwait(false);
                // Windows often recovers on its own; only restart if it's still aborted.
                if (DateTime.UtcNow >= deadline || primary.Provider.AdvertisementStatus != GattServiceProviderAdvertisementStatus.Aborted)
                    continue;
                attempt++;
                try
                {
                    primary.Provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
                }
                catch (InvalidOperationException)
                {
                    // It changed state between the check and the call; the loop re-reads it.
                }
                continue;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        foreach (var p in _published)
            Logger.Info($"BLE service {p.Profile.Name}: {p.Provider.AdvertisementStatus}");

        if (!IsStarted(primary.Provider.AdvertisementStatus))
        {
            return primary.Provider.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Aborted
                ? new BluetoothSupport(BluetoothSupportState.Error,
                    "Windows refused to advertise the BLE printer service. Another app may be using the adapter's advertising slots.")
                : new BluetoothSupport(BluetoothSupportState.Error, "Windows didn't start BLE advertising within 10 seconds.");
        }

        foreach (var other in _published.Where(p => p.Provider.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Aborted))
            Logger.Info($"BLE service {other.Profile.Name} couldn't be registered; apps using it won't find it");
        return null;
    }

    private static bool IsStarted(GattServiceProviderAdvertisementStatus status)
        => status is GattServiceProviderAdvertisementStatus.Started
            or GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData;

    private static bool IsSettled(GattServiceProviderAdvertisementStatus status)
        => IsStarted(status) || status == GattServiceProviderAdvertisementStatus.Aborted;

    private async Task TearDownAsync()
    {
        if (_radio is not null)
        {
            _radio.StateChanged -= OnRadioStateChanged;
            _radio = null;
        }

        Published[] published;
        Dictionary<string, GattSession> sessions;
        lock (_gate)
        {
            published = _published.ToArray();
            _published.Clear();
            sessions = new Dictionary<string, GattSession>(_sessions);
            _sessions.Clear();
        }

        foreach (var p in published)
        {
            try
            {
                if (p.Provider.AdvertisementStatus is GattServiceProviderAdvertisementStatus.Started
                    or GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData)
                    p.Provider.StopAdvertising();
            }
            catch (Exception ex) { Logger.Exception(ex, $"Error stopping BLE service {p.Profile.Name}"); }
            p.Detach();
        }

        foreach (var session in sessions.Values)
            session.SessionStatusChanged -= OnSessionStatusChanged;

        if (_engine is not null)
        {
            _engine.ClientsChanged -= OnClientsChanged;
            await _engine.DisposeAsync().ConfigureAwait(false);
            _engine = null;
        }
    }

    // ---- GATT events ------------------------------------------------------------------------------

    private async void OnWriteRequested(BlePrinterProfile profile, GattWriteRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null)
                return; // the app has no access, or the request was cancelled

            var session = args.Session;
            TrackSession(session);
            var reader = DataReader.FromBuffer(request.Value);
            var data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
            _engine?.OnWrite(session.DeviceId.Id, profile, data, session.MaxPduSize);

            if (request.Option == GattWriteOption.WriteWithResponse)
                request.Respond();
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "BLE write handling failed");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static async void OnReadRequested(GattReadRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            request?.RespondWithValue(new DataWriter().DetachBuffer()); // nothing to read; some apps probe it
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "BLE read handling failed");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnSubscribersChanged(Published published)
    {
        var engine = _engine;
        if (engine is null)
            return;

        var now = published.Notify.SubscribedClients.ToDictionary(c => c.Session.DeviceId.Id, c => c.Session);
        string[] added, removed;
        lock (_gate)
        {
            added = now.Keys.Except(published.Subscribers).ToArray();
            removed = published.Subscribers.Except(now.Keys).ToArray();
            published.Subscribers.Clear();
            published.Subscribers.UnionWith(now.Keys);
        }

        foreach (var id in added)
        {
            TrackSession(now[id]);
            engine.OnSubscribed(id, published.Profile, now[id].MaxPduSize);
        }
        foreach (var id in removed)
            engine.OnUnsubscribed(id, published.Profile);
    }

    private void TrackSession(GattSession session)
    {
        lock (_gate)
        {
            if (!_sessions.TryAdd(session.DeviceId.Id, session))
                return;
        }
        session.SessionStatusChanged += OnSessionStatusChanged;
    }

    private void OnSessionStatusChanged(GattSession session, GattSessionStatusChangedEventArgs args)
    {
        if (args.Status != GattSessionStatus.Closed)
            return;

        var id = session.DeviceId.Id;
        lock (_gate)
        {
            _sessions.Remove(id);
            foreach (var p in _published)
                p.Subscribers.Remove(id);
        }
        session.SessionStatusChanged -= OnSessionStatusChanged;
        _engine?.OnDisconnected(id);
    }

    private void OnClientsChanged()
    {
        if (IsRunning)
            SetStatus(true, RunningStatus());
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        if (sender.State == RadioState.On || !IsRunning)
            return;
        Logger.Info("Bluetooth radio turned off; BLE printer stopped");
        _ = Task.Run(async () =>
        {
            await StopAsync().ConfigureAwait(false);
            SetStatus(false, "Bluetooth turned off. Turn it back on and start again.");
        });
    }

    private string RunningStatus()
        => $"Advertising BLE printer service 18F0 as \"{Environment.MachineName}\" ({ConnectedClients} connected). " +
           "Windows always uses the PC name over BLE.";

    private void SetStatus(bool running, string text)
    {
        IsRunning = running;
        StatusText = text;
        StatusChanged?.Invoke();
    }

    /// <summary>One published service and the handlers attached to it (kept so they can be detached).</summary>
    private sealed class Published
    {
        public Published(BlePrinterProfile profile, GattServiceProvider provider, GattLocalCharacteristic write, GattLocalCharacteristic notify)
        {
            Profile = profile;
            Provider = provider;
            Write = write;
            Notify = notify;
        }

        public BlePrinterProfile Profile { get; }
        public GattServiceProvider Provider { get; }
        public GattLocalCharacteristic Write { get; }
        public GattLocalCharacteristic Notify { get; }
        public HashSet<string> Subscribers { get; } = new();

        public global::Windows.Foundation.TypedEventHandler<GattLocalCharacteristic, GattWriteRequestedEventArgs>? OnWriteRequested;
        public global::Windows.Foundation.TypedEventHandler<GattLocalCharacteristic, GattReadRequestedEventArgs>? OnReadRequested;
        public global::Windows.Foundation.TypedEventHandler<GattLocalCharacteristic, object>? OnSubscribersChanged;
        public global::Windows.Foundation.TypedEventHandler<GattServiceProvider, GattServiceProviderAdvertisementStatusChangedEventArgs>? OnAdvertisementStatusChanged;

        public void Detach()
        {
            if (OnWriteRequested is not null) Write.WriteRequested -= OnWriteRequested;
            if (OnReadRequested is not null) Write.ReadRequested -= OnReadRequested;
            if (OnSubscribersChanged is not null) Notify.SubscribedClientsChanged -= OnSubscribersChanged;
            if (OnAdvertisementStatusChanged is not null) Provider.AdvertisementStatusChanged -= OnAdvertisementStatusChanged;
        }
    }
}
