---
version: 1
slug: "src-crossescpos-app-views-mainview-axaml"
primary_target: "src/CrossEscPos.App/Views/MainView.axaml"
related_targets: ["src/CrossEscPos.App/Views/MonitorView.axaml","src/CrossEscPos.Controls/ReceiptView.axaml","src/CrossEscPos.Controls/PrinterStatePanel.axaml"]
---

Mode: operate

## Direction contract

THESIS: CrossEscPos is an arrivals hall for print jobs. Receipts are arrivals and the paper is the only bright thing on screen. It refuses the category default: a grey three-column form wall with Start buttons, a box of toggles and a stack of images.

OWN-WORLD: Concourse black (#141416 ground, #101012 panels, #1E1E21 raised) with signage amber (#F2B33D) as the one accent. Amber appears only as a 1px selection line and as the "new arrival" lamp. Status green, red and amber appear only on state. Public Sans for UI text; JetBrains Mono (the receipt face) for every figure, time and size, so digits stack like a board. Outline icons at 1.5px stroke. 6px radius on cards, pill-shaped controls, no shadows on resting surfaces.

STORY: A developer or QA tester sees at a glance which connections are live and whether the printer is ready. They start Bluetooth with one click, print from a phone, and watch the receipt land at full pixel truth, marked new until they look. They change the printer's state from the Printer panel and see the result in the status bar.

FIRST VIEWPORT: A 56px icon rail with the brand mark and three views: Connections, Printer and Test print. Monitor sits at the bottom. A 300px side panel shows the chosen view; Connections is the default and lists Bluetooth first. The canvas has a title row ("Receipts", a count, Clear and Export) and then the receipt paper centred at pixel-exact scale. A 248px arrivals board on the right lists one row per cut (#, time, length in mm, a NEW lamp). A 28px status strip runs along the bottom with printer readiness. The primary action is the Start control on the first Bluetooth row.

FORM: Railway arrivals board, candidate 6 of 7 on the grounded list (kitchen ticket rail, till roll, darkroom contact sheet, galley proofs, sorting office, arrivals board, box-office ticket roll), fused with the taste skill's data-terminal app style. Raises: from the gate board, a new receipt stays lit until it is noticed; from the bitmap specimen, the pixel scale is honest and shown in its readout. seed:8bec883a8a326aac

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
