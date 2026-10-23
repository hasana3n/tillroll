using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tillroll.Transports.Bluetooth;

/// <summary>Stands in on platforms without Bluetooth printer emulation; explains why it can't start.</summary>
internal sealed class UnsupportedBluetoothServer : IBluetoothPrinterServer
{
    private readonly string _reason;

    public UnsupportedBluetoothServer(string displayName, string reason)
    {
        DisplayName = displayName;
        _reason = reason;
        StatusText = reason;
    }

    public string DisplayName { get; }
    public bool IsRunning => false;
    public int ConnectedClients => 0;
    public string StatusText { get; }

    public event Action? StatusChanged { add { } remove { } }

    public Task<BluetoothSupport> StartAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new BluetoothSupport(BluetoothSupportState.UnsupportedPlatform, _reason));

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
