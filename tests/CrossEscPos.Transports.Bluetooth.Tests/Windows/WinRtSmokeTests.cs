using System;
using System.Threading.Tasks;
using CrossEscPos.Core.Tests;
using CrossEscPos.Emulator;
using Xunit;

namespace CrossEscPos.Transports.Bluetooth.Tests.Windows;

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
        await using var server = BluetoothPrinter.CreateSpp(NewPrinter(), new BluetoothPrinterOptions { Name = "CrossEscPos Test" });

        var support = await server.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(support.IsSupported, support.Message);
        Assert.True(server.IsRunning);
        Assert.Contains("Advertising serial port", server.StatusText);
        Assert.Contains("CrossEscPos Test", server.StatusText);

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
}
