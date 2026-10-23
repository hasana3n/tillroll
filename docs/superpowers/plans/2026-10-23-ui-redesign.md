# CrossEscPos UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the grey three-column form layout with one modern, simpler design that looks the same on Windows, Linux, macOS and the browser. Connecting a phone and watching receipts arrive become the two fastest paths.

**Architecture:**
- An own design system (tokens, control styles and icons) lives in `src/CrossEscPos.App/Theme/` on top of FluentTheme, with Public Sans and JetBrains Mono embedded as Avalonia resources.
- `MainView` becomes:
  - an icon rail that switches a side panel between Connections, Printer and Test print;
  - a receipts canvas (pixel-exact paper feed plus an arrivals board);
  - a printer status strip along the bottom.
- View-model additions are small and tested: panel selection, printer readiness, arrival metadata, a "new until seen" flag, transport descriptions and icons. All existing bindings and behaviour stay.

**Tech Stack:** Avalonia 12.0.4 (FluentTheme, compiled bindings), CommunityToolkit.Mvvm 8.4, .NET 10, xUnit + Avalonia.Headless.

**Spec:**
- Direction contract: `.impeccable/surfaces/src-crossescpos-app-views-mainview-axaml.md`
- Product context: `PRODUCT.md`
- App style sheet (taste skill, data-terminal): `.taste-skill/app/direction.json`

## Global Constraints

- Ground `#141416`, panel `#101012`, raised `#1E1E21`, hover `#26262A`, border `#232327`, control border `#3A3A3E`.
- Text `#F4F4F5`, muted `#A1A1AA`, faint `#8A8C92` (every text tier at least 4.5:1 on its surface).
- Accent (signage amber) `#F2B33D`. It is used only for:
  - the 1px selection line;
  - the "new" lamp;
  - the focus ring.
- State colours:
  - positive `#44A282` (pill `#1B2828` / `#5FC39C`);
  - negative `#E5566F` (pill `#2C1A20`);
  - warning `#D9A441` (pill `#2A2416`).
- Typography:
  - UI font: Public Sans (Regular, Medium, SemiBold, Bold).
  - Figures, times, sizes, IDs and ESC/POS input use JetBrains Mono, which is already the receipt font.
  - Sizes 12 / 13 / 16 / 20; body 13. No text under 11px. Sentence case and no tracked capitals.
- Shape and depth:
  - Radius 6 for cards, 999 (pill) for buttons and inputs, 4 for chips.
  - No shadows on resting surfaces; only the toast and flyouts cast a shadow.
  - No gradients, glass or emoji.
  - Icons are Lucide outline paths with a 1.5 stroke.
- Motion (Emil):
  - press scale 0.97 over 120ms;
  - hover colour change over 120ms;
  - toast enters 200ms on ease-out `0.23,1,0.32,1` and leaves in 150ms;
  - no entrance animations on lists.
- Keep every binding of the existing view models working: `MainViewModel`, `TransportEntry`, `TransportField`, `ReceiptViewModel`, `PrinterState` and `MonitorViewModel`. The existing tests must stay green; `PrinterStatePanel`'s first `ToggleSwitch` must remain Online.
- Rules from the repo: `TreatWarningsAsErrors`, explicit usings and compiled bindings (`x:DataType` on every template).

## Review Focus

1. **Narrow window.** At the minimum width (980 DIP) with an 80 mm receipt at 150% scaling, the feed must not scroll sideways. The paper shrinks to whole pixels per dot (`PixelPerfectImage.Fit`), and the arrivals board keeps 248px.
2. **Floods of receipts.** A phone sends 50 cuts quickly. The board and feed must stay responsive, auto-scroll to the newest, and keep New lamps lit until seen.
3. **Transport errors.** Long status text, for example "Bluetooth is off on this PC…", wraps inside the row and never clips.
4. **Browser head.** The same `MainView` runs in WASM. Fonts load from `avares://CrossEscPos.App`, Monitor opens as an in-page overlay, and there are no desktop-only APIs.
5. **Contradictory printer state.** With Online off and the cover open, the status strip names the first blocking cause, not "Ready".

---

### Task 1: Theme foundation (fonts, tokens, icons, control styles)

**Files:**
- Create: `src/CrossEscPos.App/Assets/Fonts/PublicSans-{Regular,Medium,SemiBold,Bold}.ttf`, `PublicSans-OFL.txt`, and `JetBrainsMono-Regular.ttf` (copied from `src/CrossEscPos.Rendering.Skia/Assets/Fonts/receipt-mono.ttf`).
- Create: `src/CrossEscPos.App/Theme/Tokens.axaml` (ResourceDictionary): colours, brushes, `Font.Ui` and `Font.Mono` FontFamily resources, and the Fluent overrides `SystemAccentColor=#F2B33D`, `ToggleSwitchFillOn=#44A282` and related keys.
- Create: `src/CrossEscPos.App/Theme/Icons.axaml` (ResourceDictionary): `StreamGeometry` keys `Icon.Plug`, `Icon.Printer`, `Icon.Code`, `Icon.Activity`, `Icon.Download`, `Icon.Trash`, `Icon.Bluetooth`, `Icon.Network`, `Icon.Cable`, `Icon.Globe`, `Icon.Refresh`, `Icon.X`, `Icon.Chevron`, written in explicit absolute commands.
- Create: `src/CrossEscPos.App/Theme/Controls.axaml` (Styles):
  - Button classes `primary`, `ghost`, `icon`, `rail`, and `chip`;
  - TextBox and ComboBox as pills;
  - a thin ScrollBar;
  - ToolTip;
  - `Path.icon`;
  - `TextBlock` classes `h1`, `h2`, `muted`, `mono`, `label`.
- Modify: `src/CrossEscPos.App/CrossEscPos.App.csproj` (add `<AvaloniaResource Include="Assets\**" />`), `src/CrossEscPos.App/App.axaml` (merge the dictionaries and include the styles after FluentTheme).

- [ ] Step 1: Copy the fonts and write the three theme files.
- [ ] Step 2: Build with `dotnet build src/CrossEscPos.App.Desktop`. Expected: 0 errors, 0 warnings.
- [ ] Step 3: Commit `feat(ui): design tokens, fonts and icons`.

### Task 2: View-model support (readiness, arrivals, panels, transport metadata)

**Files:**
- Create: `src/CrossEscPos.Controls/PrinterReadiness.cs`.
- Modify:
  - `src/CrossEscPos.Controls/ReceiptViewModel.cs`;
  - `src/CrossEscPos.Core/Emulator/ReceiptPrinter.cs` (add `public PaperConfiguration Paper => _paperConfiguration;`);
  - `src/CrossEscPos.App/Transports/TransportEntry.cs`;
  - the four transport entries (desktop and browser);
  - `src/CrossEscPos.App/ViewModels/MainViewModel.cs`;
  - `src/CrossEscPos.App/Monitor/MonitorViewModel.cs` (token colours, no glyphs in text).
- Test: `tests/CrossEscPos.Controls.Tests/ControlsTests.cs`.

**Interfaces (produces):**
- `public enum PrinterHealth { Ready, Warning, Blocked }`
- `public static (PrinterHealth Health, string Text) PrinterReadiness.Describe(PrinterState s)` uses this order:
  1. `!Online` → (Blocked, "Offline");
  2. `CoverOpen` → (Blocked, "Cover open");
  3. `Paper==Out` → (Blocked, "Out of paper");
  4. `Error==Unrecoverable` → (Blocked, "Unrecoverable error");
  5. `Error==Recoverable` → (Blocked, "Recoverable error");
  6. `Paper==NearEnd` → (Warning, "Ready · paper near end");
  7. otherwise (Ready, "Ready").
- `ReceiptViewModel` gets:
  - `DateTime ArrivedAt` (constructor time);
  - `string ArrivalTime` ("HH:mm:ss");
  - `int Number`;
  - `string LengthText` (image height / 8 dots per mm, as "114 mm");
  - `string SizeText` ("576 × 912 dots");
  - `[ObservableProperty] bool IsNew` (true at creation);
  - `void MarkSeen()`.
  Size and length update on `Refresh()`.
- `TransportEntry` gets `public virtual string Description => ""` and `public virtual TransportKind Kind => TransportKind.Network`, with `enum TransportKind { Bluetooth, Network, Cable, Browser }`.
  - Descriptions: SPP "Android phones", BLE "iPhone and iPad", TCP "Apps on this computer or your network", Serial "USB or serial cable".
  - Browser entries use `Kind=Browser` with a description passed in.
- `MainViewModel` gets:
  - `enum SidePanel { None, Connections, Printer, TestPrint }`;
  - `[ObservableProperty] SidePanel Panel = Connections`;
  - `ShowPanelCommand(SidePanel)`, which toggles back to `None` when the same panel is chosen again;
  - `bool IsConnectionsOpen`, `IsPrinterOpen`, `IsTestPrintOpen`, `IsSidePanelOpen`;
  - `PrinterHealth Health`, `string HealthText`, refreshed on `State.PropertyChanged`;
  - `int LiveCount`, `string LiveText` ("No connections running" / "1 connection running" / "N connections running");
  - `string PaperText` ("80 mm · 576 dots");
  - `int NewCount`, `string ReceiptCountText` ("3 receipts · 1 new");
  - `MarkAllSeenCommand`.
  The constructor no longer renders the default input, so the first launch shows the empty state. Toast text drops the emoji ("Buzzer", "Cash drawer opened", "Print dropped: {reason}").
- Desktop transport order becomes `spp, ble, tcp, serial`.

- [ ] Step 1: Write the failing tests:
  - `PrinterReadiness_NamesTheFirstBlockingCause` (Online=false plus CoverOpen=true gives (Blocked, "Offline"); CoverOpen only gives "Cover open"; NearEnd gives Warning; default gives Ready);
  - `ReceiptViewModel_IsNewUntilSeen` (new is true, `MarkSeen` makes it false);
  - `ReceiptViewModel_ReportsLengthInMillimetres` (an image height of h dots gives `$"{h/8.0:0} mm"`).
- [ ] Step 2: Run `dotnet test tests/CrossEscPos.Controls.Tests`. Expected: FAIL to compile, members missing.
- [ ] Step 3: Implement the interfaces above.
- [ ] Step 4: Run `dotnet test`. Expected: all suites PASS.
- [ ] Step 5: Commit `feat(app): view-model support for the redesigned UI`.

### Task 3: Main window shell (rail, side panel, canvas, status strip, toast, empty state)

**Files:**
- Modify: `src/CrossEscPos.App/Views/MainView.axaml(.cs)`, `src/CrossEscPos.App.Desktop/Views/MainWindow.axaml` (title "CrossEscPos", Background from the ground token, MinWidth 980, MinHeight 640).

**Layout:**
- `Grid` columns `56,Auto,*`, rows `*,28`.
- **Rail:**
  - brand mark `docs/logo.png` copied to `Assets/logo.png` at 28px;
  - `rail` buttons for Connections, Printer and Test print, each with `Classes.active` bound and a tooltip;
  - Monitor at the bottom.
- **Side panel:** 300px wide, visible when `IsSidePanelOpen`, with a header (h2 plus a muted subline). It holds:
  - the Connections list. Each row shows a kind icon, name, description and status dot, the Start/Stop button (`primary` while stopped, `ghost` while running), the status text (wrapped) and a "Settings" expander with the fields (enabled when not active) and a refresh icon button;
  - the `PrinterStatePanel`;
  - Test print: a mono TextBox with Render (primary), Load sample (ghost) and Clear (ghost).
- **Canvas:**
  - Title row: h1 "Receipts", `ReceiptCountText`, and on the right Clear (ghost, trash icon) and an Export flyout (ghost, download icon; "All receipts as one image…" / "Each receipt to a folder…").
  - Below it a Grid `*,248` with the `ReceiptView` feed and the arrivals `ListBox`.
- **Status strip:**
  - left: a dot coloured by `Health` and `HealthText`;
  - right: `LiveText · PaperText` in mono.
- **Empty state:** centred on the canvas when `!HasReceipts`, with h2 "Waiting for a print", one sentence, a primary "Show connections" and a ghost "Print a sample".
- **Toast:** bottom-centre above the strip, `Classes.shown` bound to `ToastVisible`, animated with opacity and translate.
- **Seen rule:** pointer-entered on the canvas runs `MarkAllSeenCommand`; selecting a board row calls `ReceiptView.ScrollTo(vm)` and `vm.MarkSeen()`.

- [ ] Step 1: Write the AXAML and code-behind.
- [ ] Step 2: Build, then launch and screenshot at 150% scaling: the empty state, after a TCP print, and with each panel open.
- [ ] Step 3: Commit `feat(ui): redesigned main window`.

### Task 4: Reusable controls (ReceiptView, PrinterStatePanel)

**Files:**
- Modify: `src/CrossEscPos.Controls/ReceiptView.axaml(.cs)`. Paper on the ground with a 24px gap between receipts, and a caption row under each receipt: mono "Cut #n · HH:mm:ss · 114 mm", an amber lamp when `IsNew`, and an export icon button. Add `public void ScrollTo(object item)`, which brings the item's container into view.
- Modify: `src/CrossEscPos.Controls/PrinterStatePanel.axaml(.cs)`. Rows with a title, a muted description and a ToggleSwitch on the right (Online first; Cover open; Cash drawer open; Feed button held). Paper is a three-segment RadioButton group (Plenty / Near end / Out) and Error a ComboBox with friendly names, both via a new `EnumEqualsConverter` in Controls. A closing note explains which status commands the panel drives.
- Test: the existing tests stay green, plus `PrinterStatePanel_PaperSegmentsSetPaperLevel`. Checking the "Out" RadioButton sets `state.Paper == PaperLevel.Out`.

- [ ] Step 1: Write the failing test.
- [ ] Step 2: Run it to confirm it fails.
- [ ] Step 3: Implement.
- [ ] Step 4: Confirm all tests pass.
- [ ] Step 5: Commit `feat(controls): redesigned receipt feed and printer panel`.

### Task 5: Monitor (window and browser overlay)

**Files:**
- Modify: `src/CrossEscPos.App/Views/MonitorView.axaml(.cs)`. Sections: "Connect" (mode, fields, refresh, connect), "Printer reports" (indicator rows with dot, label and value), "Send a test job" (a 2-column ghost button grid) and "Activity" (mono log with a Copy icon button).
- Modify: `src/CrossEscPos.App.Desktop/Views/MonitorWindow.axaml` (title "CrossEscPos Monitor", tokens). The overlay in `MainView` uses the tokens and an X icon.

- [ ] Step 1: Implement.
- [ ] Step 2: Build, open Monitor, connect to TCP 9100 and send a sample; screenshot.
- [ ] Step 3: Commit `feat(ui): redesigned monitor`.

### Task 6: Verify, review, document

- [ ] Run the full suite: `dotnet test` all PASS, and build the browser head with `dotnet build src/CrossEscPos.App.Browser`.
- [ ] Screenshot round at 150% scaling (empty state, receipts with a New lamp, each panel, Monitor, a narrow window). Fix and run one more round.
- [ ] Run the `impeccable-finish-reviewer` with the contract, screenshots and craft-floor, then apply the fixes.
- [ ] Run the `impeccable-documenter` to write DESIGN.md and `.impeccable/design.json`; update `docs/Using-the-App.md` and the screenshots.
- [ ] Commit `docs: design system and updated screenshots`.
