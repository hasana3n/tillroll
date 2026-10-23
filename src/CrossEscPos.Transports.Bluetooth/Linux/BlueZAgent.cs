using System;
using System.Text;
using System.Threading.Tasks;
using CrossEscPos.Logging;
using Tmds.DBus.Protocol;

namespace CrossEscPos.Transports.Bluetooth.Linux;

/// <summary>
/// An opt-in BlueZ pairing agent that accepts every pairing ("Just Works", NoInputNoOutput) so phones can
/// pair with a machine that has no desktop pairing prompt. Only used when
/// <see cref="BluetoothPrinterOptions.AutoAcceptPairing"/> is on.
/// </summary>
internal sealed class BlueZAgent : IPathMethodHandler, IAsyncDisposable
{
    internal const string ObjectPath = "/com/crossescpos/agent";
    private const string Agent1 = "org.bluez.Agent1";
    private const string AgentManager1 = "org.bluez.AgentManager1";

    private static readonly ReadOnlyMemory<byte> IntrospectionXml = Encoding.UTF8.GetBytes("""
        <interface name="org.bluez.Agent1">
          <method name="Release"/>
          <method name="RequestPinCode"><arg type="o" direction="in"/><arg type="s" direction="out"/></method>
          <method name="DisplayPinCode"><arg type="o" direction="in"/><arg type="s" direction="in"/></method>
          <method name="RequestPasskey"><arg type="o" direction="in"/><arg type="u" direction="out"/></method>
          <method name="DisplayPasskey"><arg type="o" direction="in"/><arg type="u" direction="in"/><arg type="q" direction="in"/></method>
          <method name="RequestConfirmation"><arg type="o" direction="in"/><arg type="u" direction="in"/></method>
          <method name="RequestAuthorization"><arg type="o" direction="in"/></method>
          <method name="AuthorizeService"><arg type="o" direction="in"/><arg type="s" direction="in"/></method>
          <method name="Cancel"/>
        </interface>
        """);

    private readonly BlueZBus _bus;
    private bool _registered;

    public BlueZAgent(BlueZBus bus) => _bus = bus;

    string IPathMethodHandler.Path => ObjectPath;
    bool IPathMethodHandler.HandlesChildPaths => false;

    /// <summary>Registers the agent; becomes the default agent (the one BlueZ asks for incoming pairings) when asked.</summary>
    public async Task StartAsync(bool makeDefault)
    {
        _bus.Connection.AddMethodHandler(this);
        await _bus.CallAsync("/org/bluez", AgentManager1, "RegisterAgent", "os", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(ObjectPath);
            w.WriteString("NoInputNoOutput");
        }).ConfigureAwait(false);
        _registered = true;

        if (makeDefault)
            await _bus.CallAsync("/org/bluez", AgentManager1, "RequestDefaultAgent", "o", (ref MessageWriter w) => w.WriteObjectPath(ObjectPath)).ConfigureAwait(false);

        Logger.Info("BlueZ pairing agent registered (pairings are accepted automatically)");
    }

    public async ValueTask DisposeAsync()
    {
        if (_registered)
        {
            try
            {
                await _bus.CallAsync("/org/bluez", AgentManager1, "UnregisterAgent", "o", (ref MessageWriter w) => w.WriteObjectPath(ObjectPath)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Info($"UnregisterAgent: {ex.Message}");
            }
            _registered = false;
        }
        _bus.Connection.RemoveMethodHandler(ObjectPath);
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(new[] { IntrospectionXml });
            return default;
        }

        if (context.Request.InterfaceAsString != Agent1)
        {
            context.ReplyUnknownMethodError();
            return default;
        }

        switch (context.Request.MemberAsString)
        {
            case "RequestPinCode":
            {
                using var writer = context.CreateReplyWriter("s");
                writer.WriteString("0000");
                context.Reply(writer.CreateMessage());
                break;
            }
            case "RequestPasskey":
            {
                using var writer = context.CreateReplyWriter("u");
                writer.WriteUInt32(0);
                context.Reply(writer.CreateMessage());
                break;
            }
            case "RequestConfirmation":
            case "RequestAuthorization":
                // Pairing itself ("Just Works"): accepted while the printer runs; the agent is gone when it stops.
                Logger.Info($"BlueZ agent: accepted {context.Request.MemberAsString}");
                ReplyEmpty(context);
                break;
            case "AuthorizeService":
            {
                // As the default agent we'd be asked about every service on this machine (file transfer, audio,
                // input, …). Only the printer's serial port is ours to approve.
                var reader = context.Request.GetBodyReader();
                var device = reader.ReadObjectPathAsString();
                var uuid = reader.ReadString();
                if (string.Equals(uuid, Ble.BlePrinterProfiles.SppServiceUuid.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info($"BlueZ agent: authorized the printer serial port for {device}");
                    ReplyEmpty(context);
                }
                else
                {
                    Logger.Info($"BlueZ agent: rejected service {uuid} for {device}");
                    context.ReplyError("org.bluez.Error.Rejected", "Only the CrossEscPos printer service is authorized");
                }
                break;
            }
            case "DisplayPinCode":
            case "DisplayPasskey":
            case "Cancel":
            case "Release":
                ReplyEmpty(context);
                break;
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
