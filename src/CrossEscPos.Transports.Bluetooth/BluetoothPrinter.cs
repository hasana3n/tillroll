using System;
using CrossEscPos.Emulator;

namespace CrossEscPos.Transports.Bluetooth;

/// <summary>
/// Creates the Bluetooth printer servers for the current platform. Each server feeds what phones send
/// into <c>printer</c>; start it with <see cref="IBluetoothPrinterServer.StartAsync"/>.
/// </summary>
public static class BluetoothPrinter
{
    public const string SppDisplayName = "Bluetooth (Android / SPP)";
    public const string BleDisplayName = "Bluetooth LE (iPhone / BLE)";

    /// <summary>A Classic Bluetooth Serial Port Profile server (what Android printing apps use).</summary>
    public static IBluetoothPrinterServer CreateSpp(ReceiptPrinter printer, BluetoothPrinterOptions options)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(options);
#if WINDOWS_BT
        return new Windows.WinRtSppServer(printer, options);
#else
        if (OperatingSystem.IsLinux())
            return new Linux.BlueZSppServer(printer, options);
        return new UnsupportedBluetoothServer(SppDisplayName, UnsupportedReason());
#endif
    }

    /// <summary>A BLE GATT peripheral exposing the common thermal-printer services (what iPhone apps use).</summary>
    public static IBluetoothPrinterServer CreateBle(ReceiptPrinter printer, BluetoothPrinterOptions options)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(options);
#if WINDOWS_BT
        return new Windows.WinRtBleServer(printer, options);
#else
        return new UnsupportedBluetoothServer(BleDisplayName, UnsupportedReason());
#endif
    }

    private static string UnsupportedReason()
    {
        if (OperatingSystem.IsWindows())
            return "This build has no Windows Bluetooth support; use the net10.0-windows10.0.19041.0 target.";
        if (OperatingSystem.IsMacOS())
            return "Bluetooth printer emulation isn't supported on macOS yet.";
        return $"Bluetooth printer emulation isn't supported on {Environment.OSVersion.Platform} yet.";
    }
}
