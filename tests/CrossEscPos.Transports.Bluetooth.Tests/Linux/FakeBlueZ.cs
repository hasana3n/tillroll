using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;
using Xunit;

namespace CrossEscPos.Transports.Bluetooth.Tests.Linux;

/// <summary>Where the fake-BlueZ tests run: a private D-Bus (dbus-run-session on Linux CI, or a local test daemon).</summary>
internal static class TestBus
{
    /// <summary>The bus address, or null when the fake-BlueZ tests are switched off.</summary>
    public static string? Address
        => Environment.GetEnvironmentVariable("ESCPOS_BT_FAKE_BLUEZ") == "1"
            ? Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")
            : null;

    public static string Require()
    {
        var address = Address;
        if (address is null)
            Assert.Skip("Fake-BlueZ test: set ESCPOS_BT_FAKE_BLUEZ=1 and run on a private session bus (see docs/Building-and-Testing.md).");
        return address!;
    }

    /// <summary>Handing a socket to the profile needs Unix fd passing, which only a Unix-socket bus on Linux carries.</summary>
    public static void RequireFdPassing()
    {
        if (!OperatingSystem.IsLinux() || Address is null || !Address.StartsWith("unix:", StringComparison.Ordinal))
            Assert.Skip("Needs Linux with a unix: session bus (file descriptors can't cross a TCP bus).");
    }
}

/// <summary>
/// A stand-in for bluetoothd: owns "org.bluez" on a test bus and implements just enough of BlueZ (adapter
/// properties, ProfileManager1, AgentManager1, GattManager1, LEAdvertisingManager1) for the CrossEscPos BlueZ
/// servers to register against, and lets tests drive them the way BlueZ would (NewConnection, WriteValue, …).
/// </summary>
internal sealed class FakeBlueZ : IAsyncDisposable
{
    public const string AdapterPath = "/org/bluez/hci0";

    private readonly DBusConnection _connection;
    private readonly ConcurrentQueue<(string Path, byte[] Value)> _notifications = new();
    private IDisposable? _notificationWatch;

    private FakeBlueZ(DBusConnection connection) => _connection = connection;

    public Dictionary<string, VariantValue> Adapter { get; } = new()
    {
        ["Address"] = "00:11:22:33:44:55",
        ["Alias"] = "fake-pc",
        ["Powered"] = false,
        ["Discoverable"] = false,
        ["Pairable"] = false,
        ["DiscoverableTimeout"] = 180u,
    };

    public readonly object Gate = new();
    public List<(string Sender, string Path, string Uuid, Dictionary<string, VariantValue> Options)> Profiles { get; } = new();
    public List<string> UnregisteredProfiles { get; } = new();
    public List<(string Sender, string Path, string Capability)> Agents { get; } = new();
    public List<string> DefaultAgents { get; } = new();
    public List<(string Sender, string Path)> Applications { get; } = new();
    public List<string> UnregisteredApplications { get; } = new();
    public List<(string Sender, string Path)> Advertisements { get; } = new();
    public List<string> UnregisteredAdvertisements { get; } = new();

    /// <summary>When set, RegisterAdvertisement fails with this BlueZ error name.</summary>
    public string? AdvertisementError { get; set; }

    public IReadOnlyList<(string Path, byte[] Value)> Notifications => _notifications.ToArray();

    public static async Task<FakeBlueZ> StartAsync(string address)
    {
        var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        var fake = new FakeBlueZ(connection);
        connection.AddMethodHandler(new Handler(fake, "/"));
        connection.AddMethodHandler(new Handler(fake, "/org/bluez"));
        connection.AddMethodHandler(new Handler(fake, AdapterPath));
        await connection.RequestNameAsync("org.bluez", RequestNameOptions.Default);
        return fake;
    }

    public async ValueTask DisposeAsync()
    {
        _notificationWatch?.Dispose();
        try { await _connection.ReleaseNameAsync("org.bluez"); } catch (Exception) { /* bus already gone */ }
        _connection.Dispose();
    }

    public bool AdapterBool(string name) { lock (Gate) return Adapter[name].GetBool(); }
    public uint AdapterUInt(string name) { lock (Gate) return Adapter[name].GetUInt32(); }

    // ---- driving the app the way bluetoothd would ------------------------------------------------

    /// <summary>Hands one end of a socket pair to the registered SPP profile and returns the other end (Linux only).</summary>
    public async Task<Socket> ConnectSppAsync(string devicePath)
    {
        var (sender, path) = SingleProfile();
        var (mine, theirs) = SocketPair();
        using (theirs)
        {
            var buffer = Build((ref MessageWriter w) =>
            {
                w.WriteMethodCallHeader(sender, path, "org.bluez.Profile1", "NewConnection", "oha{sv}");
                w.WriteObjectPath(devicePath);
                w.WriteHandle(theirs);
                w.WriteDictionary(new Dictionary<string, VariantValue>());
            });
            await _connection.CallMethodAsync(buffer);
        }
        return mine;
    }

    public Task RequestDisconnectionAsync(string devicePath)
    {
        var (sender, path) = SingleProfile();
        return _connection.CallMethodAsync(Build((ref MessageWriter w) =>
        {
            w.WriteMethodCallHeader(sender, path, "org.bluez.Profile1", "RequestDisconnection", "o");
            w.WriteObjectPath(devicePath);
        }));
    }

    public Task ReleaseProfileAsync()
    {
        var (sender, path) = SingleProfile();
        return _connection.CallMethodAsync(Build((ref MessageWriter w) => w.WriteMethodCallHeader(sender, path, "org.bluez.Profile1", "Release")));
    }

    /// <summary>Asks the registered agent to authorize a service connection, as bluetoothd does; returns the D-Bus error name or null.</summary>
    public async Task<string?> AuthorizeServiceAsync(string devicePath, string uuid)
    {
        (string Sender, string Path, string Capability) agent;
        lock (Gate) agent = Agents.Single();
        try
        {
            await _connection.CallMethodAsync(Build((ref MessageWriter w) =>
            {
                w.WriteMethodCallHeader(agent.Sender, agent.Path, "org.bluez.Agent1", "AuthorizeService", "os");
                w.WriteObjectPath(devicePath);
                w.WriteString(uuid);
            }));
            return null;
        }
        catch (DBusErrorReplyException ex)
        {
            return ex.ErrorName;
        }
    }

    /// <summary>The registered GATT application's objects: path → interface → properties.</summary>
    public Task<Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>>> GetApplicationObjectsAsync()
    {
        var (sender, path) = SingleApplication();
        return _connection.CallMethodAsync(
            Build((ref MessageWriter w) => w.WriteMethodCallHeader(sender, path, "org.freedesktop.DBus.ObjectManager", "GetManagedObjects")),
            (message, _) => ReadManagedObjects(message), null);
    }

    public Task<Dictionary<string, VariantValue>> GetAdvertisementPropertiesAsync()
    {
        (string Sender, string Path) adv;
        lock (Gate) adv = Advertisements.Single();
        return _connection.CallMethodAsync(
            Build((ref MessageWriter w) =>
            {
                w.WriteMethodCallHeader(adv.Sender, adv.Path, "org.freedesktop.DBus.Properties", "GetAll", "s");
                w.WriteString("org.bluez.LEAdvertisement1");
            }),
            (message, _) => message.GetBodyReader().ReadDictionaryOfStringToVariantValue(), null);
    }

    /// <summary>Path of the characteristic with this UUID in the registered application.</summary>
    public async Task<string> FindCharacteristicAsync(Guid uuid)
    {
        var objects = await GetApplicationObjectsAsync();
        return objects.Single(o => o.Value.TryGetValue("org.bluez.GattCharacteristic1", out var props)
                                   && string.Equals(props["UUID"].GetString(), uuid.ToString(), StringComparison.OrdinalIgnoreCase)).Key;
    }

    public Task WriteValueAsync(string characteristicPath, byte[] value, string device, ushort mtu, bool withoutResponse = true)
    {
        var sender = SingleApplication().Sender;
        return _connection.CallMethodAsync(Build((ref MessageWriter w) =>
        {
            w.WriteMethodCallHeader(sender, characteristicPath, "org.bluez.GattCharacteristic1", "WriteValue", "aya{sv}");
            w.WriteArray(value);
            w.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["device"] = new ObjectPath(device),
                ["mtu"] = mtu,
                ["type"] = withoutResponse ? "command" : "request",
            });
        }));
    }

    public async Task StartNotifyAsync(string characteristicPath)
    {
        var sender = SingleApplication().Sender;
        _notificationWatch ??= await _connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = sender, Interface = "org.freedesktop.DBus.Properties", Member = "PropertiesChanged" },
            (message, _) => (message.PathAsString ?? string.Empty, ReadChangedValue(message)),
            (Exception? ex, (string Path, byte[]? Value) change, object? _, object? __) =>
            {
                if (ex is null && change.Value is not null)
                    _notifications.Enqueue((change.Path, change.Value));
            },
            ObserverFlags.None, emitOnCapturedContext: false);

        await _connection.CallMethodAsync(Build((ref MessageWriter w) =>
            w.WriteMethodCallHeader(sender, characteristicPath, "org.bluez.GattCharacteristic1", "StartNotify")));
    }

    public Task StopNotifyAsync(string characteristicPath)
    {
        var sender = SingleApplication().Sender;
        return _connection.CallMethodAsync(Build((ref MessageWriter w) =>
            w.WriteMethodCallHeader(sender, characteristicPath, "org.bluez.GattCharacteristic1", "StopNotify")));
    }

    /// <summary>Announces that a device disconnected (Device1.Connected = false), like bluetoothd does.</summary>
    public void EmitDeviceDisconnected(string devicePath)
    {
        _connection.TrySendMessage(Build((ref MessageWriter w) =>
        {
            w.WriteSignalHeader(null, devicePath, "org.freedesktop.DBus.Properties", "PropertiesChanged", "sa{sv}as");
            w.WriteString("org.bluez.Device1");
            w.WriteDictionary(new Dictionary<string, VariantValue> { ["Connected"] = false });
            w.WriteArray(System.Array.Empty<string>());
        }));
    }

    public async Task<bool> WaitForNotificationsAsync(int count, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (_notifications.Count < count && Environment.TickCount64 < deadline)
            await Task.Delay(20);
        return _notifications.Count >= count;
    }

    // ---- helpers ----------------------------------------------------------------------------------

    // MessageWriter is a ref struct that keeps its own write position, so it must be passed by ref.
    private delegate void Writer(ref MessageWriter writer);

    private MessageBuffer Build(Writer write)
    {
        var writer = _connection.GetMessageWriter();
        try
        {
            write(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private (string Sender, string Path) SingleProfile()
    {
        lock (Gate) { var p = Profiles.Single(); return (p.Sender, p.Path); }
    }

    private (string Sender, string Path) SingleApplication()
    {
        lock (Gate) return Applications.Single();
    }

    private static byte[]? ReadChangedValue(Message message)
    {
        var reader = message.GetBodyReader();
        if (reader.ReadString() != "org.bluez.GattCharacteristic1")
            return null;
        var changed = reader.ReadDictionaryOfStringToVariantValue();
        return changed.TryGetValue("Value", out var value) ? value.GetArray<byte>() : null;
    }

    internal static Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>> ReadManagedObjects(Message message)
    {
        var result = new Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>>();
        var reader = message.GetBodyReader();
        var objects = reader.ReadDictionaryStart();
        while (reader.HasNext(objects))
        {
            var path = reader.ReadObjectPathAsString();
            var interfaces = new Dictionary<string, Dictionary<string, VariantValue>>();
            var ifaceEnd = reader.ReadDictionaryStart();
            while (reader.HasNext(ifaceEnd))
            {
                var name = reader.ReadString();
                interfaces[name] = reader.ReadDictionaryOfStringToVariantValue();
            }
            result[path] = interfaces;
        }
        return result;
    }

    private static (Socket Mine, SafeSocketHandle Theirs) SocketPair()
    {
        var fds = new int[2];
        if (socketpair(1 /* AF_UNIX */, 1 /* SOCK_STREAM */, 0, fds) != 0)
            throw new InvalidOperationException($"socketpair failed: errno {Marshal.GetLastPInvokeError()}");
        // Like bluetoothd (btio.c sets G_IO_FLAG_NONBLOCK on every accepted RFCOMM socket), hand the profile a
        // NON-blocking descriptor: the server has to cope with that.
        int flags = fcntl(fds[1], F_GETFL, 0);
        if (flags < 0 || fcntl(fds[1], F_SETFL, flags | O_NONBLOCK) < 0)
            throw new InvalidOperationException($"fcntl failed: errno {Marshal.GetLastPInvokeError()}");
        return (new Socket(new SafeSocketHandle(fds[0], ownsHandle: true)), new SafeSocketHandle(fds[1], ownsHandle: true));
    }

    private const int F_GETFL = 3;
    private const int F_SETFL = 4;
    private const int O_NONBLOCK = 0x800;

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int command, int argument);

    [DllImport("libc", SetLastError = true)]
    private static extern int socketpair(int domain, int type, int protocol, int[] sv);

    /// <summary>Handles calls BlueZ would receive on one object path.</summary>
    private sealed class Handler : IPathMethodHandler
    {
        private readonly FakeBlueZ _fake;

        public Handler(FakeBlueZ fake, string path)
        {
            _fake = fake;
            Path = path;
        }

        public string Path { get; }
        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request = context.Request;
            var sender = request.SenderAsString ?? string.Empty;
            var reader = request.GetBodyReader();

            switch (request.InterfaceAsString, request.MemberAsString)
            {
                case ("org.freedesktop.DBus.ObjectManager", "GetManagedObjects") when Path == "/":
                {
                    using var writer = context.CreateReplyWriter("a{oa{sa{sv}}}");
                    var objects = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart();
                    writer.WriteObjectPath(AdapterPath);
                    var ifaces = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("org.bluez.Adapter1");
                    lock (_fake.Gate) writer.WriteDictionary(new Dictionary<string, VariantValue>(_fake.Adapter));
                    writer.WriteDictionaryEnd(ifaces);
                    writer.WriteDictionaryEnd(objects);
                    context.Reply(writer.CreateMessage());
                    break;
                }
                case ("org.freedesktop.DBus.Properties", "Get") when Path == AdapterPath:
                {
                    reader.ReadString();
                    var name = reader.ReadString();
                    using var writer = context.CreateReplyWriter("v");
                    lock (_fake.Gate) writer.WriteVariant(_fake.Adapter[name]);
                    context.Reply(writer.CreateMessage());
                    break;
                }
                case ("org.freedesktop.DBus.Properties", "GetAll") when Path == AdapterPath:
                {
                    using var writer = context.CreateReplyWriter("a{sv}");
                    lock (_fake.Gate) writer.WriteDictionary(new Dictionary<string, VariantValue>(_fake.Adapter));
                    context.Reply(writer.CreateMessage());
                    break;
                }
                case ("org.freedesktop.DBus.Properties", "Set") when Path == AdapterPath:
                {
                    reader.ReadString();
                    var name = reader.ReadString();
                    var value = reader.ReadVariantValue();
                    lock (_fake.Gate) _fake.Adapter[name] = value;
                    ReplyEmpty(context);
                    break;
                }
                case ("org.bluez.ProfileManager1", "RegisterProfile"):
                {
                    var path = reader.ReadObjectPathAsString();
                    var uuid = reader.ReadString();
                    var options = reader.ReadDictionaryOfStringToVariantValue();
                    lock (_fake.Gate) _fake.Profiles.Add((sender, path, uuid, options));
                    ReplyEmpty(context);
                    break;
                }
                case ("org.bluez.ProfileManager1", "UnregisterProfile"):
                {
                    var path = reader.ReadObjectPathAsString();
                    lock (_fake.Gate) _fake.UnregisteredProfiles.Add(path);
                    ReplyEmpty(context);
                    break;
                }
                case ("org.bluez.AgentManager1", "RegisterAgent"):
                {
                    var path = reader.ReadObjectPathAsString();
                    var capability = reader.ReadString();
                    lock (_fake.Gate) _fake.Agents.Add((sender, path, capability));
                    ReplyEmpty(context);
                    break;
                }
                case ("org.bluez.AgentManager1", "RequestDefaultAgent"):
                {
                    var path = reader.ReadObjectPathAsString();
                    lock (_fake.Gate) _fake.DefaultAgents.Add(path);
                    ReplyEmpty(context);
                    break;
                }
                case ("org.bluez.AgentManager1", "UnregisterAgent"):
                    ReplyEmpty(context);
                    break;
                case ("org.bluez.GattManager1", "RegisterApplication"):
                {
                    var path = reader.ReadObjectPathAsString();
                    lock (_fake.Gate) _fake.Applications.Add((sender, path));
                    ReplyEmpty(context);
                    break;
                }
                case ("org.bluez.GattManager1", "UnregisterApplication"):
                {
                    var path = reader.ReadObjectPathAsString();
                    lock (_fake.Gate) _fake.UnregisteredApplications.Add(path);
                    ReplyEmpty(context);
                    break;
                }
                case ("org.bluez.LEAdvertisingManager1", "RegisterAdvertisement"):
                {
                    var path = reader.ReadObjectPathAsString();
                    if (_fake.AdvertisementError is { } error)
                    {
                        context.ReplyError(error, "advertising not supported by this controller");
                        break;
                    }
                    lock (_fake.Gate) _fake.Advertisements.Add((sender, path));
                    ReplyEmpty(context);
                    break;
                }
                case ("org.bluez.LEAdvertisingManager1", "UnregisterAdvertisement"):
                {
                    var path = reader.ReadObjectPathAsString();
                    lock (_fake.Gate) _fake.UnregisteredAdvertisements.Add(path);
                    ReplyEmpty(context);
                    break;
                }
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
            return default;
        }

        private static void ReplyEmpty(MethodContext context)
        {
            using var writer = context.CreateReplyWriter(null!);
            context.Reply(writer.CreateMessage());
        }
    }
}
