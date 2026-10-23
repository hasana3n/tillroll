# Third-party notices

Tillroll is released under the [MIT License](LICENSE). It includes or depends on the following third-party work,
each under its own licence. The desktop downloads bundle the .NET runtime and the libraries below.

## Origins

| Project | Author | Licence |
|---|---|---|
| [CrossEscPosEmulator](https://github.com/danielmeza/CrossEscPosEmulator), which Tillroll is built on | Daniel Meza / Gasoleo Technology | MIT |
| [EscPosEmulator](https://github.com/roydejong/EscPosEmulator), the original emulator and ESC/POS interpreter | Roy de Jong | No licence file is published in the original repository |

## Libraries

| Library | Used for | Licence |
|---|---|---|
| [.NET runtime and libraries](https://github.com/dotnet/runtime), including System.IO.Ports | Runtime (bundled in the self-contained builds), serial ports | MIT |
| [ASP.NET Core](https://github.com/dotnet/aspnetcore) (SignalR client, WebAssembly server) | Browser relay host | MIT |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (incl. Avalonia.Themes.Fluent, Avalonia.Fonts.Inter) | User interface | MIT |
| [SkiaSharp and HarfBuzzSharp](https://github.com/mono/SkiaSharp) | Rendering (bundled native libraries) | MIT |
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 2.x and ImageSharp.Drawing | Managed render backend | Apache-2.0 |
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 3.x | Managed render backend | [Six Labors Split License 1.0](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE) (Apache-2.0 terms for open-source use) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | View models | MIT |
| [ZXing.Net](https://github.com/micjahn/ZXing.Net) | Barcode and 2D-code rendering | Apache-2.0 |
| [QRCoder](https://github.com/codebude/QRCoder) | QR code rendering | MIT |
| [Ardalis.SmartEnum](https://github.com/ardalis/SmartEnum) | Command code maps | MIT |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | Bluetooth on Linux (BlueZ over D-Bus) | MIT |
| [ESC-POS-.NET](https://github.com/lukevp/ESC-POS-.NET) | The Monitor's test jobs | MIT |
| [LibUsbDotNet](https://github.com/LibUsbDotNet/LibUsbDotNet) | The Monitor's direct USB printing | LGPL-3.0 |

### About LibUsbDotNet (LGPL-3.0)

LibUsbDotNet is used as an unmodified library. In the portable Windows and Linux builds it ships as a separate
file, `LibUsbDotNet.dll`, beside the Tillroll executable rather than inside it, so you can replace it with your own
build of the library. Its source code and licence are available at
[github.com/LibUsbDotNet/LibUsbDotNet](https://github.com/LibUsbDotNet/LibUsbDotNet), and the
[LGPL-3.0](https://www.gnu.org/licenses/lgpl-3.0.html) text at gnu.org.

## Fonts

| Font | Used for | Licence |
|---|---|---|
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) | Receipt text and the figures in the interface | SIL Open Font License 1.1 ([text](src/Tillroll.Rendering.Skia/Assets/Fonts/OFL.txt)) |
| [Public Sans](https://github.com/uswds/public-sans) | The interface | SIL Open Font License 1.1 ([text](src/Tillroll.App/Assets/Fonts/PublicSans-OFL.txt)) |
| [Inter](https://github.com/rsms/inter), via Avalonia.Fonts.Inter | Fallback interface font | SIL Open Font License 1.1 |

"JetBrains Mono" is a trademark of JetBrains s.r.o. The fonts are redistributed unmodified under their original
licences.

## Icons

The interface icons are drawn from [Lucide](https://lucide.dev) (ISC License, copyright Lucide Contributors and
Cole Bemis).

## Trademarks

ESC/POS is a trademark of Seiko Epson Corporation. Tillroll is an independent project and is not affiliated with
or endorsed by Epson or any printer manufacturer.
