using System;
using System.Threading.Tasks;
using Tillroll.Core.Tests;
using Tillroll.Emulator;
using Xunit;

namespace Tillroll.Transports.Bluetooth.Tests.Windows;

/// <summary>
/// Starts the real Windows Bluetooth servers on this PC's adapter. They make the PC discoverable and publish
/// services, so they only run when explicitly asked: set ESCPOS_BT_HARDWARE_TESTS=1.
/// </summary>
public class WinRtSmokeTests
{
    private static void SkipUnlessHardware()
    {
        if (Environment.GetEnvironmentVariable("ESCPOS_BT_HARDWARE_TESTS") != "1")
            Assert.Skip("Hardware test: set ESCPOS_BT_HARDWARE_TESTS=1 to run against this PC's Bluetooth adapter.");
    }

    private static ReceiptPrinter NewPrinter()
        => new(PaperConfiguration.Default, new FakeImageFactory(), new FakeTypefaceProvider());

    [Fact]
    public async Task Spp_StartsAndStops_OnThisPc()
    {
        SkipUnlessHardware();
        await using var server = BluetoothPrinter.CreateSpp(NewPrinter(), new BluetoothPrinterOptions { Name = "Tillroll Test" });

        var support = await server.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(support.IsSupported, support.Message);
        Assert.True(server.IsRunning);
        Assert.Contains("Advertising serial port", server.StatusText);
        Assert.Contains("Tillroll Test", server.StatusText);

        await server.StopAsync();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Spp_CanRestartAfterStop()
    {
        SkipUnlessHardware();
        await using var server = BluetoothPrinter.CreateSpp(NewPrinter(), new BluetoothPrinterOptions());

        Assert.True((await server.StartAsync(TestContext.Current.CancellationToken)).IsSupported);
        await server.StopAsync();
        var again = await server.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(again.IsSupported, again.Message);
        Assert.True(server.IsRunning);
    }

    [Fact]
    public async Task Ble_Advertises18F0_AndRegistersCompatibilityServices()
    {
        SkipUnlessHardware();
        await using var server = BluetoothPrinter.CreateBle(NewPrinter(), new BluetoothPrinterOptions());

        var support = await server.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(support.IsSupported, support.Message);
        Assert.True(server.IsRunning);
        Assert.Contains("18F0", server.StatusText);
        Assert.Contains(Environment.MachineName, server.StatusText);
        var statuses = ((Tillroll.Transports.Bluetooth.Windows.WinRtBleServer)server).PublishedServiceStatuses;
        Assert.Equal(3, statuses.Count);
        Assert.StartsWith("Started", statuses["18F0"]);
        // Windows merges discoverable services into one advertisement and the two 128-bit UUIDs don't fit,
        // so the compatibility services are registered without advertising: Windows reports them "Created".
        // Whether a phone can reach them over GATT is checked with nRF Connect (docs/Bluetooth.md).
        Assert.Equal("Created", statuses["E7810A71"]);
        Assert.Equal("Created", statuses["49535343"]);

        await server.StopAsync();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Ble_And_Spp_RunTogether()
    {
        SkipUnlessHardware();
        var printer = NewPrinter();
        await using var spp = BluetoothPrinter.CreateSpp(printer, new BluetoothPrinterOptions());
        await using var ble = BluetoothPrinter.CreateBle(printer, new BluetoothPrinterOptions());

        var sppSupport = await spp.StartAsync(TestContext.Current.CancellationToken);
        var bleSupport = await ble.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(sppSupport.IsSupported, sppSupport.Message);
        Assert.True(bleSupport.IsSupported, bleSupport.Message);
        Assert.True(spp.IsRunning && ble.IsRunning);

        await ble.StopAsync();
        await spp.StopAsync();
        Assert.False(spp.IsRunning || ble.IsRunning);
    }
}
