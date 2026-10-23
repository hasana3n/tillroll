# Bluetooth Printer Emulation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a Windows or Linux PC running CrossEscPos appear as a Bluetooth receipt printer (Classic SPP for Android, BLE GATT for iOS/Android), and fix the known reliability bugs in the interpreter and existing transports.

**Architecture:** Core gets parser recovery and per-job ownership so several transports can feed one printer safely. A new packable `CrossEscPos.Transports.Bluetooth` holds a platform-neutral engine (BLE write queues, notification chunking, SPP stream pumping) plus two thin platform layers: WinRT on `net10.0-windows10.0.19041.0`, BlueZ over D-Bus (Tmds.DBus.Protocol) on `net10.0`. The desktop app shows two new connection-panel entries backed by that package.

**Tech Stack:** .NET 10 (SDK 10.0.300+, installed here at `%LOCALAPPDATA%\Microsoft\dotnet`), Avalonia 12.0.4, CommunityToolkit.Mvvm, WinRT Bluetooth APIs (Windows SDK projection), Tmds.DBus.Protocol 0.92.0, xUnit 2.9.2 (existing projects) / xunit.v3 3.2.2 (new Bluetooth tests).

**Spec:** `docs/superpowers/specs/2026-10-23-bluetooth-printer-design.md`

## Global Constraints

- Build must pass with `TreatWarningsAsErrors=true`, `Nullable=enable`, `ImplicitUsings=disable` (write explicit `using`s), `LangVersion=latest` (all from `Directory.Build.props`). Test projects keep their existing `TreatWarningsAsErrors=false`.
- Use the installed SDK: `export PATH="$LOCALAPPDATA/Microsoft/dotnet:$PATH" DOTNET_ROOT="$LOCALAPPDATA/Microsoft/dotnet"` (bash) before any `dotnet` command. The machine-wide `C:\Program Files\dotnet` has no SDK.
- Windows target framework is exactly `net10.0-windows10.0.19041.0`. Linux needs BlueZ 5.50+.
- Must not break: the browser (`net10.0-browser`) head, macOS builds, CI (`ubuntu-latest`), or release (`win-x64` is published **on ubuntu**, so Windows-TFM projects need `<EnableWindowsTargeting>true</EnableWindowsTargeting>`).
- A project that sets `<TargetFrameworks>` must also set `<TargetFramework></TargetFramework>` (empty), because `Directory.Build.props` sets `TargetFramework=net10.0` and would otherwise turn the outer build into an inner build.
- `Tmds.DBus.Protocol` version is pinned to **0.92.0**, the version Avalonia.FreeDesktop 12.0.4 depends on, so the desktop app doesn't load a newer, possibly incompatible version under Avalonia.
- Package names, authors, URLs and `Version` (1.3.2) stay unchanged. The new project is packable and gets added to the release `pack` list.
- SPP UUID `00001101-0000-1000-8000-00805F9B34FB`. BLE profiles, verbatim from the spec:
  - Service `000018F0-0000-1000-8000-00805F9B34FB`, write `00002AF1-0000-1000-8000-00805F9B34FB` (write + write-without-response), notify `00002AF0-0000-1000-8000-00805F9B34FB`. **The only advertised service.**
  - Service `E7810A71-73AE-499D-8C15-FAA9AEF0C3F2`, one characteristic `BEF8D6C9-9C21-4C9E-B632-BD58C1009F9F` (write, write-without-response, notify, read).
  - Service `49535343-FE7D-4AE5-8FA9-9FAFD205E455`, write `49535343-8841-43F4-A8D4-ECBE34729BB3`, notify `49535343-1E4D-4BD9-BA61-23C647249616`.
- Bluetooth is **off until the user starts it** (UI button, or env vars `ESCPOS_BT_SPP=1`, `ESCPOS_BT_BLE=1`; name from `ESCPOS_BT_NAME`, default `CrossEscPos`; Linux auto-pair agent only with `ESCPOS_BT_AUTO_PAIR=1`). Never register as the default BlueZ agent unless auto-pair is on.
- Default job idle timeout for printer ownership: **1 second**.
- Status lines must never say "running"/"advertising" unless the OS API confirmed it.

## Review Focus

1. **A phone disconnects mid-job while it owns the printer.** Others must not wait out the idle timeout; ownership is released on disconnect. (Test in Task 2.)
2. **A large job over BLE** (≈100 KB raster sent as thousands of 20-byte write-without-response packets) must reach the printer complete and in order. (Test in Task 5.)
3. **Two senders at once** (e.g. TCP client and a phone) must not mix bytes inside each other's commands. (Test in Task 2.)
4. **A long or non-ASCII printer name** must not make BLE advertising fail. Legacy advertising leaves 20 bytes for the name. (Test in Task 4.)
5. **A phone sends a status query over BLE before subscribing to notifications.** The reply is dropped with a log line, without throwing or blocking later jobs. (Test in Task 5.)

---

## File Structure

```
src/CrossEscPos.Core/
  EscPos/EscPosInterpreter.cs              (modify: unknown-prefix recovery, ResetParserState)
  Emulator/ReceiptPrinter.cs               (modify: feed lock, job ownership, realtime-while-busy)
  Emulator/RealtimeQuery.cs                (create: recognises payloads made only of DLE real-time commands)
  Emulator/PrinterStreamFeeder.cs          (create: Stream -> printer pump shared by serial/SPP)
src/CrossEscPos.Transports/
  NetServer.cs, NetClient.cs, SerialServer.cs  (modify: client cleanup, idempotent close, EOF handling)
src/CrossEscPos.Transports.Bluetooth/       (create, packable, net10.0 + net10.0-windows10.0.19041.0)
  CrossEscPos.Transports.Bluetooth.csproj
  BluetoothPrinter.cs                      (factory: CreateSpp / CreateBle)
  BluetoothPrinterOptions.cs
  BluetoothSupport.cs                      (BluetoothSupportState enum + record)
  IBluetoothPrinterServer.cs
  UnsupportedBluetoothServer.cs
  Ble/BlePrinterProfiles.cs                (the three services, UUID constants)
  Ble/BleChunker.cs
  Ble/BleGattPrinter.cs                    (platform-neutral engine)
  Ble/IBleNotifier.cs
  Windows/WinRtSppServer.cs                (compiled only for the Windows TFM)
  Windows/WinRtBleServer.cs
  Windows/WindowsBluetoothProbe.cs
  Linux/BlueZBus.cs                        (connection, property get/set, ObjectManager lookup, adapter state restore)
  Linux/UnixFdStream.cs
  Linux/BlueZSppServer.cs                  (Profile1 export + RegisterProfile)
  Linux/BlueZBleServer.cs                  (GATT application, LEAdvertisement1)
  Linux/BlueZAgent.cs                      (opt-in Just Works agent)
tests/CrossEscPos.Core.Tests/              (add tests to existing files)
tests/CrossEscPos.Transports.Tests/        (create: NetServer/serial-feeder tests, xUnit 2.9.2)
tests/CrossEscPos.Transports.Bluetooth.Tests/  (create, xunit.v3; net10.0, plus Windows TFM when built on Windows)
  BleChunkerTests.cs, BleGattPrinterTests.cs, OptionsTests.cs
  Linux/FakeBlueZ.cs, Linux/BlueZIntegrationTests.cs   (run only on Linux with ESCPOS_BT_FAKE_BLUEZ=1)
  Windows/WinRtSmokeTests.cs                           (run only with ESCPOS_BT_HARDWARE_TESTS=1)
src/CrossEscPos.App.Desktop/
  CrossEscPos.App.Desktop.csproj           (modify: conditional Windows TFM, reference Bluetooth)
  Transports/BluetoothTransportEntry.cs    (create)
  DesktopPlatformServices.cs               (modify: add the two entries)
docs/Bluetooth.md (create), README.md, docs/Architecture.md, docs/Packages.md, docs/Transports.md, docs/Connecting.md, docs/_Sidebar.md, docs/README.md (modify)
.github/workflows/ci.yml, .github/workflows/release.yml, CrossEscPos.slnx (modify)
```

---

### Task 1: Baseline and interpreter recovery

**Files:**
- Modify: `src/CrossEscPos.Core/EscPos/EscPosInterpreter.cs` (prefix handling ~lines 230-275; buffers region)
- Modify: `src/CrossEscPos.Core/Emulator/ReceiptPrinter.cs` (`FeedEscPos(string, …)` catch block)
- Test: `tests/CrossEscPos.Core.Tests/EscPosInterpretationTests.cs` (robustness section)

**Interfaces:**
- Produces: `public void EscPosInterpreter.ResetParserState()`: clears the command and real-time buffers and returns to normal mode, without touching the print buffer.

- [ ] **Step 1: Record the baseline.** Run `dotnet build CrossEscPos.slnx -c Release` and `dotnet test CrossEscPos.slnx -c Release`. Write the warning/error/failure counts into the commit message of Step 7. Any pre-existing failure becomes an extra "existing problem": fix it in this task with a regression test, or note it for Task 11's report if it's environmental (e.g. needs a display).
- [ ] **Step 2: Write the failing tests**

```csharp
[Fact]
public void UnknownCommand_TextAfterItInSamePayload_StillPrints()
{
    _printer.FeedEscPos(Bytes(ESC, 0x99) + "after" + Bytes(LF));
    Assert.Contains("after", RenderReceipt().DrawnText);
}

[Fact]
public void UnknownCommand_DoesNotBreakLaterJobs()
{
    _printer.FeedEscPos(Bytes(GS, '(', 'Z'));          // unknown 3-byte prefix
    _printer.FeedEscPos("next job" + Bytes(LF));
    Assert.Contains("next job", RenderReceipt().DrawnText);
}

[Fact]
public void UnknownCommand_FollowedByCommandStarter_ReprocessesStarter()
{
    // "ESC GS" is not a command; the GS must be reprocessed so "GS r 1" is still answered.
    var responder = new CapturingResponder();
    _printer.FeedEscPos(Bytes(ESC, GS, 'r', 1), responder);
    Assert.Single(responder.Bytes);                    // GS r 1 answered
}
```

- [ ] **Step 3: Run them.** `dotnet test tests/CrossEscPos.Core.Tests --filter "FullyQualifiedName~UnknownCommand"`. Expected: the first two FAIL (text missing), and the third FAILS (no reply).
- [ ] **Step 4: Implement recovery.** At registration, build a `HashSet<string>` of every proper prefix of every registered command. In prefix mode, once the buffer is neither a registered command nor a proper prefix, log `Unsupported command: 0x.. 0x..` (`Logger.Info`), call `ResetParserState()`, and if the last byte is `ESC`/`FS`/`GS`/`DLE`, reprocess it in normal mode (step the loop index back by one). Never throw. In `ReceiptPrinter.FeedEscPos`'s catch block, call `_escPosInterpreter.ResetParserState()` so an exception thrown by a command's `Execute`/`InterpretNextChar` can't leave the parser stuck mid-command either.
- [ ] **Step 5: Run the Core tests.** `dotnet test tests/CrossEscPos.Core.Tests`. Expected: all PASS, including the existing `UnsupportedCommand_DoesNotThrow`.
- [ ] **Step 6: Build with zero warnings.** `dotnet build src/CrossEscPos.Core -c Release`.
- [ ] **Step 7: Commit.** `git commit -m "fix(core): recover from unknown ESC/POS commands instead of wedging the parser"`, with the baseline numbers in the body.

### Task 2: Thread-safe feeding with per-job ownership

**Files:**
- Modify: `src/CrossEscPos.Core/Emulator/ReceiptPrinter.cs`
- Create: `src/CrossEscPos.Core/Emulator/RealtimeQuery.cs`
- Test: `tests/CrossEscPos.Core.Tests/EmulatorTests.cs` (new `// ---- concurrency ----` section)

**Interfaces:**
- Produces on `ReceiptPrinter`:
  - `public TimeSpan JobIdleTimeout { get; set; }`, default `TimeSpan.FromSeconds(1)`.
  - `public void ReleaseOwnership(IPrinterResponder owner)`: ends that sender's job immediately and wakes waiters. `UnregisterResponder` calls it too.
  - The existing `FeedEscPos(…, IPrinterResponder? responder)` semantics: a non-null responder is the **owner key**. If another owner holds the printer and has fed within `JobIdleTimeout`, the call blocks (`Monitor.Wait` on the feed lock, re-checking idle expiry) until the printer is free; then this responder becomes the owner. `responder == null` feeds take the lock but never take or wait for ownership. All feeds run under one lock, so `_currentResponder` and `_blockedNotified` are per-call.
- Produces: `internal static class RealtimeQuery { public static bool IsRealtimeOnly(ReadOnlySpan<char> payload); }`. True when the payload is one or more complete `DLE EOT n`, `DLE ENQ`, or `DLE DC4 1 m t` sequences and nothing else. A waiting (non-owner) feed whose payload satisfies this is answered immediately, outside the ownership wait, by a separate real-time path that doesn't touch the owner's parse state.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void TwoSenders_CommandSplitAcrossChunks_IsNotCorruptedByOtherSender()
{
    var a = new CapturingResponder(); var b = new CapturingResponder();
    var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromMilliseconds(300);
    printer.FeedEscPos(Bytes(GS, 'r'), a);                         // A: half of "GS r 1"
    var bTask = Task.Run(() => printer.FeedEscPos(Bytes(DLE, EOT, 1) + "B" + Bytes(LF), b));
    Thread.Sleep(50);
    printer.FeedEscPos(Bytes(1), a);                               // A completes GS r 1
    Assert.True(bTask.Wait(2000));
    Assert.Single(a.Bytes);                                        // A got exactly its GS r reply
    Assert.Single(b.Bytes);                                        // B got exactly its DLE EOT reply
}

[Fact]
public void RealtimeStatusFromWaitingSender_IsAnsweredWithoutWaiting()
{
    var a = new CapturingResponder(); var b = new CapturingResponder();
    var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromSeconds(5);
    printer.FeedEscPos("A owns the printer", a);
    var sw = Stopwatch.StartNew();
    printer.FeedEscPos(Bytes(DLE, EOT, 1), b);
    Assert.True(sw.ElapsedMilliseconds < 500);
    Assert.Single(b.Bytes);
}

[Fact]
public void OwnerDisconnect_ReleasesPrinterImmediately()
{
    var a = new CapturingResponder(); var b = new CapturingResponder();
    var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromSeconds(30);
    printer.RegisterResponder(a);
    printer.FeedEscPos("A", a);
    printer.UnregisterResponder(a);                                 // phone went away
    var sw = Stopwatch.StartNew();
    printer.FeedEscPos("B" + Bytes(LF), b);
    Assert.True(sw.ElapsedMilliseconds < 500);
}

[Fact]
public void IdleOwner_IsReplacedAfterTimeout()
{
    var a = new CapturingResponder(); var b = new CapturingResponder();
    var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromMilliseconds(200);
    printer.FeedEscPos("A", a);
    var sw = Stopwatch.StartNew();
    printer.FeedEscPos("B" + Bytes(LF), b);
    Assert.InRange(sw.ElapsedMilliseconds, 100, 1500);
}

[Fact]
public void NullResponderFeeds_NeverWait()
{
    var a = new CapturingResponder();
    var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromSeconds(30);
    printer.FeedEscPos("A", a);
    var sw = Stopwatch.StartNew();
    printer.FeedEscPos("direct API" + Bytes(LF));
    Assert.True(sw.ElapsedMilliseconds < 500);
}

[Fact]
public void ConcurrentFeeds_FromManyThreads_DoNotThrowOrLoseJobs()
{
    var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromMilliseconds(20);
    Parallel.For(0, 8, i => printer.FeedEscPos($"job{i}" + Bytes(LF), new CapturingResponder()));
    var text = string.Join("|", Render(printer).DrawnText);
    for (int i = 0; i < 8; i++) Assert.Contains($"job{i}", text);
}
```

(`NewPrinter()`/`Render()` are small helpers in the test class, following `EscPosInterpretationTests`. Move `CapturingResponder` to a shared internal test file if needed.)

- [ ] **Step 2: Run them.** `dotnet test tests/CrossEscPos.Core.Tests --filter "FullyQualifiedName~EmulatorTests"`. Expected: the ownership tests FAIL (B's reply lands inside A's command; no ownership members exist yet, so it doesn't compile).
- [ ] **Step 3: Implement** `JobIdleTimeout`, `ReleaseOwnership`, the ownership wait, `RealtimeQuery.IsRealtimeOnly`, and the real-time fast path in `ReceiptPrinter`. The fast path reuses `TransmitRealtimeStatus` / `RealtimeRecover` / `KickCashDrawer`, with that call's responder as the reply target. It runs under the feed lock but never touches `EscPosInterpreter`. `OnActivityEvent` fires outside the lock.
- [ ] **Step 4: Run all Core tests.** `dotnet test tests/CrossEscPos.Core.Tests`. Expected: all PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(core): serialize feeds and give each sender ownership of the printer for its job"`

### Task 3: Shared stream feeder, TCP and serial fixes

**Files:**
- Create: `src/CrossEscPos.Core/Emulator/PrinterStreamFeeder.cs`
- Modify: `src/CrossEscPos.Transports/SerialServer.cs` (receive loop), `NetServer.cs`, `NetClient.cs`
- Create: `tests/CrossEscPos.Transports.Tests/CrossEscPos.Transports.Tests.csproj` (copy the `CrossEscPos.Core.Tests` csproj, referencing Core + Transports), `NetServerTests.cs`, `PrinterStreamFeederTests.cs`
- Modify: `CrossEscPos.slnx` (add the test project under `/tests/`)

**Interfaces:**
- Produces: `public static class PrinterStreamFeeder { public static Task<int> PumpAsync(ReceiptPrinter printer, Stream input, IPrinterResponder responder, CancellationToken ct, int bufferSize = 16 * 1024); }`. It reads until EOF (read returns 0), cancellation, or IOException/ObjectDisposedException, feeds each chunk with `responder`, returns the total bytes read, and always calls `printer.ReleaseOwnership(responder)` before returning.
- Produces on `NetServer`: `public int ClientCount { get; }`, `public IPEndPoint? BoundEndPoint { get; }` (the actual bound endpoint, so port 0 works). `Stop()` closes all clients.
- `NetClient.Close()` is idempotent and never throws. It raises `internal event Action<NetClient>? Closed`, which the server uses to remove the client.
- `SerialServer` uses `PrinterStreamFeeder.PumpAsync`. EOF means the port is gone: set `IsRunning=false` and log. No spin.

- [ ] **Step 1: Write the failing tests**

```csharp
// PrinterStreamFeederTests
[Fact] public async Task Pump_FeedsAllBytes_AndReturnsOnEof()        // MemoryStream("Hello\n") -> returns 6, receipt contains "Hello"
[Fact] public async Task Pump_StopsOnCancellation()                  // blocking pipe stream + cancel -> completes within 1 s
[Fact] public async Task Pump_ReleasesOwnership_OnEof()              // owner A pumps EOF, then B feeds with JobIdleTimeout=30s -> < 500 ms

// NetServerTests (loopback, port 0)
[Fact] public async Task ClientDisconnect_IsRemovedFromServer()      // connect, send "x\n", close -> ClientCount reaches 0 within 2 s
[Fact] public async Task Stop_ClosesConnectedClients()               // connect, Stop() -> client's Receive returns 0 within 2 s
[Fact] public async Task StatusQuery_GetsReplyOverTcp()              // send DLE EOT 1 -> 1 byte back
[Fact] public void Close_Twice_DoesNotThrow()                        // NetClient via accepted socket, Close(); Close();
```

- [ ] **Step 2: Run them.** `dotnet test tests/CrossEscPos.Transports.Tests`. Expected: compile failure, then FAILs for `ClientCount`/`Stop` behaviour.
- [ ] **Step 3: Implement** the feeder and the fixes. `NetClient.ReceiveLoopAsync` keeps its socket loop (it needs no Stream) but must call `Close()` on EOF/error exactly once.
- [ ] **Step 4: Run** `dotnet test tests/CrossEscPos.Transports.Tests` and `dotnet test tests/CrossEscPos.Core.Tests`. Expected: all PASS.
- [ ] **Step 5: Commit.** `git commit -m "fix(transports): clean up TCP clients, stop serial busy-loop, add shared stream feeder"`

### Task 4: Bluetooth package skeleton, public API, profiles, chunker

**Files:**
- Create: the `src/CrossEscPos.Transports.Bluetooth/` files listed in File Structure, except `Ble/BleGattPrinter.cs` and the `Windows/` and `Linux/` folders.
- Create: `tests/CrossEscPos.Transports.Bluetooth.Tests/CrossEscPos.Transports.Bluetooth.Tests.csproj`, `BleChunkerTests.cs`, `OptionsTests.cs`
- Modify: `CrossEscPos.slnx`

**csproj essentials:** `<TargetFramework></TargetFramework>`, `<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>`, `<EnableWindowsTargeting>true</EnableWindowsTargeting>`, `<IsPackable>true</IsPackable>`, a `Description`, `ProjectReference` to Core, `PackageReference Tmds.DBus.Protocol 0.92.0` (net10.0 only). `Windows/**` is compiled only when `$(TargetFramework)` contains `-windows` and `Linux/**` only for plain `net10.0`, so the Windows TFM build carries no D-Bus code. Define `WINDOWS_BT` for the Windows TFM. The test csproj uses `<TargetFrameworks>net10.0</TargetFrameworks>`, plus `;net10.0-windows10.0.19041.0` when `$([MSBuild]::IsOSPlatform('Windows'))`, with xunit.v3 3.2.2, xunit.runner.visualstudio 3.1.5, and Microsoft.NET.Test.Sdk 17.12.0.

**Interfaces (namespace `CrossEscPos.Transports.Bluetooth`):**
```csharp
public sealed class BluetoothPrinterOptions
{
    public string Name { get; set; } = "CrossEscPos";
    public bool AutoAcceptPairing { get; set; }            // Linux only
    public string? DBusAddress { get; set; }               // Linux; null = system bus (tests use the session bus)
    public string? AdapterName { get; set; }               // Linux, e.g. "hci1"; null = first adapter
    public string AdvertisedName { get; }                  // Name trimmed to <= 20 UTF-8 bytes on a char boundary; "CrossEscPos" if empty
}
public enum BluetoothSupportState { Supported, NoAdapter, AdapterOff, NotSupportedByAdapter, PermissionDenied, UnsupportedPlatform, Error }
public sealed record BluetoothSupport(BluetoothSupportState State, string Message) { public bool IsSupported => State == BluetoothSupportState.Supported; }
public interface IBluetoothPrinterServer : IAsyncDisposable
{
    string DisplayName { get; }          // "Bluetooth (Android / SPP)" | "Bluetooth LE (iPhone / BLE)"
    bool IsRunning { get; }
    int ConnectedClients { get; }
    string StatusText { get; }           // one plain-language line for the UI
    event Action? StatusChanged;         // may fire on any thread
    Task<BluetoothSupport> StartAsync(CancellationToken ct = default);   // IsRunning only after the OS confirmed
    Task StopAsync();
}
public static class BluetoothPrinter
{
    public static IBluetoothPrinterServer CreateSpp(ReceiptPrinter printer, BluetoothPrinterOptions options);
    public static IBluetoothPrinterServer CreateBle(ReceiptPrinter printer, BluetoothPrinterOptions options);
}
public static class BlePrinterProfiles
{
    public static readonly Guid SppServiceUuid;                       // 00001101-…
    public static IReadOnlyList<BlePrinterProfile> All { get; }       // order: 18F0, E7810A71, 49535343
}
public sealed record BlePrinterProfile(string Name, Guid ServiceUuid, Guid WriteUuid, Guid NotifyUuid, bool Advertised);
// For E7810A71, WriteUuid == NotifyUuid == BEF8D6C9-…
public static class BleChunker { public static IEnumerable<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> data, int attMtu); }
// chunk size = max(20, attMtu - 3); empty input yields nothing
```
In this task the factory returns `UnsupportedBluetoothServer` everywhere (`StartAsync` returns `UnsupportedPlatform` with the message `"Bluetooth printer emulation isn't supported on <OS> yet."`). On Windows under plain `net10.0`, the message is `"This build has no Windows Bluetooth support; use the net10.0-windows10.0.19041.0 target."`. Tasks 6–9 swap in the real servers.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData(23, 20)] [InlineData(185, 182)] [InlineData(517, 514)] [InlineData(0, 20)]
public void Split_UsesMtuMinusThree_MinimumTwenty(int mtu, int expectedChunk)
{
    var data = new byte[1000];
    var chunks = BleChunker.Split(data, mtu).ToList();
    Assert.All(chunks.SkipLast(1), c => Assert.Equal(expectedChunk, c.Length));
    Assert.Equal(1000, chunks.Sum(c => c.Length));
}

[Fact] public void Split_Empty_YieldsNothing() => Assert.Empty(BleChunker.Split(ReadOnlyMemory<byte>.Empty, 23));

[Theory]
[InlineData("CrossEscPos", "CrossEscPos")]
[InlineData("A very long printer name that overflows", "A very long printer ")]   // 20 bytes
[InlineData("Café Café Café Café", "Café Café Café Ca")]                         // é is 2 bytes; no split char
[InlineData("", "CrossEscPos")]
public void AdvertisedName_FitsLegacyAdvertising(string name, string expected)
    => Assert.Equal(expected, new BluetoothPrinterOptions { Name = name }.AdvertisedName);

[Fact]
public void Profiles_MatchSpecUuids()
{
    var p = BlePrinterProfiles.All;
    Assert.Equal(Guid.Parse("000018F0-0000-1000-8000-00805F9B34FB"), p[0].ServiceUuid);
    Assert.True(p[0].Advertised); Assert.False(p[1].Advertised); Assert.False(p[2].Advertised);
    Assert.Equal(p[1].WriteUuid, p[1].NotifyUuid);
    Assert.Equal(Guid.Parse("49535343-1E4D-4BD9-BA61-23C647249616"), p[2].NotifyUuid);
}
```

- [ ] **Step 2: Run them.** `dotnet test tests/CrossEscPos.Transports.Bluetooth.Tests`. Expected: compile failure.
- [ ] **Step 3: Implement** the files above.
- [ ] **Step 4: Run** the tests (both TFMs on Windows), then `dotnet build CrossEscPos.slnx -c Release`. Expected: PASS, zero warnings.
- [ ] **Step 5: Commit.** `git commit -m "feat(bluetooth): add Bluetooth transport package skeleton, BLE profiles and chunking"`

### Task 5: Platform-neutral BLE engine

**Files:**
- Create: `src/CrossEscPos.Transports.Bluetooth/Ble/IBleNotifier.cs`, `Ble/BleGattPrinter.cs`
- Test: `tests/CrossEscPos.Transports.Bluetooth.Tests/BleGattPrinterTests.cs`

**Interfaces:**
```csharp
public interface IBleNotifier
{
    // Send one chunk (already <= MTU-3) on the profile's notify characteristic to one client.
    Task NotifyAsync(BlePrinterProfile profile, string clientId, ReadOnlyMemory<byte> chunk);
}
public sealed class BleGattPrinter : IAsyncDisposable
{
    public BleGattPrinter(Action<byte[], IPrinterResponder> feed, Action<IPrinterResponder> release,
                          Action<IPrinterResponder> register, Action<IPrinterResponder> unregister, IBleNotifier notifier);
    public BleGattPrinter(ReceiptPrinter printer, IBleNotifier notifier);   // wires FeedEscPos / ReleaseOwnership / Register / UnregisterResponder
    public int ConnectedClients { get; }
    public event Action? ClientsChanged;
    public void OnWrite(string clientId, BlePrinterProfile profile, ReadOnlySpan<byte> data, int attMtu); // copies, enqueues, returns at once
    public void OnSubscribed(string clientId, BlePrinterProfile profile, int attMtu);
    public void OnUnsubscribed(string clientId, BlePrinterProfile profile);
    public void OnDisconnected(string clientId);   // drops the queue, releases ownership, unregisters responders
    public Task DrainAsync(string clientId);       // test/teardown helper: completes when the queue is empty
}
```
Behaviour: one ordered queue and one consumer task per client (`System.Threading.Channels`, unbounded, single reader). Each (client, profile) pair has one responder object. Its `Send` chunks with `BleChunker` at the last MTU seen for that client (default 23) and awaits `NotifyAsync` sequentially. If the client isn't subscribed to that profile's notify characteristic, it logs `"BLE reply dropped: client not subscribed"` and returns. On subscribe, the responder is registered with the printer (for Automatic Status Back); on unsubscribe or disconnect, it is unregistered and ownership released. A client with any pending writes or subscriptions counts as connected.

- [ ] **Step 1: Write the failing tests** (fake notifier records `(profile, clientId, bytes)`; a fake feed records concatenated bytes per responder)

```csharp
[Fact] public async Task Writes_AreFedInOrder_ForLargeJob()               // 100_000 random bytes in 20-byte OnWrite calls -> fed bytes equal the input exactly
[Fact] public async Task Reply_IsNotifiedOnSameProfile_ChunkedByMtu()     // subscribe(profile 18F0, mtu 23); feed callback sends 50 bytes -> chunks 20,20,10 on 18F0
[Fact] public async Task Reply_BeforeSubscribe_IsDroppedWithoutThrowing() // feed sends reply, no subscription -> notifier never called; a later write still fed
[Fact] public async Task Subscribe_RegistersResponder_UnsubscribeUnregisters()
[Fact] public async Task Disconnect_ReleasesOwnership_AndClearsClient()   // ConnectedClients 1 -> 0; release called with that responder
[Fact] public async Task TwoClients_HaveSeparateQueuesAndResponders()
[Fact] public async Task EndToEnd_WithRealPrinter_StatusQueryNotifiesOneByte() // ReceiptPrinter + fake backend from Core.Tests (link FakeRenderBackend.cs as a compile item)
```

- [ ] **Step 2: Run them.** Expected: compile failure.
- [ ] **Step 3: Implement `BleGattPrinter`.**
- [ ] **Step 4: Run** `dotnet test tests/CrossEscPos.Transports.Bluetooth.Tests`. Expected: PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(bluetooth): BLE engine with ordered per-client queues and chunked notifications"`

### Task 6: Windows Classic SPP server (WinRT)

**Files:**
- Create: `src/CrossEscPos.Transports.Bluetooth/Windows/WindowsBluetoothProbe.cs`, `Windows/WinRtSppServer.cs`
- Modify: `BluetoothPrinter.cs` (`#if WINDOWS_BT` → `WinRtSppServer`)
- Test: `tests/CrossEscPos.Transports.Bluetooth.Tests/Windows/WinRtSmokeTests.cs` (Windows TFM only)

**Interfaces:**
- `internal static class WindowsBluetoothProbe { public static Task<BluetoothSupport> ProbeAsync(bool needClassic, bool needPeripheral); }` maps: no default adapter → `NoAdapter`; radio not `On` → `AdapterOff` ("Bluetooth is turned off in Windows settings."); `!IsClassicSupported` / `!IsPeripheralRoleSupported` → `NotSupportedByAdapter`; `UnauthorizedAccessException` → `PermissionDenied` (message mentions Settings → Privacy → Bluetooth/Radios).
- `internal sealed class WinRtSppServer : IBluetoothPrinterServer`. Uses `RfcommServiceProvider.CreateAsync(RfcommServiceId.SerialPort)`; `StreamSocketListener.BindServiceNameAsync(serviceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication)`; SDP attribute `0x0100` = `[0x25, len, UTF-8 name]`; `StartAdvertising(listener, radioDiscoverable: true)`. Each `ConnectionReceived` gets a per-connection responder (writes to `OutputStream.AsStreamForWrite()` + flush) and runs `PrinterStreamFeeder.PumpAsync` on `InputStream.AsStreamForRead()`. The connection is disposed after the pump returns. It subscribes to `Radio.StateChanged`: radio off → status "Bluetooth turned off", `IsRunning=false`. `StatusText` while running: `"Advertising serial port as \"{Name}\" on {computer name} ({n} connected). Pair this PC from the phone's Bluetooth settings."`
- **Risk:** if WinRT refuses the server role for an unpackaged desktop app, implement SPP with Winsock instead (`AF_BTH=32`, `BTHPROTO_RFCOMM=3`, bind `BT_PORT_ANY`, `WSASetService` with SPP class) behind the same class, and note it in the commit.

- [ ] **Step 1: Write the smoke test** (skip unless `ESCPOS_BT_HARDWARE_TESTS=1`, using `Assert.Skip`)

```csharp
[Fact]
public async Task Spp_StartsAndStops_OnThisPc()
{
    SkipUnlessHardware();
    await using var server = BluetoothPrinter.CreateSpp(NewPrinter(), new BluetoothPrinterOptions { Name = "CrossEscPos Test" });
    var support = await server.StartAsync();
    Assert.True(support.IsSupported, support.Message);
    Assert.True(server.IsRunning);
    Assert.Contains("Advertising serial port", server.StatusText);
    await server.StopAsync();
    Assert.False(server.IsRunning);
}
```

- [ ] **Step 2: Run it.** `ESCPOS_BT_HARDWARE_TESTS=1 dotnet test tests/CrossEscPos.Transports.Bluetooth.Tests -f net10.0-windows10.0.19041.0 --filter Spp_`. Expected: FAIL (it's the unsupported stub).
- [ ] **Step 3: Implement** the probe and `WinRtSppServer`.
- [ ] **Step 4: Run it again.** Expected: PASS on this PC. Also run once without the env var and expect SKIPPED.
- [ ] **Step 5: Commit.** `git commit -m "feat(bluetooth): Windows SPP server via WinRT RFCOMM"`

### Task 7: Windows BLE GATT server (WinRT)

**Files:**
- Create: `src/CrossEscPos.Transports.Bluetooth/Windows/WinRtBleServer.cs`
- Modify: `BluetoothPrinter.cs`, `Windows/WinRtSmokeTests.cs`

**Interfaces:**
- `internal sealed class WinRtBleServer : IBluetoothPrinterServer, IBleNotifier`. For each `BlePrinterProfiles.All` entry, it runs `GattServiceProvider.CreateAsync(serviceUuid)` and creates the write characteristic (`Write | WriteWithoutResponse`, plus `Notify | Read` when the write and notify UUIDs are equal) and the notify characteristic (`Notify`), all with `GattProtectionLevel.Plain`. `WriteRequested` takes a deferral, reads `request.Value` via `DataReader`, calls `engine.OnWrite(session.DeviceId.Id, profile, bytes, session.MaxPduSize)`, and calls `request.Respond()` for `WriteWithResponse`. `SubscribedClientsChanged` diffs into `OnSubscribed`/`OnUnsubscribed`. `GattSession.SessionStatusChanged` to `Closed` → `OnDisconnected`. `NotifyAsync` finds the matching `GattSubscribedClient` by `Session.DeviceId.Id` and calls `NotifyValueAsync(buffer, client)`. `StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = profile.Advertised })` runs on **every** provider, because Windows only publishes a service whose provider is advertising. `IsRunning` becomes true only when the 18F0 provider reports `AdvertisementStatus` `Started` or `StartedWithoutAllAdvertisementData`; `Aborted` → status with the error. `StatusText` while running: `"Advertising BLE printer service 18F0 as \"{computer name}\" ({n} connected). Windows always uses the PC name over BLE."`

- [ ] **Step 1: Write the smoke test**

```csharp
[Fact]
public async Task Ble_PublishesAllThreeServices_AndAdvertises()
{
    SkipUnlessHardware();
    await using var server = BluetoothPrinter.CreateBle(NewPrinter(), new BluetoothPrinterOptions());
    var support = await server.StartAsync();
    Assert.True(support.IsSupported, support.Message);
    Assert.True(server.IsRunning);
    Assert.Contains("18F0", server.StatusText);
    await server.StopAsync();
}

[Fact]
public async Task Ble_And_Spp_RunTogether()   // start both, both IsRunning, stop both
```

- [ ] **Step 2: Run them.** Expected: FAIL.
- [ ] **Step 3: Implement `WinRtBleServer`.**
- [ ] **Step 4: Run them.** Expected: PASS on this PC, all Windows smoke tests together.
- [ ] **Step 5: Commit.** `git commit -m "feat(bluetooth): Windows BLE GATT peripheral with the three printer profiles"`

### Task 8: Linux BlueZ plumbing and SPP server, with a fake BlueZ

**Files:**
- Create: `src/CrossEscPos.Transports.Bluetooth/Linux/BlueZBus.cs`, `Linux/UnixFdStream.cs`, `Linux/BlueZSppServer.cs`
- Create: `tests/CrossEscPos.Transports.Bluetooth.Tests/Linux/FakeBlueZ.cs`, `Linux/BlueZIntegrationTests.cs`
- Modify: `BluetoothPrinter.cs` (on Linux with plain `net10.0`, return the BlueZ servers)

**Interfaces:**
- `internal sealed class BlueZBus : IAsyncDisposable`
  - `static Task<BlueZBus> ConnectAsync(string? address)`: `new Connection(address ?? DBusAddress.System!)`.
  - `Connection Connection`.
  - `Task<string?> FindAdapterAsync(string? name)`: `org.freedesktop.DBus.ObjectManager.GetManagedObjects` on `/` of `org.bluez`; returns the first path with `org.bluez.Adapter1` (or the one ending in `/{name}`).
  - `Task<VariantValue> GetAsync(string path, string iface, string prop)`, `Task SetAsync(string path, string iface, string prop, VariantValue value)`.
  - `Task PrepareAdapterAsync(string adapterPath)`: sets `Powered`, `Discoverable`, `Pairable` true and `DiscoverableTimeout` 0, remembering the values it changed.
  - `Task RestoreAdapterAsync()`: puts back only what it changed.
  - It maps D-Bus errors to `BluetoothSupport`: `org.freedesktop.DBus.Error.ServiceUnknown` → `NoAdapter` ("BlueZ (bluetoothd) isn't running."); `AccessDenied` → `PermissionDenied` ("Add your user to the 'bluetooth' group, then log in again."); no adapter → `NoAdapter`; connect failure → `Error`.
- `internal sealed class UnixFdStream : Stream`: wraps an owned fd using libc `read`/`write`/`close` (`[LibraryImport("libc", SetLastError = true)]`). Reads block on a dedicated thread (the pump calls it from `Task.Run`); `Dispose` calls `shutdown(fd, SHUT_RDWR)` then `close`.
- `internal sealed class BlueZSppServer : IBluetoothPrinterServer, IPathMethodHandler`
  - Path `/com/crossescpos/spp`. Handles `org.bluez.Profile1`: `NewConnection(o device, h fd, a{sv})` reads the fd with `reader.ReadHandle<SafeFileHandle>()`, takes ownership of the raw fd, and runs `PrinterStreamFeeder.PumpAsync` over a `UnixFdStream` with a per-connection responder; `RequestDisconnection(o)` disposes that device's streams; `Release()` → `IsRunning=false`, status "BlueZ released the serial profile". It answers `Introspect`.
  - `StartAsync`: connect, find the adapter, `PrepareAdapterAsync`, optionally start `BlueZAgent` (Task 9's class; until it exists, skip when `AutoAcceptPairing` is false), then `org.bluez.ProfileManager1.RegisterProfile(o /com/crossescpos/spp, s SPP UUID, a{sv} {Name: s options.Name, Role: s "server", RequireAuthentication: b false, RequireAuthorization: b false, AutoConnect: b false})` on `/org/bluez`.
  - `StopAsync`: `UnregisterProfile`, close the connections, `RestoreAdapterAsync`.
  - `StatusText` while running: `"Advertising serial port as \"{Name}\" on {adapter Alias} ({n} connected). Pair this computer from the phone's Bluetooth settings."`
- Test fake: `internal sealed class FakeBlueZ : IAsyncDisposable`. It connects to the **session** bus, `RequestNameAsync("org.bluez")`, and exports:
  - `/` with `ObjectManager.GetManagedObjects` → `/org/bluez/hci0` with `Adapter1`
  - `/org/bluez/hci0` with `org.freedesktop.DBus.Properties` Get/Set/GetAll over an in-memory `Adapter1` (Address, Alias="fake-pc", Powered=false, Discoverable=false, Pairable=false, DiscoverableTimeout=180)
  - `/org/bluez` with `ProfileManager1.RegisterProfile`/`UnregisterProfile` and `AgentManager1.RegisterAgent`/`RequestDefaultAgent`/`UnregisterAgent`, recording calls
  - `/org/bluez/hci0` with `GattManager1` and `LEAdvertisingManager1` (used in Task 9)

  Helpers: `Task<Socket> ConnectSppAsync(string devicePath)` makes a `socketpair(AF_UNIX, SOCK_STREAM)` via P/Invoke, calls `NewConnection(device, fd, {})` on the registered profile path with `WriteHandle`, and returns the other end as a `Socket`. Also `Task ReleaseProfileAsync()`.
- Tests run only when `OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("ESCPOS_BT_FAKE_BLUEZ") == "1"`. Otherwise `Assert.Skip("Linux fake-BlueZ tests need Linux + dbus-run-session; see docs/Building-and-Testing.md")`. Servers use `DBusAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")`.

- [ ] **Step 1: Write the failing integration tests**

```csharp
[Fact] public async Task Spp_RegistersProfileWithSppUuidAndName()          // fake recorded RegisterProfile(path, "00001101-…", Name="CrossEscPos", Role="server")
[Fact] public async Task Spp_PreparesAdapter_AndRestoresOnStop()           // Powered/Discoverable/Pairable true while running; back to false/false/false, timeout 180 after Stop
[Fact] public async Task Spp_ConnectionPrintsAndAnswersStatus()            // ConnectSppAsync, write "Hi\n" + DLE EOT 1 -> 1 byte read back; printer receipt contains "Hi"
[Fact] public async Task Spp_ProfileRelease_ReportsStopped()               // fake.ReleaseProfileAsync -> IsRunning false, StatusText mentions "released"
[Fact] public async Task Spp_NoBlueZOnBus_ReportsNotRunning()              // no FakeBlueZ -> StartAsync returns NoAdapter with "bluetoothd" in the message
```

- [ ] **Step 2: Build** both TFMs on Windows (`dotnet build tests/CrossEscPos.Transports.Bluetooth.Tests`). Expected: compiles; the Linux tests show SKIPPED on Windows. Note in the commit message that the Linux run happens in CI (Task 10) or on a Linux machine.
- [ ] **Step 3: Implement** `BlueZBus`, `UnixFdStream`, `BlueZSppServer`, and `FakeBlueZ`.
- [ ] **Step 4: Verify** on Windows: build is clean, all non-Linux tests pass, and the Linux tests are reported SKIPPED (not failed).
- [ ] **Step 5: Commit.** `git commit -m "feat(bluetooth): Linux SPP server via BlueZ Profile1, with fake-BlueZ integration tests"`

### Task 9: Linux BLE GATT application, advertisement, pairing agent

**Files:**
- Create: `src/CrossEscPos.Transports.Bluetooth/Linux/BlueZBleServer.cs`, `Linux/BlueZAgent.cs`
- Modify: `BluetoothPrinter.cs`, `Linux/BlueZSppServer.cs` (use `BlueZAgent`), `tests/.../Linux/FakeBlueZ.cs`, `Linux/BlueZIntegrationTests.cs`

**Interfaces:**
- `internal sealed class BlueZBleServer : IBluetoothPrinterServer, IBleNotifier`. It exports, with `HandlesChildPaths=true`, root `/com/crossescpos/gatt`, which implements `ObjectManager.GetManagedObjects` (`a{oa{sa{sv}}}`), and the children:
  - `serviceN` with `org.bluez.GattService1` {UUID s, Primary b true}
  - `serviceN/charM` with `org.bluez.GattCharacteristic1` {UUID s, Service o, Flags as}. Flags: write `["write","write-without-response"]`; notify `["notify"]`; combined E7810A71 `["read","write","write-without-response","notify"]`.
  - Methods: `ReadValue(a{sv})` → empty `ay`; `WriteValue(ay value, a{sv} options)` → `engine.OnWrite(options["device"] as object path string, profile, value, options["mtu"] as ushort or 23)`; `StartNotify()` / `StopNotify()` → subscribe/unsubscribe. BlueZ doesn't say which device called StartNotify, so use client id `"*"` for subscriptions and notify all subscribers. Writes still use per-device ids, so the engine maps replies for any device to `"*"` when no per-device subscription exists. Document this one-phone-at-a-time limitation of BlueZ notifications in `docs/Bluetooth.md`. Properties `Get`/`GetAll` are served too.
  - `NotifyAsync` emits the signal `org.freedesktop.DBus.Properties.PropertiesChanged("org.bluez.GattCharacteristic1", {"Value": ay chunk}, [])` from the notify characteristic's path (`WriteSignalHeader` + `TrySendMessage`).
  - Advertisement `/com/crossescpos/adv0` implements `org.bluez.LEAdvertisement1` {Type s "peripheral", ServiceUUIDs as ["000018f0-0000-1000-8000-00805f9b34fb"], LocalName s options.AdvertisedName, Discoverable b true} and `Release()`.
  - `StartAsync`: adapter prep (as SPP), optional agent, `GattManager1.RegisterApplication(o /com/crossescpos/gatt, {})`, then `LEAdvertisingManager1.RegisterAdvertisement(o /com/crossescpos/adv0, {})`. `IsRunning` is true only after both return. `org.bluez.Error.NotSupported` from advertising → `NotSupportedByAdapter`.
  - Device disconnects: subscribe to `PropertiesChanged` on `org.bluez.Device1` (`Connected=false`) via `AddMatchAsync` → `OnDisconnected(devicePath)`.
  - `StatusText` while running: `"Advertising BLE printer service 18F0 as \"{AdvertisedName}\" ({n} connected)."`
- `internal sealed class BlueZAgent : IPathMethodHandler, IAsyncDisposable` at `/com/crossescpos/agent`, capability `"NoInputNoOutput"`: `RequestConfirmation`/`RequestAuthorization`/`AuthorizeService` reply OK; `RequestPinCode` → `"0000"`; `RequestPasskey` → `0u`; `Display*`/`Cancel`/`Release` → OK. `StartAsync(bool makeDefault)` calls `AgentManager1.RegisterAgent`, and `RequestDefaultAgent` only when `makeDefault` (= `options.AutoAcceptPairing`). Both servers create the agent only when `AutoAcceptPairing` is true.
- FakeBlueZ additions: it records `RegisterApplication`/`RegisterAdvertisement` and, after registration, calls the app's `GetManagedObjects` and the advertisement's properties. Helpers: `Task WriteAsync(string charPath, byte[] value, string device, ushort mtu)`, `Task StartNotifyAsync(string charPath)`, and an `AddMatchAsync` on `PropertiesChanged` collecting notified `Value`s.

- [ ] **Step 1: Write the failing integration tests**

```csharp
[Fact] public async Task Ble_ExportsThreeServicesWithSpecUuidsAndFlags()   // GetManagedObjects via fake: 3 GattService1, UUIDs & flags as specified
[Fact] public async Task Ble_AdvertisesOnly18F0_WithTrimmedName()           // adv ServiceUUIDs == ["000018f0-…"], LocalName == options.AdvertisedName
[Fact] public async Task Ble_WritePrints_AndStatusIsNotified()               // StartNotify on 2AF0; WriteValue on 2AF1 "Hi\n"+DLE EOT 1 -> one PropertiesChanged Value of 1 byte; receipt has "Hi"
[Fact] public async Task Ble_NotificationChunksFollowMtu()                   // mtu 23, trigger 50-byte reply (GS I ? or repeated DLE EOT x25) -> chunks <= 20 bytes
[Fact] public async Task Agent_OnlyBecomesDefault_WhenAutoPairOn()           // AutoAcceptPairing false -> no RegisterAgent; true -> RegisterAgent + RequestDefaultAgent
```

- [ ] **Step 2: Build.** Expected: compiles; the Linux tests SKIPPED on Windows.
- [ ] **Step 3: Implement** `BlueZBleServer` and `BlueZAgent`, and extend `FakeBlueZ`.
- [ ] **Step 4: Verify** that the build is clean and the Windows test run is green.
- [ ] **Step 5: Commit.** `git commit -m "feat(bluetooth): Linux BLE GATT peripheral, advertisement and opt-in pairing agent"`

### Task 10: Desktop app integration, CI and release

**Files:**
- Modify: `src/CrossEscPos.App.Desktop/CrossEscPos.App.Desktop.csproj`
- Create: `src/CrossEscPos.App.Desktop/Transports/BluetoothTransportEntry.cs`
- Modify: `src/CrossEscPos.App.Desktop/DesktopPlatformServices.cs`
- Modify: `.github/workflows/ci.yml`, `.github/workflows/release.yml`

**Interfaces:**
- csproj: `<TargetFramework Condition="$(RuntimeIdentifier.StartsWith('win')) Or ('$(RuntimeIdentifier)' == '' And $([MSBuild]::IsOSPlatform('Windows')))">net10.0-windows10.0.19041.0</TargetFramework>` (otherwise the inherited `net10.0`), `<EnableWindowsTargeting>true</EnableWindowsTargeting>`, and a `ProjectReference` to `CrossEscPos.Transports.Bluetooth`.
- `public sealed class BluetoothTransportEntry : TransportEntry`. Constructor `(IBluetoothPrinterServer server, TransportField nameField, bool autoStart)`; `Name` = `server.DisplayName`; `Fields = [nameField]` labelled "Printer name". `ButtonText` reads "Stop" when active and "Start" otherwise. `ToggleAsync` starts or stops; while a start is in progress, `Status` = "Starting…". `server.StatusChanged` → `Dispatcher.UIThread.Post(() => Set(server.IsRunning, server.StatusText))`. When the name changes while running, the new name applies on the next start. `Shutdown()` → `server.StopAsync().GetAwaiter().GetResult()` with a 3 s timeout. `StartAsync`'s `BluetoothSupport.Message` is shown when not supported.
- `DesktopPlatformServices.CreateTransports` returns `[tcp, serial, spp, ble]`, built from `ESCPOS_BT_NAME`, `ESCPOS_BT_SPP=1`, `ESCPOS_BT_BLE=1`, `ESCPOS_BT_AUTO_PAIR=1`. Both servers share one `BluetoothPrinterOptions`, created at start from the name field.
- ci.yml: after `Test`, add `sudo apt-get install -y dbus` and `ESCPOS_BT_FAKE_BLUEZ=1 dbus-run-session -- dotnet test tests/CrossEscPos.Transports.Bluetooth.Tests -c Release --no-build`.
- release.yml: add `Transports.Bluetooth` to the `pack` project loop. The desktop publish step needs nothing new, because the csproj condition handles `-r win-x64` on ubuntu.

- [ ] **Step 1: Build all four ways.**

```bash
dotnet build CrossEscPos.slnx -c Release                                   # Windows host -> desktop uses Windows TFM
dotnet publish src/CrossEscPos.App.Desktop -c Release -r win-x64 --self-contained -o artifacts/pub-win
dotnet publish src/CrossEscPos.App.Desktop -c Release -r linux-x64 --self-contained -o artifacts/pub-linux
dotnet publish src/CrossEscPos.App.Desktop -c Release -r osx-arm64 --self-contained -o artifacts/pub-osx
```
Expected: all succeed with zero warnings. The `pub-win` output contains `Microsoft.Windows.SDK.NET.dll`, `pub-linux` contains `Tmds.DBus.Protocol.dll`, and `pub-osx` builds. (Delete `artifacts/` afterwards; it isn't committed.)
- [ ] **Step 2: Run the app.** Run `dotnet run --project src/CrossEscPos.App.Desktop` with the `run` skill. Click Start on both Bluetooth rows and take a screenshot of the connections panel. Both rows must show the advertising status lines. Stop both; the rows return to the stopped status. Then send `printf 'Hello\n\n\n\035V\000'` over TCP 9100 and confirm the receipt still prints (regression check).
- [ ] **Step 3: Run the full test suite.** `dotnet test CrossEscPos.slnx -c Release`. Expected: all PASS (Linux/hardware tests SKIPPED).
- [ ] **Step 4: Commit.** `git commit -m "feat(app): Bluetooth SPP and BLE entries in the desktop connections panel; CI fake-BlueZ job"`

### Task 11: Documentation and final verification

**Files:**
- Create: `docs/Bluetooth.md`
- Modify: `README.md` (Key features: Bluetooth bullet; Documentation table row; Troubleshooting entry "The phone can't see the printer"), `docs/Architecture.md` (packages table row), `docs/Packages.md`, `docs/Transports.md`, `docs/Connecting.md` (env vars `ESCPOS_BT_*`), `docs/Building-and-Testing.md` (how to run the fake-BlueZ and hardware tests), `docs/README.md`, `docs/_Sidebar.md`

**`docs/Bluetooth.md` must contain:**
1. What works where (Windows/Linux × SPP/BLE; macOS/browser not supported).
2. Windows setup (Bluetooth on; pair from the phone; accept Windows' "Add a device" prompt; BLE uses the PC name).
3. Linux setup (bluetoothd running; `bluetooth` group; `ESCPOS_BT_AUTO_PAIR=1` for headless).
4. The UUID table from Global Constraints.
5. Android Kotlin snippets: SPP (`createRfcommSocketToServiceRecord(UUID.fromString("00001101-…"))`, write the bytes, read the status byte) and BLE (`connectGatt`, `requestMtu(185)`, enable notifications on 2AF0 via CCCD `00002902-…`, write 2AF1).
6. iOS Swift CoreBluetooth snippet: `scanForPeripherals(withServices: [CBUUID(string: "18F0")])`, discover 2AF1/2AF0, `setNotifyValue(true, …)`, `writeValue(…, type: .withoutResponse)` in chunks of `maximumWriteValueLength(for:)`.
7. Note that printer libraries filtering paired devices by printer class (e.g. DantSu) won't list the PC; connect by address instead.
8. Linux notification limitation (one subscribed phone at a time receives replies).
9. **Phone test script**, with exact hex to send and the expected result for each step: Android "Serial Bluetooth Terminal" (SPP) and nRF Connect (BLE); iPhone nRF Connect or LightBlue (BLE). Steps: send `48 65 6C 6C 6F 0A 0A 0A 1D 56 00` → a "Hello" receipt with a cut; send `10 04 01` → one status byte back (`0x16` when online with default state); toggle "Cover open" in the Printer state panel, resend → a status byte with bit 2 set, and a print attempt is dropped with a toast.

- [ ] **Step 1: Write the docs.**
- [ ] **Step 2: Run the final checks.** `dotnet build CrossEscPos.slnx -c Release` (0 warnings) and `dotnet test CrossEscPos.slnx -c Release` (all pass), plus `ESCPOS_BT_HARDWARE_TESTS=1 dotnet test tests/CrossEscPos.Transports.Bluetooth.Tests -f net10.0-windows10.0.19041.0` (pass on this PC).
- [ ] **Step 3: Request a whole-branch review** (superpowers:requesting-code-review), fix the confirmed findings, and re-run Step 2.
- [ ] **Step 4: Commit.** `git commit -m "docs: Bluetooth guide, phone test script, and updated package/transport docs"`
- [ ] **Step 5: Final report to the user,** split into "verified on this PC", "verified only by build/skip (needs Linux: CI or a Linux box)", and "needs your phone", plus any problems found but not fixed.
