# Product

<!-- impeccable:product-schema 1 -->

## Platform

desktop

One Avalonia 12 application with a single, own design language (not per-OS native styling) that runs on
Windows, Linux and macOS, and the same UI as WebAssembly in the browser. Platform-only capabilities
(Bluetooth, serial ports, TCP listening) are offered only where the head supports them.

## Users

Both, with equal weight (confirmed):

- **App developers** building point-of-sale or mobile apps that print receipts (for example the owner's
  own app, Wasl). The emulator sits next to their editor while they code: they connect their app or phone,
  print, inspect the receipt, and check the status replies their code handles.
- **QA / testers** verifying receipts and error handling (paper out, cover open, drawer) without knowing
  ESC/POS. Technical detail must be available but not required for the main flows.

## Product Purpose

Tillroll stands in for a physical ESC/POS receipt printer. It receives jobs the way a real printer does
(TCP port 9100, serial, USB, Bluetooth SPP for Android, BLE for iPhone, and in-browser transports), renders
each receipt on screen, answers status queries, and simulates printer states so apps can be tested without
paper, hardware or walking to a printer. Success: a developer connects in seconds, sees exactly what their
app printed, and can reproduce printer problems on demand.

## Positioning

A cross-platform, open-source (MIT) printer emulator that talks back: real transports including Bluetooth
for phones, real status replies, simulated printer state, and a headless core with NuGet packages. Built on
danielmeza/CrossEscPosEmulator by Daniel Meza, itself a port of roydejong/EscPosEmulator by Roy de Jong.

## Operating Context

- Runs beside an IDE on a developer's desktop, often on a laptop screen at 125–150 % display scaling.
- Typical loop: start a connection (most often Bluetooth for phones, or TCP), trigger a print from the app,
  look at the receipt, adjust the app, print again. Long sessions; many receipts accumulate.
- Receipts are 58 mm (384 dots) or 80 mm (576 dots) 1-bit prints; many apps send Arabic or other scripts
  as raster images.
- A Monitor test client sends sample jobs to the emulator itself.

## Capabilities and Constraints

- Connections: TCP/IP listener (address, port), Serial (port, baud), Bluetooth (Android, SPP) and
  Bluetooth LE (iPhone, BLE) on Windows and Linux; browser head: Web Serial, WebUSB, SignalR TCP proxy.
  Each connection has a start/stop toggle, a few fields, and a plain-language status line with a connected
  count.
- Receipts: one image per cut, shown pixel-exact; export one (PNG) or all (single image or one file per cut).
- Printer state: online, cover open/closed, paper level (adequate / near end / out), error state, cash drawer,
  feed button; drives status replies and blocks printing like real hardware. Toast notifications for buzzer,
  cash drawer, and blocked prints.
- Input: paste or type ESC/POS text and render it; load a sample (barcodes + QR); clear.
- Monitor: separate test-client window on desktop, in-page panel in the browser.
- Environment variables set initial state (ESCPOS_TCP_PORT, ESCPOS_BT_*, ESCPOS_PAPER_WIDTH, …).
- Technical: Avalonia 12 + FluentTheme, CommunityToolkit.Mvvm, compiled bindings; warnings are errors;
  headless UI tests exist (tests/Tillroll.Controls.Tests). The UI lives in src/Tillroll.App (views,
  view models) and src/Tillroll.Controls (ReceiptView, PrinterStatePanel, PixelPerfectImage).
- Undecided: paper width selection in the UI (today only via ESCPOS_PAPER_WIDTH); a raw byte/hex inspector
  (not requested as a primary job).

## Brand Commitments

- Name: Tillroll. Logo: the "printing T" (an amber print head over a receipt with a torn edge, on a concourse-black
  tile). Masters and sizes in branding/ (see branding/README.md); the app icon lives in
  src/Tillroll.App.Desktop/Assets/Icon.
- Receipt text is rendered in JetBrains Mono (embedded, OFL) so output is identical everywhere.
- Copy voice in docs and status lines: plain, direct, task-first; no jargon in user-facing status.

## Evidence on Hand

- Screenshots: docs/images/ (main window, printer state, Monitor, a rendered sample receipt).
- No testimonials, user counts or benchmarks exist; do not invent any.

## Product Principles

1. Connecting a phone and seeing its receipt are the two fastest paths in the app (confirmed priorities).
2. Show the truth of the print: receipts are pixel-exact, statuses are honest, nothing claims to run when it
   isn't.
3. Simple first, depth on demand: QA can use it without ESC/POS knowledge; developers can reach every
   detail.
4. One app everywhere: the same UI on Windows, Linux, macOS and the browser; unsupported capabilities are
   hidden, not broken.
