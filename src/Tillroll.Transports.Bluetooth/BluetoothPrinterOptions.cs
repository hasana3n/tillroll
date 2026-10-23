using System.Text;

namespace Tillroll.Transports.Bluetooth;

/// <summary>Settings shared by the Bluetooth printer servers.</summary>
public sealed class BluetoothPrinterOptions
{
    /// <summary>Legacy BLE advertising leaves this many bytes for the name next to the flags and the 0x18F0 UUID.</summary>
    public const int MaxAdvertisedNameBytes = 20;

    private const string DefaultName = "Tillroll";

    /// <summary>
    /// The printer name phones see: the SPP service name, and the BLE advertised name on Linux (Windows always
    /// advertises the computer name over BLE).
    /// </summary>
    public string Name { get; set; } = DefaultName;

    /// <summary>
    /// Linux only: accept pairing requests automatically ("Just Works") by registering a BlueZ agent as the
    /// default agent. Needed on machines without a desktop pairing prompt. Off by default so it never takes
    /// over GNOME's or KDE's pairing dialogs.
    /// </summary>
    public bool AutoAcceptPairing { get; set; }

    /// <summary>Linux only: the D-Bus address to reach BlueZ on. Null means the system bus.</summary>
    public string? DBusAddress { get; set; }

    /// <summary>Linux only: the adapter to use, e.g. "hci1". Null means the first adapter BlueZ reports.</summary>
    public string? AdapterName { get; set; }

    /// <summary>
    /// <see cref="Name"/> cut to at most <see cref="MaxAdvertisedNameBytes"/> UTF-8 bytes on a character
    /// boundary, so BLE advertising never fails because the name is too long. Empty names become "Tillroll".
    /// </summary>
    public string AdvertisedName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
                return DefaultName;

            var result = new StringBuilder();
            int bytes = 0;
            foreach (var rune in Name.EnumerateRunes())
            {
                bytes += rune.Utf8SequenceLength;
                if (bytes > MaxAdvertisedNameBytes)
                    break;
                result.Append(rune.ToString());
            }
            return result.ToString();
        }
    }
}
