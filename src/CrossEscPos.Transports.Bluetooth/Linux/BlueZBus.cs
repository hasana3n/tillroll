using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossEscPos.Logging;
using Tmds.DBus.Protocol;

namespace CrossEscPos.Transports.Bluetooth.Linux;

/// <summary>
/// The D-Bus connection to BlueZ (bluetoothd), plus the few calls every BlueZ server needs: finding the
/// adapter, reading and writing its properties, and putting back any adapter setting we changed.
/// </summary>
internal sealed class BlueZBus : IAsyncDisposable
{
    public const string Service = "org.bluez";
    public const string Adapter1 = "org.bluez.Adapter1";
    public const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    public const string ObjectManagerInterface = "org.freedesktop.DBus.ObjectManager";

    // Set in this order (timeout before discoverable, so it doesn't time out); restored in reverse.
    private static readonly (string Name, VariantValue Value)[] PrinterAdapterSettings =
    {
        ("Powered", true),
        ("DiscoverableTimeout", 0u),
        ("Pairable", true),
        ("Discoverable", true),
    };

    private readonly string _address;
    private string? _adapterLease; // set while this connection holds a share of the adapter settings

    private BlueZBus(DBusConnection connection, string address)
    {
        Connection = connection;
        _address = address;
    }

    public DBusConnection Connection { get; }

    /// <summary>Writes a message (header and body). MessageWriter is a ref struct with its own state, so it's passed by ref.</summary>
    public delegate void BodyWriter(ref MessageWriter writer);

    public static async Task<BlueZBus> ConnectAsync(string? address)
    {
        address ??= DBusAddress.System ?? "unix:path=/var/run/dbus/system_bus_socket";
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        return new BlueZBus(connection, address);
    }

    public ValueTask DisposeAsync()
    {
        Connection.Dispose();
        return ValueTask.CompletedTask;
    }

    public MessageBuffer Build(BodyWriter write)
    {
        var writer = Connection.GetMessageWriter();
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

    /// <summary>Calls a BlueZ method that returns nothing.</summary>
    public Task CallAsync(string path, string iface, string member, string? signature = null, BodyWriter? body = null)
        => Connection.CallMethodAsync(Build((ref MessageWriter w) =>
        {
            w.WriteMethodCallHeader(Service, path, iface, member, signature);
            body?.Invoke(ref w);
        }));

    /// <summary>The first adapter BlueZ reports (or the one named <paramref name="name"/>, e.g. "hci1"), or null.</summary>
    public async Task<string?> FindAdapterAsync(string? name)
    {
        var objects = await Connection.CallMethodAsync(
            Build((ref MessageWriter w) => w.WriteMethodCallHeader(Service, "/", ObjectManagerInterface, "GetManagedObjects")),
            (message, _) => ReadAdapterPaths(message), null).ConfigureAwait(false);

        return name is null
            ? objects.OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault()
            : objects.FirstOrDefault(p => p.EndsWith("/" + name, StringComparison.Ordinal));
    }

    public Task<VariantValue> GetAsync(string path, string iface, string property)
        => Connection.CallMethodAsync(
            Build((ref MessageWriter w) =>
            {
                w.WriteMethodCallHeader(Service, path, PropertiesInterface, "Get", "ss");
                w.WriteString(iface);
                w.WriteString(property);
            }),
            (message, _) => message.GetBodyReader().ReadVariantValue(), null);

    public Task SetAsync(string path, string iface, string property, VariantValue value)
        => CallAsync(path, PropertiesInterface, "Set", "ssv", (ref MessageWriter w) =>
        {
            w.WriteString(iface);
            w.WriteString(property);
            w.WriteVariant(value);
        });

    /// <summary>
    /// Powers the adapter and makes it discoverable and pairable with no timeout. The settings are shared by
    /// every server in this process using the adapter (the SPP and BLE rows): the first one to start changes
    /// them and remembers the originals; only the last one to stop puts them back.
    /// </summary>
    public async Task PrepareAdapterAsync(string adapterPath)
    {
        if (_adapterLease is not null)
            return;
        var key = _address + "|" + adapterPath;
        await SharedAdapterSettings.AcquireAsync(this, key, adapterPath).ConfigureAwait(false);
        _adapterLease = key;
    }

    /// <summary>Gives up this connection's share; restores the original settings if it was the last. Never throws.</summary>
    public async Task RestoreAdapterAsync()
    {
        var key = _adapterLease;
        _adapterLease = null;
        if (key is not null)
            await SharedAdapterSettings.ReleaseAsync(this, key).ConfigureAwait(false);
    }

    /// <summary>Applies the printer settings to an adapter and returns the originals of what it changed.</summary>
    private async Task<List<(string Name, VariantValue Original)>> ApplyPrinterSettingsAsync(string adapterPath)
    {
        var changed = new List<(string Name, VariantValue Original)>();
        try
        {
            foreach (var (name, desired) in PrinterAdapterSettings)
            {
                var current = await GetAsync(adapterPath, Adapter1, name).ConfigureAwait(false);
                if (SameValue(current, desired))
                    continue;
                await SetAsync(adapterPath, Adapter1, name, desired).ConfigureAwait(false);
                changed.Add((name, current));
                Logger.Info($"BlueZ adapter {adapterPath}: {name} set for the printer");
            }
            return changed;
        }
        catch
        {
            await RestoreSettingsAsync(adapterPath, changed).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RestoreSettingsAsync(string adapterPath, List<(string Name, VariantValue Original)> changed)
    {
        for (int i = changed.Count - 1; i >= 0; i--)
        {
            var (name, original) = changed[i];
            try
            {
                await SetAsync(adapterPath, Adapter1, name, original).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"Couldn't restore adapter setting {name}");
            }
        }
    }

    /// <summary>Process-wide, reference-counted record of the adapter settings changed for the printer.</summary>
    private static class SharedAdapterSettings
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly Dictionary<string, Entry> Entries = new();

        public static async Task AcquireAsync(BlueZBus bus, string key, string adapterPath)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Entries.TryGetValue(key, out var entry))
                {
                    var changed = await bus.ApplyPrinterSettingsAsync(adapterPath).ConfigureAwait(false);
                    Entries[key] = entry = new Entry(adapterPath, changed);
                }
                entry.Users++;
            }
            finally
            {
                Gate.Release();
            }
        }

        public static async Task ReleaseAsync(BlueZBus bus, string key)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Entries.TryGetValue(key, out var entry) || --entry.Users > 0)
                    return;
                Entries.Remove(key);
                await bus.RestoreSettingsAsync(entry.AdapterPath, entry.Changed).ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }

        private sealed class Entry
        {
            public Entry(string adapterPath, List<(string Name, VariantValue Original)> changed)
            {
                AdapterPath = adapterPath;
                Changed = changed;
            }

            public string AdapterPath { get; }
            public List<(string Name, VariantValue Original)> Changed { get; }
            public int Users;
        }
    }

    public const string AdapterTurnedOff = "Bluetooth was turned off. Turn it back on, then start again.";
    public const string BluetoothdStopped = "BlueZ (bluetoothd) stopped. Start the bluetooth service, then start again.";

    /// <summary>
    /// Calls <paramref name="onLost"/> with a plain-language reason when the adapter is powered off or bluetoothd
    /// goes away, so a server never keeps saying it's running when phones can't reach it.
    /// </summary>
    public async Task<IDisposable> WatchForLossAsync(string adapterPath, Action<string> onLost)
    {
        var powered = await Connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Sender = Service,
                Path = adapterPath,
                Interface = PropertiesInterface,
                Member = "PropertiesChanged",
                Arg0 = Adapter1,
            },
            (message, _) => IsPoweredOff(message),
            (Exception? ex, bool off, object? _, object? __) =>
            {
                if (ex is null && off)
                    onLost(AdapterTurnedOff);
            },
            ObserverFlags.None, emitOnCapturedContext: false).ConfigureAwait(false);

        var owner = await Connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Sender = "org.freedesktop.DBus",
                Interface = "org.freedesktop.DBus",
                Member = "NameOwnerChanged",
                Arg0 = Service,
            },
            (message, _) =>
            {
                var reader = message.GetBodyReader();
                reader.ReadString();
                reader.ReadString();
                return reader.ReadString(); // new owner; empty when bluetoothd went away
            },
            (Exception? ex, string newOwner, object? _, object? __) =>
            {
                if (ex is null && newOwner.Length == 0)
                    onLost(BluetoothdStopped);
            },
            ObserverFlags.None, emitOnCapturedContext: false).ConfigureAwait(false);

        return new CompositeDisposable(powered, owner);
    }

    private static bool IsPoweredOff(Message message)
    {
        var reader = message.GetBodyReader();
        reader.ReadString();
        var changed = reader.ReadDictionaryOfStringToVariantValue();
        return changed.TryGetValue("Powered", out var powered) && !powered.GetBool();
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _parts;
        public CompositeDisposable(params IDisposable[] parts) => _parts = parts;
        public void Dispose()
        {
            foreach (var part in _parts)
                part.Dispose();
        }
    }

    /// <summary>Turns a D-Bus failure into a plain-language reason for the status line.</summary>
    public static BluetoothSupport MapError(Exception ex) => ex switch
    {
        DBusErrorReplyException { ErrorName: "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner" }
            => new(BluetoothSupportState.NoAdapter, "BlueZ (bluetoothd) isn't running. Start the bluetooth service and try again."),
        DBusErrorReplyException { ErrorName: "org.freedesktop.DBus.Error.AccessDenied" or "org.bluez.Error.NotAuthorized" or "org.bluez.Error.NotPermitted" }
            => new(BluetoothSupportState.PermissionDenied, "Access to Bluetooth was denied. Add your user to the 'bluetooth' group, then log in again."),
        DBusErrorReplyException { ErrorName: "org.bluez.Error.NotSupported" } e
            => new(BluetoothSupportState.NotSupportedByAdapter, $"This Bluetooth adapter doesn't support it: {e.ErrorMessage}"),
        DBusErrorReplyException { ErrorName: "org.bluez.Error.NotReady" }
            => new(BluetoothSupportState.AdapterOff, "The Bluetooth adapter is off or blocked (check 'rfkill list')."),
        DBusErrorReplyException e
            => new(BluetoothSupportState.Error, $"BlueZ refused: {e.ErrorMessage} ({e.ErrorName})"),
        DBusConnectFailedException or DBusConnectionException
            => new(BluetoothSupportState.Error, $"Couldn't reach the D-Bus system bus: {ex.Message}"),
        _ => new(BluetoothSupportState.Error, $"Bluetooth error: {ex.Message}"),
    };

    private static List<string> ReadAdapterPaths(Message message)
    {
        var adapters = new List<string>();
        var reader = message.GetBodyReader();
        var objects = reader.ReadDictionaryStart();
        while (reader.HasNext(objects))
        {
            var path = reader.ReadObjectPathAsString();
            var interfaces = reader.ReadDictionaryStart();
            while (reader.HasNext(interfaces))
            {
                var iface = reader.ReadString();
                reader.ReadDictionaryOfStringToVariantValue();
                if (iface == Adapter1)
                    adapters.Add(path);
            }
        }
        return adapters;
    }

    private static bool SameValue(VariantValue a, VariantValue b)
    {
        if (a.Type != b.Type)
            return false;
        return a.Type switch
        {
            VariantValueType.Bool => a.GetBool() == b.GetBool(),
            VariantValueType.UInt32 => a.GetUInt32() == b.GetUInt32(),
            VariantValueType.String => a.GetString() == b.GetString(),
            _ => false,
        };
    }
}
