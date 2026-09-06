# Settings page

*(Part of the [User Manual](USERMANUAL.md).)*

**Settings** holds OpenTabletArtist's own preferences, divided into tabs: **Presets**, **Hotkeys**, **Appearance** (theme), **System** (Startup + Shortcut + Driver Cleanup, Windows-only), and **Developer** *(debugging tools)*. (**Per-App Presets** is hidden while the feature is disabled.)

## Presets

Save, load, rename, and delete whole-configuration snapshots (all tablets), and change the active configuration by preset hotkey, the tray's Switch Display, or a tablet's display picker. **See [Presets](PRESETS.md).**

## Hotkeys

Global keyboard shortcuts that work even when OpenTabletArtist isn't focused. Assign a combination (a modifier — Ctrl / Alt / Shift / Win — plus a letter, digit, or F-key) with the on-screen picker, and it takes effect system-wide.

- **Cycle mapped monitor** — moves the active tablet's area to the next monitor (wrapping around). Shows a toast with the new monitor; no-ops (with a toast) if you only have one display or no tablet is active.
- **Preset switching** — assign a hotkey to a preset to switch to it instantly. The switch is a live-only override (your saved default isn't overwritten); a "Preset override" chip shows while one is active.

> **Per-App Presets** (automatic preset switching by foreground app) is temporarily hidden and disabled while its switching model is being reconsidered. The feature and any saved app→preset mappings are retained and may return in a later version.

## Appearance

Everything about how the app looks is here, split into three **subtabs** down the left: **Theme**, **Backdrop** and **Colours**. The backdrop's glows used to be a page away, under Developer → Gradients, which meant tuning a backdrop took two tabs and a lot of walking.

### Theme

- **Theme** — a selector with **System** (follows your Windows light/dark setting), **Light**, **Dark**, **Sakura** (a pink skin with a soft gradient backdrop and frosted-glass panels — the default), **Dark Sakura** (the same skin over a dark scheme), and **Custom** (a translucent skin you tune yourself). Applied immediately and remembered across restarts.
- **Falling petals** *(Sakura, Dark Sakura, and Custom)* — toggles the drifting cherry-blossom animation, with an opacity slider to tune how prominent the petals are (defaults to a soft 25%).

### Backdrop

What fills the window behind the panels. Light and Dark paint nothing there, so the subtab says so and stops.

- **Solid colour / Generated gradient** *(Sakura and Dark Sakura)* — a flat colour (`#FDE4E8` on Sakura, `#1E0A14` on Dark Sakura), or a base colour with soft glows over it. Applied live and remembered across restarts.
- **Base colour** *(generated gradient only)* — a colour picker (plus hex box) for the flat tint the glows sit on. Each skin keeps its own, as it does its glows.
- **Preview** — the whole backdrop as a 1280×800 window would draw it, so a base colour can be judged against the glows on top of it without leaving the page.
- **Glows** — the list of glows for the current skin, each with a chip painted from the glow itself. Picking one opens its controls on the right. A glow is either **radial** (a soft blob placed along its edge, with **Center X** and **Width**) or **linear** (a wash spanning the whole edge, with a **Falloff** for the shape of its fade); **Edge** anchors it to the bottom, top, left or right of the window, and **Reach** is how far it comes in from there. Radial glows stay on the bottom and top edges. **Add glow** appends one; the list's **⋯** holds **Reset to defaults** and **Copy settings JSON**; a glow's own **⋯** duplicates or removes it.
- **Custom backdrop** *(Custom only)* — a base colour, and optionally a background image to fill the window instead, with an opacity slider that fades it over the base.

### Colours

*(Sakura, Dark Sakura, and Custom; the subtab says so for the others.)* Each control is a swatch with the current hex beside it; clicking one opens a picker with a colour wheel, a curated palette, and a hex box. Everything here applies live and is stored **per skin**, so tuning Sakura never changes how Dark Sakura or Custom look.

- **Highlight colour** — the accent used for the current page and tab, selected options, links, and primary buttons.
- **Card colour** — the tint of the frosted panels.
- **Card opacity** — how translucent those panels are; lower lets more of the backdrop show through.
- **Reset to defaults** — restores this skin's colours, opacity, backdrop base and petals. It only affects the skin you're on.

## System

The **System** tab groups the Windows-only housekeeping controls: Startup, Shortcut, and Driver Cleanup.

### Startup

A single toggle — **Start OpenTabletArtist when Windows starts** — that launches the app minimized to the tray at sign-in, so hotkeys are ready without opening it yourself (per-user Run key; Windows only).

### Shortcut

A single checkbox — **Create a Start-menu shortcut for this app** — that mirrors whether a per-user Start-menu shortcut exists: check it to create the shortcut, uncheck it to remove it. A dev build run straight from its build folder isn't a registered app; the shortcut registers it under its name so desktop-automation / screenshot tooling can find it (Windows only).

### Driver Cleanup

*(Windows-only.)* Finds and removes conflicting manufacturer tablet drivers.

- **Conflicting drivers detected** — When the daemon flags a manufacturer driver (parsed from its detection warnings), each is shown as its own card with the driver name, its impact ("Blocks OpenTabletDriver from detecting tablets" / "Can cause flaky tablet support"), the offending processes, the full (selectable) daemon message, and an **Open OpenTabletDriver FAQ** link. (OpenTabletArtist's own process is filtered out so it isn't mistaken for a conflict.)
- **TabletDriverCleanup** — Manages the [TabletDriverCleanup](https://github.com/OpenTabletDriver/TabletDriverCleanup) tool by the OTD team that removes leftover bits from previous manufacturer tablet drivers (Wacom, Huion, XP-Pen, etc.). Install the tool first via **Install** (downloads the latest release to `%LocalAppData%\TabletDriverCleanup`, no admin required); then **Run** launches it with a UAC prompt and a visible terminal so the cleanup output is readable. **Browse** opens the install folder; **Uninstall** removes it.

## Developer

Testing aids not needed for normal use — force/introduce *Needs attention* warnings, reveal the hidden tablet tabs, pin the window to an exact size, and screenshot every page. **See [Developer tools](DEVELOPER.md).**
