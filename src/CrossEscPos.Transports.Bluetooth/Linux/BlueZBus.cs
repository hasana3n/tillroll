using System;
using System.Collections.Generic;
using System.Linq;
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

    private readonly List<(string Path, string Name, VariantValue Original)> _changed = new();

    private BlueZBus(DBusConnection connection) => Connection = connection;

    public DBusConnection Connection { get; }

    /// <summary>Writes a message (header and body). MessageWriter is a ref struct with its own state, so it's passed by ref.</summary>
    public delegate void BodyWriter(ref MessageWriter writer);

    public static async Task<BlueZBus> ConnectAsync(string? address)
    {
        var connection = new DBusConnection(address ?? DBusAddress.System ?? "unix:path=/var/run/dbus/system_bus_socket");
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        return new BlueZBus(connection);
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

    /// <summary>Powers the adapter and makes it discoverable and pairable with no timeout, remembering what it changed.</summary>
    public async Task PrepareAdapterAsync(string adapterPath)
    {
        foreach (var (name, desired) in PrinterAdapterSettings)
        {
            var current = await GetAsync(adapterPath, Adapter1, name).ConfigureAwait(false);
            if (SameValue(current, desired))
                continue;
            await SetAsync(adapterPath, Adapter1, name, desired).ConfigureAwait(false);
            lock (_changed) _changed.Add((adapterPath, name, current));
            Logger.Info($"BlueZ adapter {adapterPath}: {name} set for the printer");
        }
    }

    /// <summary>Puts back every adapter setting <see cref="PrepareAdapterAsync"/> changed. Never throws.</summary>
    public async Task RestoreAdapterAsync()
    {
        (string Path, string Name, VariantValue Original)[] changed;
        lock (_changed)
        {
            changed = _changed.ToArray();
            _changed.Clear();
        }

        for (int i = changed.Length - 1; i >= 0; i--)
        {
            var (path, name, original) = changed[i];
            try
            {
                await SetAsync(path, Adapter1, name, original).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"Couldn't restore adapter setting {name}");
            }
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
