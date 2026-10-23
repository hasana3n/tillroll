using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CrossEscPos.Core.Tests;
using CrossEscPos.Emulator;
using CrossEscPos.Transports.Bluetooth.Ble;
using CrossEscPos.Transports.Bluetooth.Linux;
using Xunit;

namespace CrossEscPos.Transports.Bluetooth.Tests.Linux;

/// <summary>
/// Runs the BlueZ servers against <see cref="FakeBlueZ"/> on a private D-Bus. All BlueZ tests live in this one
/// class so they never compete for the "org.bluez" name.
/// </summary>
public class BlueZIntegrationTests
{
    private const string Phone = "/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF";

    private static ReceiptPrinter NewPrinter(out FakeImageFactory factory)
    {
        factory = new FakeImageFactory();
        return new ReceiptPrinter(PaperConfiguration.Default, factory, new FakeTypefaceProvider());
    }

    private static string RenderedText(ReceiptPrinter printer, FakeImageFactory factory)
    {
        var text = new System.Collections.Generic.List<string>();
        foreach (var receipt in printer.ReceiptStack.Where(r => !r.IsEmpty))
        {
            using var _ = receipt.Render();
            text.AddRange(factory.Canvases[^1].DrawnText);
        }
        return string.Join("|", text);
    }

    private static BluetoothPrinterOptions Options(string address, bool autoPair = false)
        => new() { DBusAddress = address, AutoAcceptPairing = autoPair };

    // ---- Classic SPP ------------------------------------------------------------------------------

    [Fact]
    public async Task Spp_RegistersProfileWithSppUuidAndName()
    {
        var address = TestBus.Require();
        await using var bluez = await FakeBlueZ.StartAsync(address);
        await using var server = new BlueZSppServer(NewPrinter(out _), Options(address));

        var support = await server.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(support.IsSupported, support.Message);
        Assert.True(server.IsRunning);
        var profile = Assert.Single(bluez.Profiles);
        Assert.Equal("00001101-0000-1000-8000-00805f9b34fb", profile.Uuid);
        Assert.Equal("CrossEscPos", profile.Options["Name"].GetString());
        Assert.Equal("server", profile.Options["Role"].GetString());
        Assert.False(profile.Options["RequireAuthorization"].GetBool());
        Assert.Contains("fake-pc", server.StatusText);
    }

    [Fact]
    public async Task Spp_PreparesAdapter_AndRestoresOnStop()
    {
        var address = TestBus.Require();
        await using var bluez = await FakeBlueZ.StartAsync(address);
        await using var server = new BlueZSppServer(NewPrinter(out _), Options(address));

        await server.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(bluez.AdapterBool("Powered"));
        Assert.True(bluez.AdapterBool("Discoverable"));
        Assert.True(bluez.AdapterBool("Pairable"));
        Assert.Equal(0u, bluez.AdapterUInt("DiscoverableTimeout"));

        await server.StopAsync();
        Assert.False(server.IsRunning);
        Assert.False(bluez.AdapterBool("Powered"));
        Assert.False(bluez.AdapterBool("Discoverable"));
        Assert.False(bluez.AdapterBool("Pairable"));
        Assert.Equal(180u, bluez.AdapterUInt("DiscoverableTimeout"));
        Assert.Single(bluez.UnregisteredProfiles);
    }

    [Fact]
    public async Task Spp_ConnectionPrintsAndAnswersStatus()
    {
        var address = TestBus.Require();
        TestBus.RequireFdPassing();
        await using var bluez = await FakeBlueZ.StartAsync(address);
        var printer = NewPrinter(out var factory);
        await using var server = new BlueZSppServer(printer, Options(address));
        await server.StartAsync(TestContext.Current.CancellationToken);

        using var phone = await bluez.ConnectSppAsync(Phone);
        phone.Send(Encoding.Latin1.GetBytes("Hi\n"));
        phone.Send(new byte[] { 0x10, 0x04, 0x01 });
        var reply = new byte[8];
        phone.ReceiveTimeout = 3000;
        int read = phone.Receive(reply);

        Assert.Equal(1, read);
        Assert.Equal(1, server.ConnectedClients);
        Assert.Contains("Hi", RenderedText(printer, factory));
    }

    [Fact]
    public async Task Spp_RequestDisconnection_ClosesTheConnection()
    {
        var address = TestBus.Require();
        TestBus.RequireFdPassing();
        await using var bluez = await FakeBlueZ.StartAsync(address);
        await using var server = new BlueZSppServer(NewPrinter(out _), Options(address));
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var phone = await bluez.ConnectSppAsync(Phone);

        await bluez.RequestDisconnectionAsync(Phone);

        phone.ReceiveTimeout = 3000;
        Assert.Equal(0, phone.Receive(new byte[8]));            // orderly close
        var deadline = Environment.TickCount64 + 3000;
        while (server.ConnectedClients != 0 && Environment.TickCount64 < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Equal(0, server.ConnectedClients);
    }

    [Fact]
    public async Task Spp_ProfileRelease_ReportsStopped()
    {
        var address = TestBus.Require();
        await using var bluez = await FakeBlueZ.StartAsync(address);
        await using var server = new BlueZSppServer(NewPrinter(out _), Options(address));
        await server.StartAsync(TestContext.Current.CancellationToken);

        await bluez.ReleaseProfileAsync();

        Assert.False(server.IsRunning);
        Assert.Contains("released", server.StatusText);
    }

    [Fact]
    public async Task Spp_NoBlueZOnBus_ReportsNotRunning()
    {
        var address = TestBus.Require();
        await using var server = new BlueZSppServer(NewPrinter(out _), Options(address));

        var support = await server.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BluetoothSupportState.NoAdapter, support.State);
        Assert.Contains("bluetoothd", support.Message);
        Assert.False(server.IsRunning);
    }

    // ---- pairing agent ----------------------------------------------------------------------------

    [Fact]
    public async Task Agent_NotRegistered_WhenAutoPairOff()
    {
        var address = TestBus.Require();
        await using var bluez = await FakeBlueZ.StartAsync(address);
        await using var server = new BlueZSppServer(NewPrinter(out _), Options(address, autoPair: false));

        await server.StartAsync(TestContext.Current.CancellationToken);

        Assert.Empty(bluez.Agents);
        Assert.Empty(bluez.DefaultAgents);
    }

    [Fact]
    public async Task Agent_RegistersAsDefaultJustWorks_WhenAutoPairOn()
    {
        var address = TestBus.Require();
        await using var bluez = await FakeBlueZ.StartAsync(address);
        await using var server = new BlueZSppServer(NewPrinter(out _), Options(address, autoPair: true));

        await server.StartAsync(TestContext.Current.CancellationToken);

        var agent = Assert.Single(bluez.Agents);
        Assert.Equal("NoInputNoOutput", agent.Capability);
        Assert.Equal(agent.Path, Assert.Single(bluez.DefaultAgents));
    }
}
