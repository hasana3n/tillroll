using System;
using System.Collections.Generic;

namespace CrossEscPos.Transports.Bluetooth.Ble;

/// <summary>
/// One BLE GATT service layout used by thermal printers: phones write ESC/POS to <see cref="WriteUuid"/>
/// and receive replies (status bytes) as notifications on <see cref="NotifyUuid"/>.
/// </summary>
/// <param name="Name">A short label for logs.</param>
/// <param name="Advertised">Whether the service UUID goes in the advertisement (only one fits).</param>
public sealed record BlePrinterProfile(string Name, Guid ServiceUuid, Guid WriteUuid, Guid NotifyUuid, bool Advertised)
{
    /// <summary>True when one characteristic both receives writes and sends notifications.</summary>
    public bool SharedCharacteristic => WriteUuid == NotifyUuid;
}

/// <summary>The Bluetooth UUIDs CrossEscPos publishes.</summary>
public static class BlePrinterProfiles
{
    /// <summary>The standard Classic Bluetooth Serial Port Profile UUID.</summary>
    public static readonly Guid SppServiceUuid = Guid.Parse("00001101-0000-1000-8000-00805F9B34FB");

    /// <summary>The generic thermal-printer service most cheap BLE printers expose. The one we advertise.</summary>
    public static readonly BlePrinterProfile Generic18F0 = new(
        "18F0",
        Guid.Parse("000018F0-0000-1000-8000-00805F9B34FB"),
        Guid.Parse("00002AF1-0000-1000-8000-00805F9B34FB"),
        Guid.Parse("00002AF0-0000-1000-8000-00805F9B34FB"),
        Advertised: true);

    /// <summary>The service many iOS printing SDKs look for; one characteristic for writes and notifications.</summary>
    public static readonly BlePrinterProfile IosSdkE7810A71 = new(
        "E7810A71",
        Guid.Parse("E7810A71-73AE-499D-8C15-FAA9AEF0C3F2"),
        Guid.Parse("BEF8D6C9-9C21-4C9E-B632-BD58C1009F9F"),
        Guid.Parse("BEF8D6C9-9C21-4C9E-B632-BD58C1009F9F"),
        Advertised: false);

    /// <summary>Microchip/ISSC "transparent UART", used by printers built on Microchip BLE modules.</summary>
    public static readonly BlePrinterProfile MicrochipTransparentUart = new(
        "49535343",
        Guid.Parse("49535343-FE7D-4AE5-8FA9-9FAFD205E455"),
        Guid.Parse("49535343-8841-43F4-A8D4-ECBE34729BB3"),
        Guid.Parse("49535343-1E4D-4BD9-BA61-23C647249616"),
        Advertised: false);

    /// <summary>Every profile, advertised one first.</summary>
    public static IReadOnlyList<BlePrinterProfile> All { get; } = new[] { Generic18F0, IosSdkE7810A71, MicrochipTransparentUart };
}
