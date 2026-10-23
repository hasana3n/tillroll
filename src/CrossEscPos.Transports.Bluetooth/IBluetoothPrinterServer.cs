using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrossEscPos.Transports.Bluetooth;

/// <summary>
/// A Bluetooth service that makes this computer look like a receipt printer to phones, and feeds what they
/// send into a <see cref="Emulator.ReceiptPrinter"/>.
/// </summary>
public interface IBluetoothPrinterServer : IAsyncDisposable
{
    /// <summary>"Bluetooth (Android)" or "Bluetooth LE (iPhone)".</summary>
    string DisplayName { get; }

    /// <summary>True only once the operating system confirmed the service is published.</summary>
    bool IsRunning { get; }

    /// <summary>Phones currently connected.</summary>
    int ConnectedClients { get; }

    /// <summary>One plain-language line describing the current state, for a status bar.</summary>
    string StatusText { get; }

    /// <summary>Raised when <see cref="IsRunning"/>, <see cref="ConnectedClients"/> or <see cref="StatusText"/> change. May fire on any thread.</summary>
    event Action? StatusChanged;

    /// <summary>Publishes the service. Returns why it couldn't when the result isn't supported.</summary>
    Task<BluetoothSupport> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Withdraws the service and disconnects phones.</summary>
    Task StopAsync();
}
