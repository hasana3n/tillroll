using System;
using System.Threading.Tasks;
using CrossEscPos.Core.Tests;
using CrossEscPos.Emulator;
using CrossEscPos.Transports.Bluetooth.Ble;
using Xunit;

namespace CrossEscPos.Transports.Bluetooth.Tests;

public class OptionsTests
{
    [Theory]
    [InlineData("CrossEscPos", "CrossEscPos")]
    [InlineData("A very long printer name that overflows", "A very long printer ")]   // 20 bytes
    [InlineData("Café Café Café Café", "Café Café Café Ca")]                         // é is 2 bytes; no split char
    [InlineData("", "CrossEscPos")]
    [InlineData("   ", "CrossEscPos")]
    public void AdvertisedName_FitsLegacyAdvertising(string name, string expected)
        => Assert.Equal(expected, new BluetoothPrinterOptions { Name = name }.AdvertisedName);

    [Fact]
    public void Name_DefaultsToCrossEscPos() => Assert.Equal("CrossEscPos", new BluetoothPrinterOptions().Name);

    [Fact]
    public void Profiles_MatchSpecUuids()
    {
        var p = BlePrinterProfiles.All;
        Assert.Equal(3, p.Count);
        Assert.Equal(Guid.Parse("000018F0-0000-1000-8000-00805F9B34FB"), p[0].ServiceUuid);
        Assert.Equal(Guid.Parse("00002AF1-0000-1000-8000-00805F9B34FB"), p[0].WriteUuid);
        Assert.Equal(Guid.Parse("00002AF0-0000-1000-8000-00805F9B34FB"), p[0].NotifyUuid);
        Assert.Equal(Guid.Parse("E7810A71-73AE-499D-8C15-FAA9AEF0C3F2"), p[1].ServiceUuid);
        Assert.Equal(Guid.Parse("BEF8D6C9-9C21-4C9E-B632-BD58C1009F9F"), p[1].WriteUuid);
        Assert.Equal(p[1].WriteUuid, p[1].NotifyUuid);
        Assert.Equal(Guid.Parse("49535343-FE7D-4AE5-8FA9-9FAFD205E455"), p[2].ServiceUuid);
        Assert.Equal(Guid.Parse("49535343-8841-43F4-A8D4-ECBE34729BB3"), p[2].WriteUuid);
        Assert.Equal(Guid.Parse("49535343-1E4D-4BD9-BA61-23C647249616"), p[2].NotifyUuid);
        Assert.True(p[0].Advertised); Assert.False(p[1].Advertised); Assert.False(p[2].Advertised);
    }

    [Fact]
    public void SppUuid_IsTheStandardSerialPortProfile()
        => Assert.Equal(Guid.Parse("00001101-0000-1000-8000-00805F9B34FB"), BlePrinterProfiles.SppServiceUuid);

    [Fact]
    public void Servers_HavePlainLanguageNames()
    {
        var printer = new ReceiptPrinter(PaperConfiguration.Default, new FakeImageFactory(), new FakeTypefaceProvider());
        Assert.Equal("Bluetooth (Android)", BluetoothPrinter.CreateSpp(printer, new BluetoothPrinterOptions()).DisplayName);
        Assert.Equal("Bluetooth LE (iPhone)", BluetoothPrinter.CreateBle(printer, new BluetoothPrinterOptions()).DisplayName);
    }

    [Fact]
    public async Task UnsupportedServer_ReportsWhyAndNeverRuns()
    {
        await using var server = new UnsupportedBluetoothServer("Bluetooth (Android)", "Bluetooth printer emulation isn't supported on macOS yet.");
        var support = await server.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(BluetoothSupportState.UnsupportedPlatform, support.State);
        Assert.False(support.IsSupported);
        Assert.False(server.IsRunning);
        Assert.Equal(support.Message, server.StatusText);
    }
}
