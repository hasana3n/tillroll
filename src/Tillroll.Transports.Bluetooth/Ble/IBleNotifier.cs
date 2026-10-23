using System;
using System.Threading.Tasks;

namespace Tillroll.Transports.Bluetooth.Ble;

/// <summary>The platform half of a BLE printer: sends one notification to one subscribed client.</summary>
public interface IBleNotifier
{
    /// <summary>
    /// Sends <paramref name="chunk"/> (already no larger than the client's ATT MTU − 3) as a notification on
    /// <paramref name="profile"/>'s notify characteristic to <paramref name="clientId"/>.
    /// </summary>
    Task NotifyAsync(BlePrinterProfile profile, string clientId, ReadOnlyMemory<byte> chunk);
}
