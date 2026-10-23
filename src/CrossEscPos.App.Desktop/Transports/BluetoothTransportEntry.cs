using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CrossEscPos.App.Transports;
using CrossEscPos.Logging;
using CrossEscPos.Transports.Bluetooth;

namespace CrossEscPos.App.Desktop.Transports;

/// <summary>
/// A Bluetooth printer service (Classic SPP for Android, or BLE for iPhone) in the connections panel. Off until
/// started: starting makes this computer visible to phones as a receipt printer.
/// </summary>
public sealed class BluetoothTransportEntry : TransportEntry
{
    private readonly IBluetoothPrinterServer _server;
    private readonly BluetoothPrinterOptions _options;
    private readonly TransportField _name;
    private bool _busy;

    public BluetoothTransportEntry(IBluetoothPrinterServer server, BluetoothPrinterOptions options, bool autoStart)
        : base(server.DisplayName)
    {
        _server = server;
        _options = options;
        _name = new TransportField("Printer name", options.Name);
        Fields = new[] { _name };
        _server.StatusChanged += OnServerStatusChanged;
        Set(false, "Off. Start it to make this computer visible to phones as a Bluetooth printer.");
        if (autoStart)
            _ = ToggleAsync();
    }

    public override string ButtonText => IsActive ? "Stop" : "Start";

    public override TransportKind Kind => TransportKind.Bluetooth;

    public override string Description => _server.DisplayName == BluetoothPrinter.BleDisplayName
        ? "iPhone and iPad, no pairing needed"
        : "Android phones, pair once in Settings";

    protected override async Task ToggleAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            if (_server.IsRunning)
            {
                Status = "Stopping…";
                await _server.StopAsync();
            }
            else
            {
                // A renamed printer takes effect from the next start.
                _options.Name = string.IsNullOrWhiteSpace(_name.Value) ? "CrossEscPos" : _name.Value.Trim();
                Status = "Starting…";
                await _server.StartAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, $"{_server.DisplayName} failed");
        }
        finally
        {
            _busy = false;
            OnServerStatusChanged();
        }
    }

    public override void Shutdown()
    {
        _server.StatusChanged -= OnServerStatusChanged;
        try
        {
            // The servers never resume on the caller's context, so waiting on the UI thread is safe.
            if (!_server.StopAsync().Wait(TimeSpan.FromSeconds(3)))
                Logger.Info($"{_server.DisplayName} didn't stop within 3 seconds");
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, $"Stopping {_server.DisplayName} failed");
        }
    }

    private void OnServerStatusChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Set(_server.IsRunning, _server.StatusText);
        else
            Dispatcher.UIThread.Post(() => Set(_server.IsRunning, _server.StatusText));
    }
}
