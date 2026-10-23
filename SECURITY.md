# Security policy

## Supported versions

Security fixes go into the latest release. Please update to it before reporting a problem.

| Version | Supported |
|---|---|
| 1.x (latest) | Yes |
| Older | No |

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Report them privately through GitHub instead:

1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability**.
3. Describe the problem, how to reproduce it, and what an attacker could do with it.

Only the maintainers can see the report. You'll get a reply within a week, and we'll keep you updated while we
work on a fix. Once a fix is released, we'll credit you in the advisory unless you'd rather stay anonymous.

## What's in scope

Tillroll is a development and testing tool that listens for print jobs. Things worth reporting include:

- a crafted ESC/POS job, Bluetooth write or relay message that crashes Tillroll, hangs it, or makes it read or
  write files it shouldn't;
- the browser relay host (`samples/Tillroll.Host`) accepting connections or data it shouldn't;
- anything in the release builds or the release workflow that could let someone tamper with what users download.

## Things to know when running Tillroll

- **It accepts jobs from your network.** The TCP listener binds to all interfaces (`0.0.0.0`) on port 9100 by
  default, so anyone who can reach your computer can print to it, just as with a real network printer. To accept
  only local jobs, set `ESCPOS_LISTEN_ADDRESS=127.0.0.1` or choose `127.0.0.1` under the TCP/IP row's Settings.
- **Bluetooth makes the computer visible.** While a Bluetooth row is running, nearby phones can find and connect
  to the computer as a printer. Stop it when you're done. See [Bluetooth security](docs/Bluetooth.md#security).
- **Debug dumps contain print data.** With `ESCPOS_DEBUG_DUMP=1`, Tillroll writes every job it receives to its
  working folder. Receipts can contain personal or payment data, so turn this off when you don't need it.
- **The downloads aren't code-signed yet.** Download releases only from
  [this repository's releases page](https://github.com/hasana3n/tillroll/releases).
