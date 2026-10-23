# Connecting

How print jobs reach the emulator: TCP and serial on the desktop, and how to test serial without hardware.
For Bluetooth (phones printing over SPP or BLE), see [Bluetooth](Bluetooth.md). For the browser version's
transports (Web Serial, WebUSB and the TCP proxy), see [Browser app](Browser-App.md).

## TCP and serial

The emulator accepts ESC/POS data over two transports. Both can be changed **live from the UI**
(left panel): pick a TCP listen address and port and Start/Stop the listener, or select a serial
port + baud and Open/Close it (⟳ refreshes the port list). The environment variables below set the
**initial** values at startup:

| Variable | Default | Meaning |
|----------|---------|---------|
| `ESCPOS_LISTEN_ADDRESS` | `0.0.0.0` | Initial TCP bind address (`0.0.0.0` = all interfaces, `127.0.0.1` = localhost). |
| `ESCPOS_TCP_PORT` | `9100` | Initial TCP listen port. Set to `off` / `0` to start with TCP stopped. |
| `ESCPOS_SERIAL_PORT` | *(unset)* | Serial device to auto-open (e.g. `/dev/ttys004`, `COM3`). Unset = serial closed. |
| `ESCPOS_SERIAL_BAUD` | `9600` | Serial baud rate. |
| `ESCPOS_DEBUG_DUMP` | *(off)* | Set to `1` to dump every received payload to `last_*` files. |
| `ESCPOS_BT_SPP` / `ESCPOS_BT_BLE` | *(off)* | Set to `1` to start the Bluetooth SPP / BLE printer at launch. See [Bluetooth](Bluetooth.md). |
| `ESCPOS_BT_NAME` | `Tillroll` | Bluetooth printer name. |
| `ESCPOS_PAPER_WIDTH` | `80` | Paper width: `58` emulates a 58 mm printer (384-dot head, typical of Bluetooth/handheld printers). |
| `ESCPOS_BT_AUTO_PAIR` | *(off)* | Linux: set to `1` to accept Bluetooth pairing automatically (headless machines). |
| `ESCPOS_RENDER_BACKEND` | `skia` | Render backend: `skia` (default) or `imagesharp` (managed). The `--backend` arg overrides it. |

Examples:

```sh
# (run is shorthand for: dotnet run --project src/Tillroll.App.Desktop)
dotnet run --project src/Tillroll.App.Desktop                  # TCP only, port 9100
ESCPOS_TCP_PORT=9200 dotnet run --project src/Tillroll.App.Desktop   # TCP on 9200
ESCPOS_SERIAL_PORT=/dev/ttys004 dotnet run --project src/Tillroll.App.Desktop  # TCP 9100 + serial
ESCPOS_TCP_PORT=off ESCPOS_SERIAL_PORT=COM3 dotnet run --project src/Tillroll.App.Desktop  # serial only
```

The status panel shows the active TCP endpoint and serial port.

## Testing serial without hardware (app-to-app on one machine)

You don't need a USB serial adapter. Create a **virtual serial bridge** — a pair of linked ports —
then point the emulator at one end and your POS application (or a shell) at the other. Bytes written
to one end appear on the other.

**macOS / Linux** — using [`socat`](http://www.dest-unreach.org/socat/) (`brew install socat` /
`apt install socat`). A helper script is included:

```sh
./scripts/serial-bridge.sh
# It prints a linked pair, e.g.:
#   PORT A (emulator): /dev/ttys004
#   PORT B (your app): /dev/ttys005
# Leave it running.
```

Then, in two more terminals:

```sh
# Terminal 2 — run the emulator on port A
ESCPOS_SERIAL_PORT=/dev/ttys004 dotnet run --project src/Tillroll.App.Desktop

# Terminal 3 — send a receipt from "another app" on port B
cat test_receipt.txt > /dev/ttys005
#   …or from your own program, just open /dev/ttys005 like a normal serial port
#   (9600 8N1) and write ESC/POS bytes to it.
```

The receipt appears in the emulator window. Because the interpreter is stateful across reads,
fragmented serial writes (commands split across packets) are handled correctly.

**Windows** — install [com0com](https://com0com.sourceforge.net/) and create a linked pair
(e.g. `COM3` ↔ `COM4`). Run the emulator with `ESCPOS_SERIAL_PORT=COM3` and have your application
write to `COM4`.

## Printing from any app (add the emulator as an OS printer)

You can register the emulator's TCP port as a printer, so any program's **Print** dialog can send to it. Use a
raw / text-only driver: the emulator understands ESC/POS and plain text, not the page data that document
drivers (PDF, XPS, PCL) produce.

**Windows** (PowerShell as administrator):

```powershell
Add-PrinterPort -Name "Tillroll_9100" -PrinterHostAddress "127.0.0.1" -PortNumber 9100
Add-Printer -Name "Tillroll" -DriverName "Generic / Text Only" -PortName "Tillroll_9100"
# remove later: Remove-Printer -Name "Tillroll"; Remove-PrinterPort -Name "Tillroll_9100"
```

**Linux / macOS** (CUPS):

```sh
sudo lpadmin -p Tillroll -E -v socket://127.0.0.1:9100 -m raw
lp -d Tillroll -o raw receipt.bin          # send ESC/POS bytes straight through
# remove later: sudo lpadmin -x Tillroll
```

Newer CUPS versions warn that raw queues are deprecated; they still work.
