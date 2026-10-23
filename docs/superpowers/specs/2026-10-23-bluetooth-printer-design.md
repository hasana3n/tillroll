# Bluetooth printer emulation: design

Date: 2026-10-23 · Branch: `feat/bluetooth`

## Intent

CrossEscPos already pretends to be a receipt printer over TCP, serial and USB. This work makes a
Windows or Linux computer running CrossEscPos **also appear as a Bluetooth receipt printer**, so a phone
app can pair with it, send ESC/POS, and see the receipt print on screen, with status replies (paper out,
cover open, drawer) flowing back exactly as they do over TCP today.

The person asking is building **their own phone app** (Android first, iOS if possible) that prints
receipts over Bluetooth. They want to develop and test it against CrossEscPos instead of a real
printer, and then **publish this fork publicly**, so other people's apps should be able to use it too.

They also asked to **fix the existing problems** in the project as part of this work.

## Goals

1. **Android: Classic Bluetooth, Serial Port Profile (SPP).** The computer publishes an SPP service
   (UUID `00001101-0000-1000-8000-00805F9B34FB`). An Android app pairs with the computer and connects
   with `createRfcommSocketToServiceRecord(SPP_UUID)`, then writes ESC/POS and reads replies.
2. **iOS (and Android BLE apps): Bluetooth Low Energy GATT peripheral.** iOS apps can't use SPP without
   Apple MFi, so iPhone apps print to cheap printers over BLE. The computer acts as a BLE peripheral
   exposing the GATT services those printers use (see "BLE profile" below). Writes carry ESC/POS in;
   notifications carry replies out.
3. **Both on Windows and on Linux.** Windows 10 2004 (build 19041) or later; Linux with BlueZ 5.50+.
4. **Status replies work over Bluetooth** like TCP: `DLE EOT`, `GS r`, `GS I`, and Automatic Status
   Back reach the phone that is connected.
5. **Fits the existing app.** Bluetooth appears in the existing connections panel next to TCP and
   Serial, with the same start/stop pattern and a status line that explains failures in plain words
   (adapter off, no BLE peripheral support, permission denied, unsupported OS).
6. **Usable as a library**, like the other transports: a new packable `CrossEscPos.Transports.Bluetooth`
   package that any .NET host can use with a `ReceiptPrinter`.
7. **Documentation for app authors:** a `docs/Bluetooth.md` guide covering setup and pairing per OS,
   the exact UUIDs, and minimal client snippets (Kotlin for Android SPP and BLE, Swift CoreBluetooth
   for iOS) that print a receipt and read a status byte.
8. **Existing problems fixed** (see the section below), each with a regression test.

## Non-goals

- macOS as a Bluetooth printer (CoreBluetooth peripheral could do BLE later). On macOS the Bluetooth
  entries show "not supported on macOS yet" and nothing else changes.
- The browser (WASM) head. Browsers can't host Bluetooth services.
- Bluetooth in the Monitor test client. The Monitor can't connect to its own machine's radio anyway.
- A headless Bluetooth runner (e.g. Raspberry Pi service). Nice later; the library makes it easy.
- Emulating any specific printer brand's proprietary BLE commands beyond carrying raw ESC/POS.
- MFi / Apple External Accessory support.

## Anti-goals (failure even if it "works")

- Breaking the existing transports, the browser build, the macOS build, or CI.
- Starting Bluetooth advertising, making the computer discoverable, or auto-accepting pairings without
  the user turning Bluetooth on (in the UI or with an environment variable).
- Taking over the Linux desktop's pairing agent (GNOME/KDE prompts) without the user asking for it.
- A status line that says "running" when the phone can't actually see or connect to the service.
- Claiming radio-level end-to-end success that wasn't observed with a real phone.

## Facts and constraints

- **This development PC:** Windows 11 (build 26300), MediaTek adapter. Classic, LE, and the BLE
  **peripheral role are all supported** (checked through WinRT `BluetoothAdapter`). So both radios can
  run here.
- **No Linux machine with Bluetooth is available to the builder.** Linux code is verified by build,
  unit tests, and tests of the D-Bus layer against a fake/mock BlueZ; radio verification on Linux
  needs the user's hardware.
- **Radio end-to-end needs a phone.** A computer can't connect to its own Bluetooth services. The
  builder runs everything up to the radio, then gives the user a short, exact test script.
- **Windows Bluetooth server APIs are WinRT only** for BLE (`GattServiceProvider`); RFCOMM can use
  WinRT (`RfcommServiceProvider`) or Winsock. WinRT requires a Windows-specific target framework
  (`net10.0-windows10.0.19041.0`).
- **Linux uses BlueZ over the D-Bus system bus.** SPP: `org.bluez.ProfileManager1.RegisterProfile` +
  an exported `org.bluez.Profile1` (BlueZ hands over a socket file descriptor per connection).
  BLE: exported GATT application objects + `GattManager1.RegisterApplication`, and an
  `LEAdvertisement1` via `LEAdvertisingManager1.RegisterAdvertisement`.
- The repo builds with warnings as errors, Nullable on, implicit usings off, .NET SDK 10.0.300.
- Release builds the `win-x64` desktop app **on ubuntu-latest**, so the Windows target framework must
  build on Linux (`EnableWindowsTargeting=true`).
- The ESC/POS interpreter already handles a byte stream arriving in arbitrary chunks, which is what BLE
  writes look like.

## Decisions (builder's calls, made during design; the user hasn't reviewed these individually)

### Two transports, both on by user action

- Two entries in the connections panel: **"Bluetooth (Android / SPP)"** and **"Bluetooth LE
  (iPhone / BLE)"**, each with its own Start/Stop and status line, so either can run alone.
- Fields: printer **name** (default `CrossEscPos`) on both; RFCOMM channel is automatic.
- Off by default. Environment variables auto-start them, matching the existing pattern:
  `ESCPOS_BT_SPP=1`, `ESCPOS_BT_BLE=1`, `ESCPOS_BT_NAME=<name>`.
- The status line shows: what's advertised, under which name/address, how many phones are connected,
  and the next step (e.g. "Pair this PC from the phone's Bluetooth settings").

### BLE profile

Host three services so apps written for the common cheap printers work unchanged. All feed the same
printer. Only the first is put in the advertisement (legacy advertising fits one 16-bit UUID plus a name);
the others are found after connecting.

| Service | Write (data in) | Notify (replies out) | Why |
|---|---|---|---|
| `000018F0-0000-1000-8000-00805F9B34FB` (0x18F0) | `0x2AF1` write + write-without-response | `0x2AF0` notify | The most common generic thermal-printer BLE profile |
| `E7810A71-73AE-499D-8C15-FAA9AEF0C3F2` | `BEF8D6C9-9C21-4C9E-B632-BD58C1009F9F` write + write-without-response (also notify, read) | same characteristic | Used by many iOS printing SDKs |
| `49535343-FE7D-4AE5-8FA9-9FAFD205E455` (Microchip/ISSC transparent UART) | `49535343-8841-43F4-A8D4-ECBE34729BB3` | `49535343-1E4D-4BD9-BA61-23C647249616` | Printers built on Microchip BLE modules |

Behaviour:
- Writes are fed to the printer **in arrival order**, per connection, by a single consumer.
- Replies go out as notifications on the notify characteristic of the **service the job was written to**,
  split into chunks of (ATT MTU − 3) bytes, minimum 20.
- A client that has subscribed to a notify characteristic is registered as a long-lived responder
  (so it receives Automatic Status Back) until it unsubscribes or disconnects.
- Windows can't set the BLE advertised local name; it uses the computer name. The status line says so.

### SPP behaviour

- SDP record: SPP UUID, service name = the printer name. Accept multiple simultaneous connections;
  each is a responder for its own replies, like a TCP client.
- Windows: advertise with the radio discoverable while the transport is on. Pairing is initiated from
  the phone; Windows shows its own "Add device" prompt that the user accepts.
- Linux: while the transport is on, ensure the adapter is powered, discoverable and pairable, and put
  back any setting the app changed when it stops. Pairing must work on a machine **without** a desktop
  pairing agent: provide an opt-in "Just Works" agent (`ESCPOS_BT_AUTO_PAIR=1` or a checkbox), never
  registered as the default agent unless the user opted in.

### Package and target frameworks

- New project `src/CrossEscPos.Transports.Bluetooth`, packable, multi-targeting:
  - `net10.0`: the Linux (BlueZ/D-Bus) implementation, the shared BLE/SPP logic, and an "unsupported"
    result on other OSes.
  - `net10.0-windows10.0.19041.0`: adds the Windows (WinRT) implementation.
- One entry point picks the implementation at runtime and can report support up front (adapter
  present, classic supported, BLE peripheral supported) so the UI can explain why something is off.
- D-Bus library: `Tmds.DBus.Protocol` (it's what Avalonia uses on Linux, so no version conflict).
- `CrossEscPos.App.Desktop` targets `net10.0-windows10.0.19041.0` when building for Windows (RID
  `win-*`, or no RID on a Windows host) and `net10.0` otherwise, so `dotnet run` works on every OS
  without `-f`, and the published `win-x64` app includes Bluetooth. CI and release keep working,
  including building `win-x64` on Ubuntu.

### Concurrency

- `ReceiptPrinter.FeedEscPos` becomes safe to call from several transports at once (TCP, serial, SPP,
  BLE): one feed runs at a time, and the "current responder" can't be overwritten by another thread.
- **A connection owns the printer for the length of its job** (builder's call). A lock per call isn't
  enough: the parser keeps state between calls, and a BLE job arrives as many small writes, so a
  second sender's bytes could land inside a half-finished command. The printer is owned by the
  connection that last fed it until that connection disconnects or goes idle (default ~1 s, tunable).
  Others wait in order. Owner identity is the responder/connection object; feeds without one (direct
  API calls) keep working as today. Real multi-interface printers behave the same way (busy while
  receiving). One-parser-per-connection was rejected as a large Core refactor with public API churn.
- Real-time commands (`DLE EOT`, `DLE ENQ`, `DLE DC4`) from a waiting connection should still be
  answered, if that can be done without corrupting the owner's parse state; otherwise documented.

## Existing problems to fix

Known from reading the code. Confirm each with a failing test before fixing:

1. **Interpreter gets stuck after an unknown command.** After an unrecognised `ESC`/`FS`/`GS` prefix,
   `_interpretingCommandPrefix` and `_commandBuffer` are never reset, so every later byte, in every
   later job, throws again and nothing prints until restart. Fix: skip the bad prefix, reset state, and
   keep parsing the rest of the same payload.
2. **`FeedEscPos` isn't thread-safe** (see Concurrency).
3. **TCP server:** closed clients are never removed from `_clients`; `Stop()` leaves connected clients
   running; `NetClient.Close()` can throw when the socket is already gone.
4. **Serial reader** busy-loops if a read returns 0 bytes.

Plus anything that `dotnet build` / `dotnet test` and a code review of the touched areas turn up. Each
fix gets a regression test. Anything found but deliberately not fixed is listed in the final report.

## How "done" is checked

1. `dotnet build CrossEscPos.slnx` with zero warnings and `dotnet test CrossEscPos.slnx` all green on
   this Windows PC; the CI workflow's steps also pass for the Linux job (build including the Windows
   target framework, tests, pack).
2. Unit tests cover: BLE chunking and notification routing, ordered feeding of writes, responder
   registration on subscribe/unsubscribe, SPP connection plumbing through a fake stream, the support
   probe's messages, the Linux D-Bus objects (introspected/invoked against a fake BlueZ), and every
   existing-problem fix.
3. On this PC the app starts both Bluetooth transports and they register with the OS (SDP record
   present, GATT services published, advertising started), observed and reported, not assumed.
4. A **phone test script** in `docs/Bluetooth.md` the user runs: Android with a generic SPP terminal app
   and nRF Connect; iPhone with nRF Connect or LightBlue. Each step says what to send (hex) and what
   should appear (receipt on screen, status byte back).
5. The final report separates **verified here** from **needs your phone / your Linux box**.

## Answered by the user

- **Discovery:** their app finds the computer with its own device picker, or connects by address plus
  the SPP UUID / BLE service UUID. The computer does **not** need to look like a printer by Bluetooth
  device class, so no system-wide Bluetooth settings change. `docs/Bluetooth.md` notes that printer
  libraries that filter paired devices by class (e.g. DantSu's ESCPOS-ThermalPrinter-Android) won't list
  the PC, and how to connect by address instead.
- **Publishing:** leave package names, authors, URLs and NuGet publishing as they are; the user rebrands
  when they publish. Only add the new project to the solution, CI, and the release `pack` list so it builds
  and packs like the others.

## Left to the builder

- Exact public API shape of the Bluetooth package, class layout, and test doubles.
- WinRT vs Winsock for Windows RFCOMM.
- How the Linux D-Bus layer is faked in tests.
- Logging detail and docs wording.
- Whether to also add the Bluetooth entries to the Monitor's port list later (out of scope now).
