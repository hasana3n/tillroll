using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tillroll.Emulator;
using Tillroll.Logging;
using Tillroll.Transports.Bluetooth.Ble;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace Tillroll.Transports.Bluetooth.Linux;

/// <summary>
/// Classic Bluetooth Serial Port Profile server on Linux. Registers an SPP profile with BlueZ
/// (ProfileManager1.RegisterProfile) and implements org.bluez.Profile1: BlueZ hands over each phone's RFCOMM
/// socket in NewConnection, and it's fed into the printer like a TCP client.
/// </summary>
internal sealed class BlueZSppServer : IBluetoothPrinterServer, IPathMethodHandler
{
    internal const string ObjectPath = "/com/tillroll/spp";
    private const string Profile1 = "org.bluez.Profile1";

    private static readonly ReadOnlyMemory<byte> IntrospectionXml = Encoding.UTF8.GetBytes("""
        <interface name="org.bluez.Profile1">
          <method name="Release"/>
          <method name="NewConnection"><arg name="device" type="o" direction="in"/><arg name="fd" type="h" direction="in"/><arg name="fd_properties" type="a{sv}" direction="in"/></method>
          <method name="RequestDisconnection"><arg name="device" type="o" direction="in"/></method>
        </interface>
        """);

    private readonly ReceiptPrinter _printer;
    private readonly BluetoothPrinterOptions _options;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Connection>> _connections = new();

    private BlueZBus? _bus;
    private BlueZAgent? _agent;
    private IDisposable? _lossWatch;
    private CancellationTokenSource? _cts;
    private string _name = string.Empty;
    private string _alias = string.Empty;

    public BlueZSppServer(ReceiptPrinter printer, BluetoothPrinterOptions options)
    {
        _printer = printer;
        _options = options;
        StatusText = "Stopped.";
    }

    public string DisplayName => BluetoothPrinter.SppDisplayName;
    public bool IsRunning { get; private set; }
    public string StatusText { get; private set; }
    public event Action? StatusChanged;

    public int ConnectedClients
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                foreach (var list in _connections.Values)
                    count += list.Count;
                return count;
            }
        }
    }

    string IPathMethodHandler.Path => ObjectPath;
    bool IPathMethodHandler.HandlesChildPaths => false;

    public async Task<BluetoothSupport> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
                return BluetoothSupport.Ok(StatusText);

            try
            {
                _name = string.IsNullOrWhiteSpace(_options.Name) ? "Tillroll" : _options.Name.Trim();
                _bus = await BlueZBus.ConnectAsync(_options.DBusAddress).ConfigureAwait(false);

                var adapter = await _bus.FindAdapterAsync(_options.AdapterName).ConfigureAwait(false);
                if (adapter is null)
                    return await FailAsync(new BluetoothSupport(BluetoothSupportState.NoAdapter,
                        _options.AdapterName is null ? "No Bluetooth adapter found." : $"Bluetooth adapter {_options.AdapterName} not found.")).ConfigureAwait(false);

                await _bus.PrepareAdapterAsync(adapter).ConfigureAwait(false);
                _alias = (await _bus.GetAsync(adapter, BlueZBus.Adapter1, "Alias").ConfigureAwait(false)).GetString();

                if (_options.AutoAcceptPairing)
                {
                    _agent = new BlueZAgent(_bus);
                    await _agent.StartAsync(makeDefault: true).ConfigureAwait(false);
                }

                _cts = new CancellationTokenSource();
                _bus.Connection.AddMethodHandler(this);
                _lossWatch = await _bus.WatchForLossAsync(adapter, OnBlueZLost).ConfigureAwait(false);
                await _bus.CallAsync("/org/bluez", "org.bluez.ProfileManager1", "RegisterProfile", "osa{sv}", (ref MessageWriter w) =>
                {
                    w.WriteObjectPath(ObjectPath);
                    w.WriteString(BlePrinterProfiles.SppServiceUuid.ToString());
                    w.WriteDictionary(new Dictionary<string, VariantValue>
                    {
                        ["Name"] = _name,
                        ["Role"] = "server",
                        ["RequireAuthentication"] = false,
                        ["RequireAuthorization"] = false,
                        ["AutoConnect"] = false,
                    });
                }).ConfigureAwait(false);

                Logger.Info($"Bluetooth SPP profile registered with BlueZ as \"{_name}\"");
                SetStatus(true, RunningStatus());
                return BluetoothSupport.Ok(StatusText);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Failed to start the BlueZ SPP server");
                return await FailAsync(BlueZBus.MapError(ex)).ConfigureAwait(false);
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
            if (_bus is null)
                return;

            try
            {
                await _bus.CallAsync("/org/bluez", "org.bluez.ProfileManager1", "UnregisterProfile", "o",
                    (ref MessageWriter w) => w.WriteObjectPath(ObjectPath)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Info($"UnregisterProfile: {ex.Message}"); // BlueZ may already have released it
            }

            await TearDownAsync().ConfigureAwait(false);
            SetStatus(false, "Stopped.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(new[] { IntrospectionXml });
            return default;
        }

        var request = context.Request;
        if (request.InterfaceAsString != Profile1)
        {
            context.ReplyUnknownMethodError();
            return default;
        }

        switch (request.MemberAsString)
        {
            case "NewConnection":
            {
                var reader = request.GetBodyReader();
                var device = reader.ReadObjectPathAsString();
                var handle = reader.ReadHandle<SafeFileHandle>();
                OnNewConnection(device, handle);
                ReplyEmpty(context);
                break;
            }
            case "RequestDisconnection":
            {
                var device = context.Request.GetBodyReader().ReadObjectPathAsString();
                CloseDevice(device);
                ReplyEmpty(context);
                break;
            }
            case "Release":
            {
                const string released = "BlueZ released the serial profile (bluetoothd stopped or restarted). Start again.";
                Logger.Info("BlueZ released the SPP profile");
                SetStatus(false, released); // before replying, so the status is already right when BlueZ moves on
                ReplyEmpty(context);
                _ = Task.Run(async () =>
                {
                    await _lifecycle.WaitAsync().ConfigureAwait(false);
                    try { await TearDownAsync().ConfigureAwait(false); }
                    finally { _lifecycle.Release(); }
                    SetStatus(false, released);
                });
                break;
            }
            default:
                context.ReplyUnknownMethodError();
                break;
        }
        return default;
    }

    private void OnNewConnection(string device, SafeFileHandle handle)
    {
        var token = _cts?.Token ?? new CancellationToken(canceled: true);
        var connection = new Connection(device, new UnixFdStream(handle), _printer);
        lock (_gate)
        {
            if (!_connections.TryGetValue(device, out var list))
                _connections[device] = list = new List<Connection>();
            list.Add(connection);
        }
        Logger.Info($"Bluetooth SPP client connected: {device}");
        SetStatus(IsRunning, RunningStatus());

        _ = Task.Run(async () =>
        {
            try
            {
                await connection.RunAsync(token).ConfigureAwait(false);
            }
            finally
            {
                connection.Close();
                lock (_gate)
                {
                    if (_connections.TryGetValue(device, out var list) && list.Remove(connection) && list.Count == 0)
                        _connections.Remove(device);
                }
                Logger.Info($"Bluetooth SPP client disconnected: {device}");
                if (IsRunning)
                    SetStatus(true, RunningStatus());
            }
        });
    }

    private void CloseDevice(string device)
    {
        Connection[] closing;
        lock (_gate)
            closing = _connections.TryGetValue(device, out var list) ? list.ToArray() : Array.Empty<Connection>();
        foreach (var connection in closing)
            connection.Close();
    }

    private async Task TearDownAsync()
    {
        _lossWatch?.Dispose();
        _lossWatch = null;
        _cts?.Cancel();
        _cts = null;

        Connection[] open;
        lock (_gate)
        {
            var all = new List<Connection>();
            foreach (var list in _connections.Values)
                all.AddRange(list);
            open = all.ToArray();
        }
        foreach (var connection in open)
            connection.Close();

        if (_agent is not null)
        {
            await _agent.DisposeAsync().ConfigureAwait(false);
            _agent = null;
        }

        if (_bus is not null)
        {
            _bus.Connection.RemoveMethodHandler(ObjectPath);
            await _bus.RestoreAdapterAsync().ConfigureAwait(false);
            await _bus.DisposeAsync().ConfigureAwait(false);
            _bus = null;
        }
        IsRunning = false;
    }

    /// <summary>The adapter or bluetoothd went away: say so at once, then clean up in the background.</summary>
    private void OnBlueZLost(string message)
    {
        if (!IsRunning)
            return;
        Logger.Info($"{DisplayName}: {message}");
        SetStatus(false, message);
        _ = Task.Run(async () =>
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_bus is not null)
                    await TearDownAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycle.Release();
            }
            SetStatus(false, message);
        });
    }

    private async Task<BluetoothSupport> FailAsync(BluetoothSupport support)
    {
        // Undo whatever got done before the failure (adapter settings, agent, connection).
        await TearDownAsync().ConfigureAwait(false);
        SetStatus(false, support.Message);
        return support;
    }

    private string RunningStatus()
        => $"Advertising serial port as \"{_name}\" on {_alias} ({ConnectedClients} connected). " +
           "Pair this computer from the phone's Bluetooth settings.";

    private void SetStatus(bool running, string text)
    {
        IsRunning = running;
        StatusText = text;
        StatusChanged?.Invoke();
    }

    private static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null!);
        context.Reply(writer.CreateMessage());
    }

    /// <summary>One phone's RFCOMM connection; replies go back over the same socket.</summary>
    private sealed class Connection : IPrinterResponder
    {
        private readonly UnixFdStream _stream;
        private readonly ReceiptPrinter _printer;
        private readonly object _writeGate = new();
        private int _closed;

        public Connection(string device, UnixFdStream stream, ReceiptPrinter printer)
        {
            Device = device;
            _stream = stream;
            _printer = printer;
        }

        public string Device { get; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            _printer.RegisterResponder(this);
            return PrinterStreamFeeder.PumpAsync(_printer, _stream, this, cancellationToken);
        }

        public void Send(byte[] data)
        {
            if (Volatile.Read(ref _closed) == 1)
                return;
            try
            {
                lock (_writeGate) _stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"Failed to send {data.Length} bytes to {Device}");
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1)
                return;
            _printer.UnregisterResponder(this);
            _stream.Dispose();
        }
    }
}
