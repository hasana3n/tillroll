---
name: Tillroll
description: An arrivals hall for print jobs. Concourse black, signage amber, and paper as the only bright thing on screen.
colors:
  ground: "#141416"
  panel: "#101012"
  raised: "#1E1E21"
  hover: "#26262A"
  pressed: "#2E2E33"
  hairline: "#232327"
  control-border: "#3A3A3E"
  control-border-hover: "#55555B"
  idle-dot: "#5A5A60"
  ink: "#F4F4F5"
  ink-muted: "#A1A1AA"
  ink-faint: "#8A8C92"
  signage-amber: "#F2B33D"
  positive: "#44A282"
  negative: "#E5566F"
  warning: "#D9A441"
  paper: "#FFFFFF"
typography:
  title:
    fontFamily: "Public Sans"
    fontSize: "20px"
    fontWeight: 600
  section:
    fontFamily: "Public Sans"
    fontSize: "16px"
    fontWeight: 600
  row-title:
    fontFamily: "Public Sans"
    fontSize: "13px"
    fontWeight: 600
  body:
    fontFamily: "Public Sans"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: "19px"
  small:
    fontFamily: "Public Sans"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: "17px"
  label:
    fontFamily: "Public Sans"
    fontSize: "12px"
    fontWeight: 500
  figure:
    fontFamily: "JetBrains Mono"
    fontSize: "12px"
    fontWeight: 400
  lamp:
    fontFamily: "JetBrains Mono"
    fontSize: "11px"
    fontWeight: 700
rounded:
  link: "4px"
  card: "6px"
  control: "6px"
  square-button: "8px"
  overlay: "8px"
  pill: "999px"
spacing:
  xxs: "4px"
  xs: "6px"
  sm: "8px"
  md: "10px"
  lg: "14px"
  xl: "16px"
  panel: "20px"
  canvas: "24px"
components:
  button-secondary:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.pill}"
    padding: "0 14px"
    height: "32px"
  button-secondary-hover:
    backgroundColor: "{colors.hover}"
  button-secondary-pressed:
    backgroundColor: "{colors.pressed}"
  button-primary:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.panel}"
    rounded: "{rounded.pill}"
    padding: "0 14px"
    height: "32px"
  button-primary-hover:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.panel}"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    rounded: "{rounded.pill}"
    padding: "0 14px"
    height: "32px"
  button-icon:
    backgroundColor: "transparent"
    textColor: "{colors.ink-muted}"
    rounded: "{rounded.square-button}"
    size: "32px"
  rail-destination:
    backgroundColor: "transparent"
    textColor: "{colors.ink-muted}"
    rounded: "{rounded.square-button}"
    size: "40px"
  rail-destination-active:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.ink}"
  text-input:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "6px 10px"
    height: "32px"
  card:
    backgroundColor: "{colors.panel}"
    rounded: "{rounded.card}"
    padding: "14px"
  board-row:
    backgroundColor: "transparent"
    textColor: "{colors.ink-muted}"
    typography: "{typography.figure}"
    rounded: "{rounded.card}"
    padding: "0 10px"
    height: "34px"
  board-row-selected:
    backgroundColor: "{colors.raised}"
  arrival-lamp:
    textColor: "{colors.signage-amber}"
    typography: "{typography.lamp}"
  status-strip:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.ink-muted}"
    typography: "{typography.small}"
    height: "28px"
  announcement:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.ink}"
    rounded: "{rounded.pill}"
    padding: "2px 12px"
---

# Design System: Tillroll

## Overview

**Creative North Star: "The Arrivals Hall"**

Tillroll is an arrivals hall for print jobs. The interface is a dark concourse, and receipts are the arrivals. The white paper is the only bright thing on screen, and everything around it steps back so the print reads at full pixel truth. Signage amber is the one accent. It works like a station's departure board: it marks where you are and what has just landed, and does nothing else. Green, red and amber also appear, but only to report state.

The density is a data terminal's: 12–13px Public Sans for the interface and JetBrains Mono for every figure, time and size, so the numbers stack in columns like a board. Surfaces are flat, toned in near-black steps and divided by 1px hairlines rather than boxes or shadows. Controls are pills. The one primary action in any group is inverted (light on dark), never painted amber. One dark world runs unchanged on Windows, Linux, macOS and the browser. Both faces are embedded so every platform draws the same letters.

Motion is only feedback. Presses scale, colours ease, a disclosure chevron turns, and an announcement fades into the status strip. Screens, panels and receipts never animate in.

**Key Characteristics:**
- Concourse black in four tonal steps; the paper is the only bright surface.
- Signage amber is the only accent: selection lines, focus, caret, and the NEW lamp.
- Every figure, time and size is set in JetBrains Mono, so columns align like an arrivals board.
- Flat at rest, with 1px hairlines. Depth comes from tone, not shadow.
- Pill buttons. The primary action is inverted, not accented.
- Lucide outline icons with a 1.5px round stroke, coloured by the surrounding text.

## Colors

The palette is a neutral, slightly cool near-black ramp with one warm signal colour and three state colours.

### Primary
- **Signage Amber** (signage-amber): the arrivals board's lamp colour. It is used only for the 1px selection line (rail destination, board row, segmented control), the NEW arrival lamp, keyboard focus outlines, the text caret and text selection (at 40–50% alpha). It never fills a button and never decorates.

### Neutral
- **Concourse Black** (ground): the canvas behind the receipts and the main window ground.
- **Platform Black** (panel): one step darker than the ground. Used by the rail, side-panel cards, inputs, the status strip and the segmented-control track.
- **Raised Slate** (raised): secondary buttons, the active rail destination, selected and hovered board rows, dropdown and menu surfaces, and the announcement pill.
- **Hover Slate** (hover) and **Pressed Slate** (pressed): hover and press fills for buttons and menu items. Hover Slate is also the face of the checked segment and the tooltip background.
- **Hairline** (hairline): every 1px divider (rail edge, side-panel edge, connection rows, board header, status strip) and every card border.
- **Control Border** (control-border) and **Control Border Hover** (control-border-hover): outlines for ghost buttons, inputs, dropdowns and the scrollbar thumb, plus their hover states.
- **Idle Dot** (idle-dot): the status dot of a connection that is off, and the off-state switch stroke.
- **Ink** (ink): primary text, receipt captions' cut number, and the fill of the inverted primary button.
- **Ink Muted** (ink-muted): secondary text, descriptions, labels, idle icons and board figures.
- **Ink Faint** (ink-faint): placeholders and disabled button text.
- **Paper** (paper): the receipt itself, and the primary button's hover fill.

### State
- **Positive Green** (positive): live connection dots, "Ready" health, and switches that are on. A switch reports state, so "on" is green, never amber.
- **Negative Red** (negative): a blocked printer's health dot and the Monitor's not-ready status.
- **Warning Amber** (warning): health warnings (near end of paper) and the scale readout when receipts can't be drawn pixel-exact. It is a duller, darker amber than Signage Amber and appears only as a state colour.

### Named Rules
**The One Lamp Rule.** Signage amber marks only where you are and what is new: 1px selection lines, focus, caret, and the NEW lamp. If amber appears anywhere else, it is a defect.

**The State Speaks Rule.** Green, red and warning amber appear only on elements that report a state (dots, health, switches, the scale readout). Never use them for decoration or emphasis.

**The Paper Is The Light Rule.** No surface other than the receipt paper (and the momentary primary-button hover) is lighter than Raised Slate. The paper stays the brightest thing on screen.

## Typography

**Interface Font:** Public Sans (embedded; Regular, Medium, SemiBold, Bold)
**Figure Font:** JetBrains Mono (embedded; Regular). It is also the face the receipts print in.

**Character:** Public Sans is a plain, civic sans for instructions and labels. JetBrains Mono carries every number, so times, cut numbers, sizes and scales line up as a timetable.

### Hierarchy
- **Title** (SemiBold, 20px): the canvas heading ("Receipts"). One per screen.
- **Section** (SemiBold, 16px): side-panel and Monitor headings, and the empty-state heading.
- **Row title** (SemiBold, 13px): connection names, printer-state rows, "Arrivals", health text in cards.
- **Body** (Regular, 13px, 19px line height): running text. Muted body (ink-muted) is used for panel introductions.
- **Small** (Regular, 12px, 17px line height): descriptions, notes, status lines, board column headers and status-strip text.
- **Label** (Medium, 12px, ink-muted, sentence case): form field labels, control-group headings ("Power and cover", "Paper") and the disclosure toggle.
- **Figure** (JetBrains Mono Regular, 12px): counts, clock, arrival times, lengths, paper size, scale readout, receipt captions and the activity log.
- **Lamp** (JetBrains Mono Bold, 11px, signage amber): the literal "NEW" on an unseen arrival. This is the only bold mono and the only uppercase in the interface.

### Named Rules
**The Timetable Rule.** If it's a number a person might compare (time, count, size, dots, scale), set it in JetBrains Mono. Sentences around it stay in Public Sans.

**The Sentence Case Rule.** Every label, heading, column header and group heading is in sentence case with no added letter spacing. Tracked uppercase labels are banned. The NEW lamp is a status signal, not a label, and stays the only uppercase.

## Layout

The window is a fixed frame around a flexible canvas. A 56px icon rail on the left holds the brand mark at the top, three panel destinations (Connections, Printer, Test print) and the Monitor at the bottom. Next to it is a 300px side panel showing the chosen destination, with a 1px hairline on its right edge. The canvas fills the rest. It has a 60px title row (title, mono count, ghost actions on the right) above a body split into the receipt feed and a 248px arrivals board. A 28px status strip runs the full width along the bottom.

The receipts are centred in the feed at a whole-number pixel scale, with each printer dot a whole number of screen pixels and no smoothing. When the canvas is too narrow to keep them pixel-exact beside the arrivals board, the board steps aside and its column collapses so the feed gets the width. When even the full canvas can't keep them pixel-exact, the board stays, and the status-strip scale readout turns warning amber.

Spacing runs on a small, mostly even rhythm: 4–6px inside clusters (icon to text, title to description), 8–10px between controls in a group, 14–16px inside cards and between list rows, and 20px panel insets (24px on the canvas). Lists are divided by hairlines, not boxed. Connection rows sit inside a list with a hairline above and below each row.

### Named Rules
**The Pixel Truth Rule.** Receipts are never scaled by a fraction. Layout gives way first: the arrivals board hides before the paper blurs, and any non-exact scale is reported in the status strip.

**The Strip Speaks Rule.** Announcements (buzzer, cash drawer, dropped print) appear in the centre of the status strip, never over the paper.

## Elevation & Depth

The system is flat. Depth comes from tone: panel (darkest), ground, raised, hover and pressed, separated by 1px hairlines. Resting surfaces have no shadows. The one lifted surface is the in-browser Monitor drawer. It opens, without animation, over a 60% black scrim and casts a single leftward shadow, because it sits above the whole window.

### Shadow Vocabulary
- **Drawer** (`-8px 0 32px 0 rgba(0,0,0,0.5)`): only on the browser Monitor drawer, together with a `rgba(0,0,0,0.6)` scrim.

### Named Rules
**The Flat Concourse Rule.** No shadows on cards, buttons, rows or panels. Use a darker or lighter tonal step, or a 1px hairline, to separate.

## Shapes

Corners are gently rounded and grow with the control's role. Text links have 4px corners. Cards, inputs, dropdowns, board rows, tooltips and Monitor job rows use 6px (control). Square icon and rail buttons and overlays use 8px. The segmented control nests a 5px face inside a 7px track. Every text button is a full pill (999px), and so is the announcement. Borders are always 1px. Status dots are 7px circles (8px when they lead a health card). Icons are Lucide outlines on a 24-unit grid, drawn by IconView with a 1.5px round-capped, round-joined stroke that stays 1.5px at any size (16px by default, 20px on the rail, 14–15px inside buttons).

The brand logo is the single full-colour mark in the interface. It sits at 28px at the top of the rail with its own colours, as a brand commitment.

## Components

### Buttons
Quiet, tactile pills that press in.
- **Shape:** full pill (999px), 32px minimum height, 14px horizontal padding, Medium 13px.
- **Secondary (default):** Raised Slate fill, Ink text.
- **Primary:** inverted. Ink fill with Platform Black SemiBold text. It turns Paper on hover and #DADADE when pressed. Use one per group, for the action that moves the job forward (Start, Render, Connect). Never amber.
- **Ghost:** transparent with a 1px Control Border outline, for actions beside or instead of a primary (Stop, Clear, Export, Load sample).
- **Hover / Press:** fills ease over 120ms. A press scales to 0.97 over 120ms with cubic-bezier(0.23, 1, 0.32, 1). Disabled buttons drop to 55% opacity with Ink Faint text.
- **Icon:** a 32px square with 8px corners, transparent, Ink Muted until hovered.
- **Link:** transparent, 24px tall, 4px corners, Regular 12px. Used for the status-strip health button and "Mark all seen".

### Navigation (rail)
- **Destinations:** 40px squares with 8px corners holding 20px outline icons in Ink Muted. Each has a tooltip to its right.
- **Active:** Raised Slate fill and Ink icon, plus a 1px × 22px signage-amber line on the rail's inner edge.

### Inputs / Fields
- **Style:** Platform Black fill, 1px Control Border, 6px corners, 32px minimum height, 10px × 6px padding. Dropdowns use the same shell and open onto a Raised Slate list.
- **Focus:** the border turns signage amber, the caret is amber, and selection is amber at 40% alpha.
- **Hover / Disabled:** the border lightens to Control Border Hover. Disabled fields keep the fill, take a Hairline border and Ink Muted text. Connection fields lock while the connection runs, with a note saying why.
- **Mono input:** the Test print editor is set in 12px JetBrains Mono.

### Cards / Containers
- **Corner Style:** 6px.
- **Background:** Platform Black on the ground, with a 1px Hairline border.
- **Shadow Strategy:** none (see Flat Concourse Rule).
- **Internal Padding:** 14px (10–12px for compact health cards).

### Segmented control
A Platform Black track with a 1px Hairline border, 7px corners and 2px padding. It holds equal-width 28px segments in Medium 12px Ink Muted. The checked segment takes a Hover Slate face, Ink text and the 1px amber selection line under its label. Keyboard focus outlines the face in amber.

### Switches
Fluent switches with their text removed. Off is an Idle Dot stroke with an Ink Muted knob. On is a Positive Green fill with a Platform Black knob.

### Arrivals board (signature)
A 248px column headed by "Arrivals" and a live mono clock. Under a Small sentence-case header row (Cut, Arrived, Length) and a hairline, each cut is a 34px row in 12px mono Ink Muted, with the cut number in Ink. A hovered or selected row takes a Raised Slate fill with 6px corners, and a selected row also gets the 1px amber line under its number. An unseen arrival shows the amber NEW lamp until it has been looked at. "Mark all seen" appears as a link only while something is new.

### Receipt slip (signature)
The paper is drawn pixel-exact on a Paper fill with no border, radius or shadow. Below it, a caption row in mono 12px shows the NEW lamp, the cut title in Ink, the arrival time and the length, with a 32px icon button to save that receipt as PNG. Slips are spaced 28px apart and centred in the feed.

### Status strip
A 28px Platform Black strip with a hairline on top. On the left is the health link: a 7px state dot and the health text. In the centre is the announcement, a Raised Slate pill with Medium 12px text that fades in and rises 4px over 180ms, then fades out. On the right are the live-connection count in Small text, and the paper size and scale in Small mono. The scale turns warning amber when it isn't exact.

### Connection row
A hairline-divided row with a muted transport icon, the name (Row title), a Small description and the start/stop pill on the right. When the row has a status, a state dot and a status line follow. A "Settings" disclosure (Label weight, with a chevron that rotates 90° over 160ms) reveals the fields.

## Do's and Don'ts

### Do:
- **Do** keep signage amber to 1px selection lines, focus, caret, selection highlight and the NEW lamp.
- **Do** make the one forward action an inverted Ink pill, with every other action as a secondary or ghost pill.
- **Do** set every time, count, size and scale in JetBrains Mono 12px.
- **Do** separate surfaces with a tonal step or a 1px Hairline, and leave resting surfaces flat.
- **Do** let the arrivals board step aside before a receipt loses pixel-exact scale, and report any non-exact scale in the status strip.
- **Do** put announcements in the status strip.
- **Do** draw icons as Lucide outlines at 1.5px stroke, coloured by the surrounding text.
- **Do** keep the brand logo in its own colours at the top of the rail.

### Don't:
- **Don't** fill buttons, backgrounds or icons with signage amber.
- **Don't** use green, red or warning amber for anything that isn't reporting a state.
- **Don't** add shadows to cards, rows, buttons or panels. Only the in-browser Monitor drawer is lifted.
- **Don't** use tracked uppercase labels, eyebrows or kickers. Labels and headings are sentence case.
- **Don't** animate screens, panels or receipts in. Motion is press, hover, disclosure and announcement feedback only, at 120–180ms.
- **Don't** overlay announcements on the paper.
- **Don't** scale a receipt by a fractional factor or smooth its pixels.
- **Don't** use glyph or filled icons, or a system display face. Both faces are embedded.
