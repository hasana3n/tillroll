using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrossEscPos.Emulator;
using CrossEscPos.Logging;
using CrossEscPos.Transports.Bluetooth.Ble;
using Tmds.DBus.Protocol;

namespace CrossEscPos.Transports.Bluetooth.Linux;

/// <summary>
/// BLE GATT peripheral on Linux. Exports a GATT application (the three printer services in
/// <see cref="BlePrinterProfiles"/>) and an advertisement for 0x18F0 to BlueZ over D-Bus, and reports
/// writes, notification subscriptions and disconnects to the shared <see cref="BleGattPrinter"/> engine.
/// <para>
/// BlueZ's StartNotify doesn't say which device subscribed, so subscriptions use the engine's "*" client and a
/// reply notifies every subscriber of that characteristic: fine for one phone at a time.
/// </para>
/// </summary>
internal sealed class BlueZBleServer : IBluetoothPrinterServer, IBleNotifier
{
    internal const string AppPath = "/com/crossescpos/gatt";
    internal const string AdvertisementPath = "/com/crossescpos/adv0";

    private const string GattService1 = "org.bluez.GattService1";
    private const string GattCharacteristic1 = "org.bluez.GattCharacteristic1";
    private const string GattManager1 = "org.bluez.GattManager1";
    private const string LEAdvertisingManager1 = "org.bluez.LEAdvertisingManager1";
    private const string LEAdvertisement1 = "org.bluez.LEAdvertisement1";

    private readonly ReceiptPrinter _printer;
    private readonly BluetoothPrinterOptions _options;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly List<ServiceObject> _services = new();
    private readonly List<CharacteristicObject> _characteristics = new();

    private BlueZBus? _bus;
    private BlueZAgent? _agent;
    private BleGattPrinter? _engine;
    private IDisposable? _deviceWatch;
    private IDisposable? _lossWatch;
    private string? _adapter;
    private bool _applicationRegistered;
    private bool _advertisementRegistered;

    public BlueZBleServer(ReceiptPrinter printer, BluetoothPrinterOptions options)
    {
        _printer = printer;
        _options = options;
        StatusText = "Stopped.";
        BuildObjects();
    }

    public string DisplayName => BluetoothPrinter.BleDisplayName;
    public bool IsRunning { get; private set; }
    public int ConnectedClients => _engine?.ConnectedClients ?? 0;
    public string StatusText { get; private set; }
    public event Action? StatusChanged;

    public async Task<BluetoothSupport> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
                return BluetoothSupport.Ok(StatusText);

            try
            {
                _bus = await BlueZBus.ConnectAsync(_options.DBusAddress).ConfigureAwait(false);
                _adapter = await _bus.FindAdapterAsync(_options.AdapterName).ConfigureAwait(false);
                if (_adapter is null)
                    return await FailAsync(new BluetoothSupport(BluetoothSupportState.NoAdapter,
                        _options.AdapterName is null ? "No Bluetooth adapter found." : $"Bluetooth adapter {_options.AdapterName} not found.")).ConfigureAwait(false);

                await _bus.PrepareAdapterAsync(_adapter).ConfigureAwait(false);

                if (_options.AutoAcceptPairing)
                {
                    _agent = new BlueZAgent(_bus);
                    await _agent.StartAsync(makeDefault: true).ConfigureAwait(false);
                }

                _engine = new BleGattPrinter(_printer, this);
                _engine.ClientsChanged += OnClientsChanged;
                foreach (var c in _characteristics)
                    c.Notifying = false;

                _bus.Connection.AddMethodHandler(new ApplicationHandler(this));
                _bus.Connection.AddMethodHandler(new AdvertisementHandler(this));
                _deviceWatch = await WatchDeviceDisconnectsAsync(_bus).ConfigureAwait(false);
                _lossWatch = await _bus.WatchForLossAsync(_adapter, OnBlueZLost).ConfigureAwait(false);

                await _bus.CallAsync(_adapter, GattManager1, "RegisterApplication", "oa{sv}", (ref MessageWriter w) =>
                {
                    w.WriteObjectPath(AppPath);
                    w.WriteDictionary(new Dictionary<string, VariantValue>());
                }).ConfigureAwait(false);
                _applicationRegistered = true;

                await _bus.CallAsync(_adapter, LEAdvertisingManager1, "RegisterAdvertisement", "oa{sv}", (ref MessageWriter w) =>
                {
                    w.WriteObjectPath(AdvertisementPath);
                    w.WriteDictionary(new Dictionary<string, VariantValue>());
                }).ConfigureAwait(false);
                _advertisementRegistered = true;

                Logger.Info($"BLE printer services registered with BlueZ; advertising 0x18F0 as \"{_options.AdvertisedName}\"");
                SetStatus(true, RunningStatus());
                return BluetoothSupport.Ok(StatusText);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Failed to start the BlueZ BLE printer");
                return await FailAsync(BlueZBus.MapError(ex)).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_bus is null)
                return;
            await TearDownAsync().ConfigureAwait(false);
            SetStatus(false, "Stopped.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    // ---- IBleNotifier -----------------------------------------------------------------------------

    public Task NotifyAsync(BlePrinterProfile profile, string clientId, ReadOnlyMemory<byte> chunk)
    {
        var bus = _bus;
        var characteristic = _characteristics.FirstOrDefault(c => c.Profile == profile && c.Notifies);
        if (bus is null || characteristic is null || !characteristic.Notifying)
            return Task.CompletedTask;

        var bytes = chunk.ToArray();
        bus.Connection.TrySendMessage(bus.Build((ref MessageWriter w) =>
        {
            w.WriteSignalHeader(null, characteristic.Path, BlueZBus.PropertiesInterface, "PropertiesChanged", "sa{sv}as");
            w.WriteString(GattCharacteristic1);
            w.WriteDictionary(new Dictionary<string, VariantValue> { ["Value"] = VariantValue.Array(bytes) });
            w.WriteArray(Array.Empty<string>());
        }));
        return Task.CompletedTask;
    }

    // ---- object model -----------------------------------------------------------------------------

    private void BuildObjects()
    {
        int s = 0;
        foreach (var profile in BlePrinterProfiles.All)
        {
            var service = new ServiceObject($"{AppPath}/service{s++}", profile);
            _services.Add(service);

            if (profile.SharedCharacteristic)
            {
                _characteristics.Add(new CharacteristicObject($"{service.Path}/char0", service.Path, profile, profile.WriteUuid,
                    new[] { "read", "write", "write-without-response", "notify" }, writes: true, notifies: true));
            }
            else
            {
                _characteristics.Add(new CharacteristicObject($"{service.Path}/char0", service.Path, profile, profile.WriteUuid,
                    new[] { "write", "write-without-response" }, writes: true, notifies: false));
                _characteristics.Add(new CharacteristicObject($"{service.Path}/char1", service.Path, profile, profile.NotifyUuid,
                    new[] { "notify" }, writes: false, notifies: true));
            }
        }
    }

    private Dictionary<string, VariantValue> ServiceProperties(ServiceObject service) => new()
    {
        ["UUID"] = service.Profile.ServiceUuid.ToString(),
        ["Primary"] = true,
    };

    private static Dictionary<string, VariantValue> CharacteristicProperties(CharacteristicObject c) => new()
    {
        ["UUID"] = c.Uuid.ToString(),
        ["Service"] = new ObjectPath(c.ServicePath),
        ["Flags"] = VariantValue.Array(c.Flags),
        ["Notifying"] = c.Notifying,
    };

    private Dictionary<string, VariantValue> AdvertisementProperties() => new()
    {
        ["Type"] = "peripheral",
        ["ServiceUUIDs"] = VariantValue.Array(new[] { BlePrinterProfiles.Generic18F0.ServiceUuid.ToString() }),
        ["LocalName"] = _options.AdvertisedName,
        ["Discoverable"] = true,
    };

    // ---- lifecycle helpers ------------------------------------------------------------------------

    private async Task<IDisposable> WatchDeviceDisconnectsAsync(BlueZBus bus)
        => await bus.Connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Sender = BlueZBus.Service,
                Interface = BlueZBus.PropertiesInterface,
                Member = "PropertiesChanged",
                Arg0 = "org.bluez.Device1",
            },
            (message, _) => (message.PathAsString ?? string.Empty, ReadDisconnected(message)),
            (Exception? ex, (string Path, bool Disconnected) change, object? _, object? __) =>
            {
                if (ex is null && change.Disconnected)
                    _engine?.OnDisconnected(change.Path);
            },
            ObserverFlags.None, emitOnCapturedContext: false).ConfigureAwait(false);

    private static bool ReadDisconnected(Message message)
    {
        var reader = message.GetBodyReader();
        reader.ReadString();
        var changed = reader.ReadDictionaryOfStringToVariantValue();
        return changed.TryGetValue("Connected", out var connected) && !connected.GetBool();
    }

    private async Task TearDownAsync()
    {
        var bus = _bus;
        if (bus is not null && _adapter is not null)
        {
            if (_advertisementRegistered)
            {
                try
                {
                    await bus.CallAsync(_adapter, LEAdvertisingManager1, "UnregisterAdvertisement", "o",
                        (ref MessageWriter w) => w.WriteObjectPath(AdvertisementPath)).ConfigureAwait(false);
                }
                catch (Exception ex) { Logger.Info($"UnregisterAdvertisement: {ex.Message}"); }
            }
            if (_applicationRegistered)
            {
                try
                {
                    await bus.CallAsync(_adapter, GattManager1, "UnregisterApplication", "o",
                        (ref MessageWriter w) => w.WriteObjectPath(AppPath)).ConfigureAwait(false);
                }
                catch (Exception ex) { Logger.Info($"UnregisterApplication: {ex.Message}"); }
            }
        }
        _advertisementRegistered = false;
        _applicationRegistered = false;

        _deviceWatch?.Dispose();
        _deviceWatch = null;
        _lossWatch?.Dispose();
        _lossWatch = null;

        if (_engine is not null)
        {
            _engine.ClientsChanged -= OnClientsChanged;
            await _engine.DisposeAsync().ConfigureAwait(false);
            _engine = null;
        }

        if (_agent is not null)
        {
            await _agent.DisposeAsync().ConfigureAwait(false);
            _agent = null;
        }

        if (bus is not null)
        {
            bus.Connection.RemoveMethodHandlers(new[] { AppPath, AdvertisementPath });
            await bus.RestoreAdapterAsync().ConfigureAwait(false);
            await bus.DisposeAsync().ConfigureAwait(false);
            _bus = null;
        }
        IsRunning = false;
    }

    /// <summary>The adapter or bluetoothd went away: say so at once, then clean up in the background.</summary>
    private void OnBlueZLost(string message)
    {
        if (!IsRunning)
            return;
        Logger.Info($"{DisplayName}: {message}");
        SetStatus(false, message);
        _ = Task.Run(async () =>
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_bus is not null)
                    await TearDownAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycle.Release();
            }
            SetStatus(false, message);
        });
    }

    private async Task<BluetoothSupport> FailAsync(BluetoothSupport support)
    {
        await TearDownAsync().ConfigureAwait(false);
        SetStatus(false, support.Message);
        return support;
    }

    private void OnClientsChanged()
    {
        if (IsRunning)
            SetStatus(true, RunningStatus());
    }

    private string RunningStatus()
        => $"Advertising BLE printer service 18F0 as \"{_options.AdvertisedName}\" ({ConnectedClients} connected).";

    private void SetStatus(bool running, string text)
    {
        IsRunning = running;
        StatusText = text;
        StatusChanged?.Invoke();
    }

    private static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null!);
        context.Reply(writer.CreateMessage());
    }

    private static void ReplyProperties(MethodContext context, Dictionary<string, VariantValue>? properties, string iface)
    {
        if (properties is null)
        {
            context.ReplyError("org.freedesktop.DBus.Error.UnknownInterface", $"No interface {iface}");
            return;
        }
        using var writer = context.CreateReplyWriter("a{sv}");
        writer.WriteDictionary(properties);
        context.Reply(writer.CreateMessage());
    }

    private static void ReplyProperty(MethodContext context, Dictionary<string, VariantValue>? properties, string name)
    {
        if (properties is null || !properties.TryGetValue(name, out var value))
        {
            context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"No property {name}");
            return;
        }
        using var writer = context.CreateReplyWriter("v");
        writer.WriteVariant(value);
        context.Reply(writer.CreateMessage());
    }

    private sealed record ServiceObject(string Path, BlePrinterProfile Profile);

    private sealed class CharacteristicObject
    {
        public CharacteristicObject(string path, string servicePath, BlePrinterProfile profile, Guid uuid, string[] flags, bool writes, bool notifies)
        {
            Path = path;
            ServicePath = servicePath;
            Profile = profile;
            Uuid = uuid;
            Flags = flags;
            Writes = writes;
            Notifies = notifies;
        }

        public string Path { get; }
        public string ServicePath { get; }
        public BlePrinterProfile Profile { get; }
        public Guid Uuid { get; }
        public string[] Flags { get; }
        public bool Writes { get; }
        public bool Notifies { get; }
        public volatile bool Notifying;
    }

    /// <summary>The GATT application tree: ObjectManager on the root, services and characteristics below it.</summary>
    private sealed class ApplicationHandler : IPathMethodHandler
    {
        private static readonly ReadOnlyMemory<byte> RootXml = Encoding.UTF8.GetBytes(
            """<interface name="org.freedesktop.DBus.ObjectManager"><method name="GetManagedObjects"><arg type="a{oa{sa{sv}}}" direction="out"/></method></interface>""");

        private static readonly ReadOnlyMemory<byte> CharacteristicXml = Encoding.UTF8.GetBytes("""
            <interface name="org.bluez.GattCharacteristic1">
              <method name="ReadValue"><arg type="a{sv}" direction="in"/><arg type="ay" direction="out"/></method>
              <method name="WriteValue"><arg type="ay" direction="in"/><arg type="a{sv}" direction="in"/></method>
              <method name="StartNotify"/>
              <method name="StopNotify"/>
            </interface>
            """);

        private readonly BlueZBleServer _server;

        public ApplicationHandler(BlueZBleServer server) => _server = server;

        public string Path => AppPath;
        public bool HandlesChildPaths => true;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request = context.Request;
            var path = request.PathAsString ?? string.Empty;

            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml(path == AppPath ? new[] { RootXml }
                    : _server._characteristics.Any(c => c.Path == path) ? new[] { CharacteristicXml }
                    : Array.Empty<ReadOnlyMemory<byte>>());
                return default;
            }

            switch (request.InterfaceAsString, request.MemberAsString)
            {
                case ("org.freedesktop.DBus.ObjectManager", "GetManagedObjects") when path == AppPath:
                    ReplyManagedObjects(context);
                    break;
                case (BlueZBus.PropertiesInterface, "GetAll"):
                {
                    var iface = request.GetBodyReader().ReadString();
                    ReplyProperties(context, PropertiesOf(path, iface), iface);
                    break;
                }
                case (BlueZBus.PropertiesInterface, "Get"):
                {
                    var reader = request.GetBodyReader();
                    var iface = reader.ReadString();
                    var name = reader.ReadString();
                    ReplyProperty(context, PropertiesOf(path, iface), name);
                    break;
                }
                case (GattCharacteristic1, var member) when _server._characteristics.FirstOrDefault(c => c.Path == path) is { } characteristic:
                    HandleCharacteristic(context, characteristic, member ?? string.Empty);
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
            return default;
        }

        private Dictionary<string, VariantValue>? PropertiesOf(string path, string iface)
        {
            if (iface == GattService1 && _server._services.FirstOrDefault(s => s.Path == path) is { } service)
                return _server.ServiceProperties(service);
            if (iface == GattCharacteristic1 && _server._characteristics.FirstOrDefault(c => c.Path == path) is { } characteristic)
                return CharacteristicProperties(characteristic);
            return null;
        }

        private void ReplyManagedObjects(MethodContext context)
        {
            // MessageWriter is a ref struct that keeps its own write position: pass it by ref, never by value.
            var writer = context.CreateReplyWriter("a{oa{sa{sv}}}");
            try
            {
                var objects = writer.WriteDictionaryStart();
                foreach (var service in _server._services)
                    WriteObject(ref writer, service.Path, GattService1, _server.ServiceProperties(service));
                foreach (var characteristic in _server._characteristics)
                    WriteObject(ref writer, characteristic.Path, GattCharacteristic1, CharacteristicProperties(characteristic));
                writer.WriteDictionaryEnd(objects);
                context.Reply(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }

        private static void WriteObject(ref MessageWriter writer, string path, string iface, Dictionary<string, VariantValue> properties)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteObjectPath(path);
            var interfaces = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString(iface);
            writer.WriteDictionary(properties);
            writer.WriteDictionaryEnd(interfaces);
        }

        private void HandleCharacteristic(MethodContext context, CharacteristicObject characteristic, string member)
        {
            var engine = _server._engine;
            switch (member)
            {
                case "ReadValue":
                {
                    using var writer = context.CreateReplyWriter("ay");
                    writer.WriteArray(Array.Empty<byte>());
                    context.Reply(writer.CreateMessage());
                    break;
                }
                case "WriteValue" when characteristic.Writes:
                {
                    var reader = context.Request.GetBodyReader();
                    var value = reader.ReadArrayOfByte();
                    var options = reader.ReadDictionaryOfStringToVariantValue();
                    var device = options.TryGetValue("device", out var d) ? d.GetObjectPathAsString() : "unknown-device";
                    int mtu = options.TryGetValue("mtu", out var m) ? m.GetUInt16() : BleChunker.DefaultAttMtu;
                    engine?.OnWrite(device, characteristic.Profile, value, mtu);
                    ReplyEmpty(context);
                    break;
                }
                case "StartNotify" when characteristic.Notifies:
                    characteristic.Notifying = true;
                    engine?.OnSubscribed(BleGattPrinter.AnyClient, characteristic.Profile, BleChunker.DefaultAttMtu);
                    ReplyEmpty(context);
                    break;
                case "StopNotify" when characteristic.Notifies:
                    characteristic.Notifying = false;
                    engine?.OnUnsubscribed(BleGattPrinter.AnyClient, characteristic.Profile);
                    ReplyEmpty(context);
                    break;
                default:
                    context.ReplyError("org.bluez.Error.NotSupported", $"{member} isn't supported on this characteristic");
                    break;
            }
        }
    }

    /// <summary>The LEAdvertisement1 object BlueZ reads the advertisement from.</summary>
    private sealed class AdvertisementHandler : IPathMethodHandler
    {
        private readonly BlueZBleServer _server;

        public AdvertisementHandler(BlueZBleServer server) => _server = server;

        public string Path => AdvertisementPath;
        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request = context.Request;
            switch (request.InterfaceAsString, request.MemberAsString)
            {
                case (BlueZBus.PropertiesInterface, "GetAll"):
                {
                    var iface = request.GetBodyReader().ReadString();
                    ReplyProperties(context, iface == LEAdvertisement1 ? _server.AdvertisementProperties() : null, iface);
                    break;
                }
                case (BlueZBus.PropertiesInterface, "Get"):
                {
                    var reader = request.GetBodyReader();
                    var iface = reader.ReadString();
                    var name = reader.ReadString();
                    ReplyProperty(context, iface == LEAdvertisement1 ? _server.AdvertisementProperties() : null, name);
                    break;
                }
                case (LEAdvertisement1, "Release"):
                    Logger.Info("BlueZ released the BLE advertisement");
                    _server._advertisementRegistered = false;
                    ReplyEmpty(context);
                    _server.OnBlueZLost("BlueZ stopped advertising the printer (the adapter went off or ran out of advertising slots). Start again.");
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
            return default;
        }
    }
}
