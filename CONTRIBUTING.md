# Contributing to Tillroll

Thanks for helping. Bug reports, missing ESC/POS commands, Bluetooth findings from real phones, documentation
fixes and pull requests are all welcome.

By taking part you agree to follow the [code of conduct](CODE_OF_CONDUCT.md).

## Reporting a bug

[Open an issue](https://github.com/hasana3n/tillroll/issues/new/choose) with the bug template. The most useful
reports include:

- what you printed and what you expected to see, ideally with a screenshot of each;
- the ESC/POS bytes: start Tillroll with `ESCPOS_DEBUG_DUMP=1` and attach the `last_escpos_receive.txt` (and
  `last_ticket.bin` for large jobs) it writes to its working folder;
- your operating system, how you connected (TCP, serial, Bluetooth Android/iPhone, browser), and the Tillroll
  version (shown in the release you downloaded);
- for crashes, the log at `%LOCALAPPDATA%\Tillroll\logs\app.log` on Windows or `~/.local/share/Tillroll/logs/app.log`
  on Linux.

Security problems are different: please report them privately, as described in [SECURITY.md](SECURITY.md).

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). The browser version also needs the
`wasm-tools` workload (`dotnet workload install wasm-tools`).

```sh
git clone https://github.com/hasana3n/tillroll.git
cd tillroll
dotnet build Tillroll.slnx
dotnet test Tillroll.slnx
dotnet run --project src/Tillroll.App.Desktop
```

Warnings fail the build, so fix them rather than suppress them. [Building and testing](docs/Building-and-Testing.md)
covers the other heads, the opt-in Bluetooth hardware tests and the portable builds.

## Making a change

1. For anything bigger than a small fix, open an issue first so we can agree on the approach.
2. Fork the repository and branch from `main`.
3. Keep the change focused: one fix or feature per pull request.
4. Add or update tests. The core is covered by `tests/Tillroll.Core.Tests` (ESC/POS in, observable results out);
   the controls by headless Avalonia tests in `tests/Tillroll.Controls.Tests`.
5. Update the docs in `docs/` when behaviour changes; the wiki is generated from them.
6. Run `dotnet test Tillroll.slnx` and make sure it passes.
7. Open the pull request and fill in the template.

### Adding an ESC/POS command

Commands follow the `BaseCommand` pattern: one class per opcode, registered in `EscPosInterpreter`.
[Adding a command](docs/Supported-Commands.md#adding-a-command) walks through it, and
[ESC/POS notes](docs/ESC-POS-Notes.md) collects the quirks found so far. Add a test that feeds the bytes and checks
what the printer renders or replies.

### Changing the interface

The interface follows the design system in [DESIGN.md](DESIGN.md): its colours, type, components and rules (for
example, amber marks only selection and new arrivals, and receipts are never smoothed). Keep new screens inside it,
and include a screenshot of any visible change in the pull request.

## Style

- Follow the existing code: explicit `using` directives, file-scoped namespaces, nullable reference types on.
- Name things for what they do; comments explain why, not what.
- Write user-facing text plainly: say what happened and what to do next.

## Licence

By contributing, you agree that your contributions are licensed under the project's [MIT License](LICENSE).
