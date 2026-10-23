# History

Tillroll is the third chapter of a project that started as a small Windows tool.

## 1. EscPosEmulator, by Roy de Jong

[Roy de Jong](https://github.com/roydejong) wrote [EscPosEmulator](https://github.com/roydejong/EscPosEmulator)
in 2022: a WPF app for Windows that listened on TCP, interpreted ESC/POS and drew each receipt on screen. Its
interpreter, one command class per opcode registered in `EscPosInterpreter`, had no dependency on the UI or on
Windows, and it is still the heart of Tillroll today. Thank you, Roy.

## 2. CrossEscPosEmulator, by Daniel Meza

[Daniel Meza](https://github.com/danielmeza) ported it to Avalonia for the
[Avalonia Port Challenge](https://avaloniaui.net/blog/avalonia-port-challenge) in 2026 and released it as
[CrossEscPosEmulator](https://github.com/danielmeza/CrossEscPosEmulator) under the MIT licence. Along the way he
turned it into a set of layered packages: a headless core, swappable render backends, transports for TCP, serial
and USB, reusable controls, the Monitor test client, a browser version with a SignalR relay, and the documentation
that this site grew from. Thank you, Daniel. His write-up of what the port took follows, lightly edited.

### From WPF to Avalonia: what the migration cost

#### Starting point

The upstream app (last updated July 2025) targeted `net9.0-windows7.0` with `UseWPF`: 48 C#/XAML files, about 2,400
lines. Windows was wired in at four levels:

- **Rendering.** Every receipt line drew itself with GDI+ (`System.Drawing.Bitmap` and `Graphics`), which is
  Windows-only on .NET 6 and later. The GDI+ types were part of the core interface,
  `IReceiptPrintable.Render(Bitmap, Graphics, int, int)`. To show a receipt, the window saved each bitmap into a BMP
  `MemoryStream` and loaded it back as a WPF `BitmapImage`.
- **UI.** Code-behind only. `MainWindow.xaml.cs` created `Image` controls by hand, found them again by a
  GUID-derived `Name`, and added or removed them from a `StackPanel`.
- **OS calls.** A `user32!FlashWindow` P/Invoke and `System.Media.SystemSounds` signalled new jobs.
- **Assumptions.** TCP was the only transport, and the test receipt was read from the current working directory.

The ESC/POS interpreter had no UI or Windows dependency, so its design carried over unchanged. It is now the
headless `Tillroll.Core` library.

#### What changed, and what each part cost

| Area | WPF original | Avalonia port | What it took |
|---|---|---|---|
| Rendering | GDI+ `Bitmap` / `Graphics` | SkiaSharp, later behind a backend-neutral `IReceiptCanvas`, plus a fully managed ImageSharp backend | The biggest single job. The text-line renderer (styles, sizes, justification, underline) was rewritten. System fonts differ per OS, so the same receipt measured differently on each; embedding JetBrains Mono (OFL) made output identical everywhere. The ImageSharp backend later needed its advance widths matched to Skia's. |
| UI | XAML + code-behind | AXAML + MVVM (CommunityToolkit.Mvvm); receipts bound to a reusable `ReceiptView` control | The markup ported almost line for line. The work was moving the code-behind into view models and bindings. |
| Win32 calls | `FlashWindow`, `SystemSounds` | `INotificationService`: `afplay` on macOS, `Console.Beep` on Windows, `paplay`/`aplay` on Linux, plus an in-window notice | Avalonia has no cross-platform system-sound API, so each OS gets its own strategy. |
| Files | Relative to the working directory | Avalonia `StorageProvider` for PNG export; app-relative asset paths | The working-directory assumption broke in the packaged app and was fixed right after the first release. |
| Transports | TCP | TCP and serial (`System.IO.Ports`); the Monitor adds direct USB (libusb) | Port names differ per OS (`COM3`, `/dev/ttyUSB0`, `/dev/cu.*`), and a Homebrew-installed libusb wasn't found until the app added the usual install paths at startup. |
| Packaging | One Windows `.exe` | Self-contained builds for win-x64, linux-x64, osx-x64 and osx-arm64; a script builds the macOS `.app` | Unsigned macOS bundles were reported as "damaged", so the bundle is ad-hoc signed. |
| Browser | n/a | The same app as an Avalonia WebAssembly head | Platform edges sit behind `IPlatformServices`. A browser can't listen on TCP, so an ASP.NET Core SignalR host opens the socket and relays jobs to the page. Serial and USB go through Web Serial and WebUSB. |

#### What was easy, and what hurt

- **Easy:** XAML to AXAML, the Fluent theme, `Dispatcher.UIThread`, and headless UI testing. The UI layer was the
  smallest part of the port.
- **Hurt:** removing GDI+. It was part of the core interfaces, not just the view, so the renderer had to be
  redesigned before anything else could move. After that came per-OS native dependencies (libusb, fontconfig on
  Linux, macOS signing) and the browser sandbox (no sockets, no raw file system).
- **Would do again:** put the drawing surface behind an interface first. Once `IReceiptCanvas` existed, the
  ImageSharp backend (contributed by [@yhonc9](https://github.com/yhonc9)) and the browser head each landed within a
  few days.

Screenshots of the original WPF app and the first Avalonia port are in the
[CrossEscPosEmulator README](https://github.com/danielmeza/CrossEscPosEmulator#from-wpf-to-avalonia-what-the-migration-cost).

## 3. Tillroll

Tillroll continues from CrossEscPosEmulator 1.3.2. It adds a Bluetooth printer for phones (Classic SPP for Android
and BLE for iPhone, on Windows and Linux), fixes for printing from phones under load, a redesigned interface with an
arrivals board and a printer status strip, receipts drawn pixel-exact at any display scaling, portable single-file
builds, and a new name and brand. The [changelog](../CHANGELOG.md) lists the details.
