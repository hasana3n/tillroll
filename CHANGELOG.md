# Changelog

All notable changes to Tillroll are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-23

The first release under the Tillroll name. Tillroll builds on
[CrossEscPosEmulator](https://github.com/danielmeza/CrossEscPosEmulator) 1.3.2 by Daniel Meza; see
[History](docs/History.md).

### Added

- **A Bluetooth printer for phones** on Windows and Linux. Classic Bluetooth (SPP) for Android apps and Bluetooth LE
  for iPhone apps (services `18F0`, `E7810A71-…` and `49535343-…`), with status replies. Both can run at the same
  time. See [Bluetooth](docs/Bluetooth.md).
- **An arrivals board** listing every cut with its arrival time and length; a **NEW** lamp stays lit until the
  receipt has been on screen while you work the feed.
- **A status strip** that says whether the printer would print right now and names the first reason if not, plus
  the running connections, the paper and the pixel scale.
- **Portable builds:** one executable for Windows and for Linux, with the runtime inside.
- A 58 mm paper preset (`ESCPOS_PAPER_WIDTH=58`).

### Changed

- **New name and brand.** CrossEscPos is now Tillroll, with a new logo and app icon. Projects, namespaces and
  package IDs are `Tillroll.*`, and the desktop executable is `Tillroll` / `Tillroll.exe`.
- **Redesigned interface** for Windows, Linux, macOS and the browser: an icon rail with Connections, Printer and
  Test print panels, receipts shown pixel-exact on a dark ground, embedded Public Sans and JetBrains Mono so text
  looks the same everywhere, and a Monitor that lists its test jobs with what each sends.
- Phones are listed first among the connections, and Bluetooth rows are named for the phone they serve.
- Announcements (buzzer, cash drawer, dropped print) appear in the status strip instead of over the receipt.
- Logs are written to a `Tillroll` folder (`%LOCALAPPDATA%\Tillroll\logs` on Windows).

### Fixed

- The app no longer crashes or freezes when a phone sends a large job over Bluetooth while the window refreshes.
- BLE writes are handled in order, so receipts from iPhones are no longer garbled.
- Receipts are drawn with whole screen pixels per dot at any display scaling, instead of being smoothed.
- An unknown ESC/POS command no longer wedges the parser for the rest of the job.
- Disconnected TCP clients are cleaned up, and the serial reader no longer spins the CPU.
- Several senders printing at once no longer interleave their jobs: each sender owns the printer for its job.

[1.0.0]: https://github.com/hasana3n/tillroll/releases/tag/v1.0.0
