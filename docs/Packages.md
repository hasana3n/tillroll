# Packages

Tillroll is split into layered, independently-usable packages so you can take only what you need —
the headless emulator, a render backend, the transports, and/or the Avalonia controls.

| Package | What it gives you | Depends on |
| --- | --- | --- |
| **Tillroll.Abstractions** | Backend-agnostic contracts: `IReceiptCanvas`, `IReceiptImage`, `IReceiptImageFactory`, `ITypefaceProvider`, `IImageEncoder`, `IReceiptPrintable`, `IPrinterResponder` | — |
| **Tillroll.Core** | The headless ESC/POS emulator: `ReceiptPrinter`, the interpreter, the receipt document model, barcode/QR | Abstractions |
| **Tillroll.Rendering.Skia** | The default render backend (SkiaSharp, native); fonts embedded | Abstractions |
| **Tillroll.Rendering.ImageSharp** | A 100% managed render backend (ImageSharp) — no native dependency, runs in the browser (WASM) with no native relink; byte-compatible output with Skia | Abstractions |
| **Tillroll.Transports** | TCP / serial / USB transports that feed a printer (desktop) | Core |
| **Tillroll.Transports.Bluetooth** | Makes the computer a Bluetooth printer: Classic SPP (Android) and a BLE GATT peripheral (iPhone). Windows (WinRT, `net10.0-windows10.0.19041.0`) and Linux (BlueZ). See [Bluetooth](Bluetooth.md) | Core, Tmds.DBus.Protocol (Linux) |
| **Tillroll.Controls** | Reusable Avalonia controls: `ReceiptView`, `PrinterStatePanel` | Core, Avalonia |

Everything lives under the single `Tillroll.*` namespace root (organized by feature, e.g.
`Tillroll.Emulator`, `Tillroll.Graphics`, `Tillroll.Controls`), so types resolve across the
packages regardless of which one they ship in.

## How the packages depend on each other

```mermaid
flowchart BT
    Abstractions["Tillroll.Abstractions"]
    Core["Tillroll.Core"] --> Abstractions
    Skia["Tillroll.Rendering.Skia"] --> Abstractions
    ImageSharp["Tillroll.Rendering.ImageSharp"] --> Abstractions
    Transports["Tillroll.Transports"] --> Core
    Bluetooth["Tillroll.Transports.Bluetooth"] --> Core
    Controls["Tillroll.Controls"] --> Core
    Controls --> Abstractions

    Host(["your host app"]) -.-> Core
    Host -.->|"pick one backend"| Skia
    Host -.->|"…or"| ImageSharp
    Host -.-> Controls
    Host -.-> Transports
    Host -.-> Bluetooth
```

## Choosing a render backend

| | `Rendering.Skia` | `Rendering.ImageSharp` |
| --- | --- | --- |
| Implementation | native `libSkiaSharp` | 100% managed (SixLabors.ImageSharp) |
| Browser (WASM) | needs `wasm-tools` + native relink (~23 MB runtime) | **just works** — no native relink |
| Speed | faster (native raster) | fine for receipt rendering |
| Output | reference | byte-compatible with Skia |
| Default in | desktop app, server | browser app |

Both embed JetBrains Mono and produce the same receipts. See **[Rendering & Backends](Rendering-and-Backends.md)**.

## Install

The packages aren't on nuget.org yet. Build them with `dotnet pack -c Release`, which writes the `.nupkg` files to
each project's `bin/Release`, and add that folder as a local package source:

```sh
dotnet pack -c Release -o ./packages
dotnet nuget add source ./packages --name tillroll-local
dotnet add package Tillroll.Core
dotnet add package Tillroll.Rendering.Skia
# or the managed backend:
dotnet add package Tillroll.Rendering.ImageSharp
```

Or reference the projects directly with `dotnet add reference`. The release workflow publishes the packages to
nuget.org once the repository variable `PUBLISH_NUGET` is `true` and the `NUGET_USER` secret is set.
