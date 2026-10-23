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

    // ---- BLE GATT peripheral ----------------------------------------------------------------------

    private static async Task<(FakeBlueZ BlueZ, BlueZBleServer Server, ReceiptPrinter Printer, FakeImageFactory Factory)> StartBleAsync(
        string address, string name = "CrossEscPos")
    {
        var bluez = await FakeBlueZ.StartAsync(address);
        var printer = NewPrinter(out var factory);
        var server = new BlueZBleServer(printer, new BluetoothPrinterOptions { DBusAddress = address, Name = name });
        var support = await server.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(support.IsSupported, support.Message);
        return (bluez, server, printer, factory);
    }

    [Fact]
    public async Task Ble_ExportsThreeServicesWithSpecUuidsAndFlags()
    {
        var address = TestBus.Require();
        var (bluez, server, _, _) = await StartBleAsync(address);
        await using var _1 = bluez;
        await using var _2 = server;

        var objects = await bluez.GetApplicationObjectsAsync();

        var services = objects.Values.Where(o => o.ContainsKey("org.bluez.GattService1"))
            .Select(o => o["org.bluez.GattService1"]["UUID"].GetString().ToUpperInvariant()).OrderBy(u => u).ToArray();
        Assert.Equal(new[] { "000018F0-0000-1000-8000-00805F9B34FB", "49535343-FE7D-4AE5-8FA9-9FAFD205E455", "E7810A71-73AE-499D-8C15-FAA9AEF0C3F2" }, services);
        Assert.All(objects.Values.Where(o => o.ContainsKey("org.bluez.GattService1")), o => Assert.True(o["org.bluez.GattService1"]["Primary"].GetBool()));

        string[] Flags(Guid uuid) => objects.Values.Single(o => o.TryGetValue("org.bluez.GattCharacteristic1", out var c)
                && string.Equals(c["UUID"].GetString(), uuid.ToString(), StringComparison.OrdinalIgnoreCase))
            ["org.bluez.GattCharacteristic1"]["Flags"].GetArray<string>();
        Assert.Equal(new[] { "write", "write-without-response" }, Flags(BlePrinterProfiles.Generic18F0.WriteUuid));
        Assert.Equal(new[] { "notify" }, Flags(BlePrinterProfiles.Generic18F0.NotifyUuid));
        Assert.Equal(new[] { "read", "write", "write-without-response", "notify" }, Flags(BlePrinterProfiles.IosSdkE7810A71.WriteUuid));
        Assert.Equal(new[] { "notify" }, Flags(BlePrinterProfiles.MicrochipTransparentUart.NotifyUuid));
        Assert.True(server.IsRunning);
    }

    [Fact]
    public async Task Ble_AdvertisesOnly18F0_WithTrimmedName()
    {
        var address = TestBus.Require();
        var (bluez, server, _, _) = await StartBleAsync(address, name: "A very long printer name that overflows");
        await using var _1 = bluez;
        await using var _2 = server;

        var adv = await bluez.GetAdvertisementPropertiesAsync();

        Assert.Equal("peripheral", adv["Type"].GetString());
        Assert.Equal(new[] { "000018f0-0000-1000-8000-00805f9b34fb" }, adv["ServiceUUIDs"].GetArray<string>());
        Assert.Equal("A very long printer ", adv["LocalName"].GetString());
        Assert.Contains("A very long printer ", server.StatusText);
    }

    [Fact]
    public async Task Ble_WritePrints_AndStatusIsNotified()
    {
        var address = TestBus.Require();
        var (bluez, server, printer, factory) = await StartBleAsync(address);
        await using var _1 = bluez;
        await using var _2 = server;
        var write = await bluez.FindCharacteristicAsync(BlePrinterProfiles.Generic18F0.WriteUuid);
        var notify = await bluez.FindCharacteristicAsync(BlePrinterProfiles.Generic18F0.NotifyUuid);

        await bluez.StartNotifyAsync(notify);
        await bluez.WriteValueAsync(write, Encoding.Latin1.GetBytes("Hi\n"), Phone, 185);
        await bluez.WriteValueAsync(write, new byte[] { 0x10, 0x04, 0x01 }, Phone, 185);

        Assert.True(await bluez.WaitForNotificationsAsync(1));
        var note = Assert.Single(bluez.Notifications);
        Assert.Equal(notify, note.Path);
        Assert.Single(note.Value);
        Assert.Equal(1, server.ConnectedClients);
        Assert.Contains("Hi", RenderedText(printer, factory));
    }

    [Fact]
    public async Task Ble_LongerReply_ArrivesComplete()
    {
        var address = TestBus.Require();
        var (bluez, server, _, _) = await StartBleAsync(address);
        await using var _1 = bluez;
        await using var _2 = server;
        var shared = await bluez.FindCharacteristicAsync(BlePrinterProfiles.IosSdkE7810A71.WriteUuid);

        await bluez.StartNotifyAsync(shared);
        await bluez.WriteValueAsync(shared, new byte[] { 0x1D, 0x49, 0x42 }, Phone, 23);   // GS I 66: maker name

        Assert.True(await bluez.WaitForNotificationsAsync(1));
        var bytes = bluez.Notifications.SelectMany(n => n.Value).ToArray();
        Assert.Equal("_CrossEscPos\0", Encoding.ASCII.GetString(bytes));
        Assert.All(bluez.Notifications, n => Assert.True(n.Value.Length <= 20));
    }

    [Fact]
    public async Task Ble_DeviceDisconnect_ForgetsClient()
    {
        var address = TestBus.Require();
        var (bluez, server, _, _) = await StartBleAsync(address);
        await using var _1 = bluez;
        await using var _2 = server;
        var write = await bluez.FindCharacteristicAsync(BlePrinterProfiles.Generic18F0.WriteUuid);
        await bluez.WriteValueAsync(write, Encoding.Latin1.GetBytes("x"), Phone, 23);
        Assert.Equal(1, server.ConnectedClients);

        bluez.EmitDeviceDisconnected(Phone);

        var deadline = Environment.TickCount64 + 3000;
        while (server.ConnectedClients != 0 && Environment.TickCount64 < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Equal(0, server.ConnectedClients);
    }

    [Fact]
    public async Task Ble_StopUnregistersEverything_AndRestoresAdapter()
    {
        var address = TestBus.Require();
        var (bluez, server, _, _) = await StartBleAsync(address);
        await using var _1 = bluez;

        await server.StopAsync();

        Assert.False(server.IsRunning);
        Assert.Single(bluez.UnregisteredAdvertisements);
        Assert.Single(bluez.UnregisteredApplications);
        Assert.False(bluez.AdapterBool("Discoverable"));
    }

    [Fact]
    public async Task Ble_AdvertisingNotSupported_ReportsAdapterLimit()
    {
        var address = TestBus.Require();
        await using var bluez = await FakeBlueZ.StartAsync(address);
        bluez.AdvertisementError = "org.bluez.Error.NotSupported";
        await using var server = new BlueZBleServer(NewPrinter(out _), Options(address));

        var support = await server.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BluetoothSupportState.NotSupportedByAdapter, support.State);
        Assert.False(server.IsRunning);
        Assert.Single(bluez.UnregisteredApplications);           // the half-done registration was undone
    }
}
