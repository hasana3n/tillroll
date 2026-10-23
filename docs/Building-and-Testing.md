# Building and testing

Build and run every head from source, and run the test suites.

## Building and running

Requires the .NET 10 SDK (and the `wasm-tools` workload for the browser head:
`dotnet workload install wasm-tools`).

```sh
# Desktop app (Windows / macOS / Linux)
dotnet run --project src/Tillroll.App.Desktop

# Headless: ESC/POS bytes -> PNG, no UI, no Avalonia
dotnet run --project samples/Tillroll.Headless -- test_receipt.txt out.png

# Browser: the SAME app in the browser (Avalonia WASM head) — full parity with desktop
dotnet run --project src/Tillroll.App.Browser

# Web host: serves the browser app AND the SignalR broker on one origin, giving it TCP reception
# (browsers can't open raw sockets). First run publishes the WASM app into wwwroot (cached after).
# Browse to the printed URL; the emulator opens the TCP port itself.
dotnet run --project samples/Tillroll.Host

# Tests (unit + headless UI)
dotnet test Tillroll.slnx
```

- **Windows / macOS:** no extra setup — native rendering libraries ship with the Avalonia packages.
- **Linux:** install the usual font/render native deps if they are missing, e.g.
  `sudo apt install libfontconfig1 libfreetype6` (Debian/Ubuntu).

## Portable build (one executable)

Publish the desktop app as one self-contained file: the .NET runtime and the native rendering libraries are
inside, so it runs on a machine with nothing installed. Copy it anywhere (a USB stick, a shared folder) and run
it.

```sh
# Windows -> publish/portable-win-x64/Tillroll.exe (about 57 MB)
dotnet publish src/Tillroll.App.Desktop -r win-x64 -p:PublishProfile=Portable-win-x64

# Linux -> publish/portable-linux-x64/Tillroll (about 50 MB)
dotnet publish src/Tillroll.App.Desktop -r linux-x64 -p:PublishProfile=Portable-linux-x64
```

- Keep `-r` alongside the profile. The project picks its target framework (Windows with WinRT Bluetooth, or plain
  .NET with BlueZ) from the runtime before the profile is read, and a Linux build without it stops with an error
  rather than quietly losing Bluetooth.
- The first start unpacks the native libraries to a temp folder, so it takes a moment longer than later starts.
- The app keeps no settings files; configuration comes only from the [environment variables](Connecting.md). It
  does write a log, to `%LOCALAPPDATA%\Tillroll\logs\app.log` on Windows (`~/.local/share/...` on
  Linux).
- The build isn't trimmed: Avalonia and the transports load types by reflection, and trimming breaks them.

The profiles live in
[`src/Tillroll.App.Desktop/Properties/PublishProfiles`](../src/Tillroll.App.Desktop/Properties/PublishProfiles).
The release workflow publishes Windows and Linux the same way.

## Testing

Four test projects under [`tests/`](../tests):

- **`Tillroll.Core.Tests`** — emulation coverage. ESC/POS sequences are fed through the interpreter
  and the observable results (rendered draws, printer state, host responses, events) asserted: text
  and control characters, styling (emphasis/italic/size/justification), cutting and feeding, 1D/2D
  barcodes and bit images, status/transmit-back (`DLE EOT`, `GS r`, `GS I`, `GS a`), cash drawer and
  buzzer, real-time commands, code pages, page mode and not-ready handling — plus exact `StatusByteBuilder`
  bit layouts and the `SmartEnum` code maps. Rendering goes to a synthetic backend, so the suite runs
  with no SkiaSharp (proving the core is headless).
- **`Tillroll.Controls.Tests`** — headless Avalonia UI tests for the controls (two-way state binding,
  receipt image rendering).
- **`Tillroll.Transports.Tests`** — the TCP server (client clean-up, stop, status replies) and the shared
  stream feeder.
- **`Tillroll.Transports.Bluetooth.Tests`** — the BLE engine and chunking (always run), plus two opt-in
  groups:
  - **Fake BlueZ** (`Linux/`): the Linux servers against a fake `bluetoothd` on a private D-Bus. CI runs them
    with `ESCPOS_BT_FAKE_BLUEZ=1 dbus-run-session -- dotnet test tests/Tillroll.Transports.Bluetooth.Tests`.
    The socket-passing tests need Linux; the rest also run against any D-Bus daemon whose address is in
    `DBUS_SESSION_BUS_ADDRESS`.
  - **Hardware** (`Windows/`): start the real Windows servers on this PC's adapter (they make the PC
    discoverable). Run with
    `ESCPOS_BT_HARDWARE_TESTS=1 dotnet test tests/Tillroll.Transports.Bluetooth.Tests -f net10.0-windows10.0.19041.0`.

  Testing with a real phone is described in [Bluetooth](Bluetooth.md#test-it-with-a-phone-no-code-needed).

```sh
dotnet test Tillroll.slnx
```

## Releases, the site and the wiki

Three GitHub Actions workflows publish the project:

- [`release.yml`](../.github/workflows/release.yml) runs on a `v*` tag. It builds self-contained desktop
  apps for win-x64 and linux-x64 (each a single portable executable) and osx-x64 and osx-arm64 (ad-hoc signed
  `.app` bundles), packs
  the NuGet libraries, publishes them to nuget.org and to GitHub Packages (which lists them in the
  repository's sidebar), and attaches everything to a GitHub Release. Each package embeds the README,
  converted for nuget.org by [`build/PackageReadme.targets`](../build/PackageReadme.targets), and uses
  [`docs/logo.png`](logo.png) as its icon.
- [`pages.yml`](../.github/workflows/pages.yml) runs on every push to `main`. It publishes the project
  site from [`site/`](../site) and the browser app under `/app/` to GitHub Pages.
- [`wiki.yml`](../.github/workflows/wiki.yml) runs when `docs/` changes on `main` and publishes these
  pages to the [wiki](https://github.com/hasana3n/tillroll/wiki). Edit the pages here, not in
  the wiki: the next sync overwrites wiki edits.
