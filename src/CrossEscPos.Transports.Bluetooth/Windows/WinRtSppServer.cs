using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrossEscPos.Emulator;
using CrossEscPos.Logging;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Radios;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace CrossEscPos.Transports.Bluetooth.Windows;

/// <summary>
/// Classic Bluetooth Serial Port Profile server on Windows (WinRT RFCOMM). Publishes an SPP SDP record named
/// after the printer, makes the radio discoverable while running, and feeds each phone connection into the
/// printer like a TCP client.
/// </summary>
internal sealed class WinRtSppServer : IBluetoothPrinterServer
{
    private const ushort SdpServiceNameAttributeId = 0x0100;
    private const byte SdpTextStringType = 0x25; // text string, 8-bit length

    private readonly ReceiptPrinter _printer;
    private readonly BluetoothPrinterOptions _options;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _gate = new();
    private readonly HashSet<Connection> _connections = new();

    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;
    private Radio? _radio;
    private CancellationTokenSource? _cts;
    private string _name = string.Empty;

    public WinRtSppServer(ReceiptPrinter printer, BluetoothPrinterOptions options)
    {
        _printer = printer;
        _options = options;
        StatusText = "Stopped.";
    }

    public string DisplayName => BluetoothPrinter.SppDisplayName;
    public bool IsRunning { get; private set; }
    public int ConnectedClients { get { lock (_gate) return _connections.Count; } }
    public string StatusText { get; private set; }
    public event Action? StatusChanged;

    public async Task<BluetoothSupport> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
                return BluetoothSupport.Ok(StatusText);

            var probe = await WindowsBluetoothProbe.ProbeAsync(needClassic: true, needPeripheral: false).ConfigureAwait(false);
            if (!probe.Support.IsSupported)
            {
                SetStatus(false, probe.Support.Message);
                return probe.Support;
            }

            try
            {
                _name = string.IsNullOrWhiteSpace(_options.Name) ? "CrossEscPos" : _options.Name.Trim();
                _cts = new CancellationTokenSource();
                _provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.SerialPort);
                _listener = new StreamSocketListener();
                _listener.ConnectionReceived += OnConnectionReceived;
                await _listener.BindServiceNameAsync(_provider.ServiceId.AsString(),
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
                _provider.SdpRawAttributes.Add(SdpServiceNameAttributeId, ServiceNameAttribute(_name));
                _provider.StartAdvertising(_listener, radioDiscoverable: true);

                _radio = probe.Radio;
                if (_radio is not null)
                    _radio.StateChanged += OnRadioStateChanged;

                Logger.Info($"Bluetooth SPP server advertising as \"{_name}\"");
                SetStatus(true, RunningStatus());
                return BluetoothSupport.Ok(StatusText);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Failed to start the Bluetooth SPP server");
                TearDown();
                var support = ex is UnauthorizedAccessException
                    ? new BluetoothSupport(BluetoothSupportState.PermissionDenied,
                        "Windows denied access to Bluetooth. Allow it in Settings > Privacy & security > Radios.")
                    : new BluetoothSupport(BluetoothSupportState.Error, $"Couldn't publish the Bluetooth serial port: {ex.Message}");
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
            if (!IsRunning && _provider is null)
                return;
            TearDown();
            SetStatus(false, "Stopped.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void TearDown()
    {
        if (_radio is not null)
        {
            _radio.StateChanged -= OnRadioStateChanged;
            _radio = null;
        }

        try { _provider?.StopAdvertising(); }
        catch (Exception ex) { Logger.Exception(ex, "Error stopping SPP advertising"); }
        _provider = null;

        if (_listener is not null)
        {
            _listener.ConnectionReceived -= OnConnectionReceived;
            _listener.Dispose();
            _listener = null;
        }

        _cts?.Cancel();
        _cts = null;

        Connection[] open;
        lock (_gate)
        {
            open = new Connection[_connections.Count];
            _connections.CopyTo(open);
        }
        foreach (var connection in open)
            connection.Close();
    }

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        var token = _cts?.Token ?? new CancellationToken(canceled: true);
        var connection = new Connection(args.Socket, _printer);
        lock (_gate) _connections.Add(connection);
        Logger.Info($"Bluetooth SPP client connected: {connection.RemoteName}");
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
                lock (_gate) _connections.Remove(connection);
                Logger.Info($"Bluetooth SPP client disconnected: {connection.RemoteName}");
                if (IsRunning)
                    SetStatus(true, RunningStatus());
            }
        });
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        if (sender.State == RadioState.On || !IsRunning)
            return;
        Logger.Info("Bluetooth radio turned off; SPP server stopped");
        _ = Task.Run(async () =>
        {
            await StopAsync().ConfigureAwait(false);
            SetStatus(false, "Bluetooth turned off. Turn it back on and start again.");
        });
    }

    private string RunningStatus()
        => $"Advertising serial port as \"{_name}\" on {Environment.MachineName} ({ConnectedClients} connected). " +
           "Pair this PC from the phone's Bluetooth settings.";

    private void SetStatus(bool running, string text)
    {
        IsRunning = running;
        StatusText = text;
        StatusChanged?.Invoke();
    }

    private static IBuffer ServiceNameAttribute(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        if (bytes.Length > byte.MaxValue)
            Array.Resize(ref bytes, byte.MaxValue);
        var writer = new DataWriter();
        writer.WriteByte(SdpTextStringType);
        writer.WriteByte((byte)bytes.Length);
        writer.WriteBytes(bytes);
        return writer.DetachBuffer();
    }

    /// <summary>One phone's RFCOMM connection; replies go back over the same socket.</summary>
    private sealed class Connection : IPrinterResponder
    {
        private readonly StreamSocket _socket;
        private readonly ReceiptPrinter _printer;
        private readonly Stream _output;
        private readonly object _writeGate = new();
        private int _closed;

        public Connection(StreamSocket socket, ReceiptPrinter printer)
        {
            _socket = socket;
            _printer = printer;
            _output = socket.OutputStream.AsStreamForWrite(0);
            RemoteName = socket.Information.RemoteHostName?.DisplayName ?? "unknown device";
        }

        public string RemoteName { get; }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            _printer.RegisterResponder(this);
            using var input = _socket.InputStream.AsStreamForRead(0);
            await PrinterStreamFeeder.PumpAsync(_printer, input, this, cancellationToken).ConfigureAwait(false);
        }

        public void Send(byte[] data)
        {
            if (Volatile.Read(ref _closed) == 1)
                return;
            try
            {
                lock (_writeGate)
                {
                    _output.Write(data, 0, data.Length);
                    _output.Flush();
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"Failed to send {data.Length} bytes to {RemoteName}");
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1)
                return;
            _printer.UnregisterResponder(this);
            try { _socket.Dispose(); }
            catch (Exception ex) { Logger.Exception(ex, "Error closing SPP socket"); }
        }
    }
}
