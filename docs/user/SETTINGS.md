# Settings page

*(Part of the [User Manual](USERMANUAL.md).)*

**Settings** holds OpenTabletArtist's own preferences, divided into tabs: **Presets**, **Hotkeys**, **Theme**, **System** (Startup + Shortcut), **Drivers** (Windows-only), and **Dev** *(debugging tools)*. (**Per-App Presets** is hidden while the feature is disabled.)

## Presets

Save, load, rename, and delete whole-configuration snapshots (all tablets), and change the active configuration by preset hotkey, the tray's Switch Display, or a tablet's display picker. **See [Presets](PRESETS.md).**

## Hotkeys

Global keyboard shortcuts that work even when OpenTabletArtist isn't focused. Assign a combination (a modifier — Ctrl / Alt / Shift / Win — plus a letter, digit, or F-key) with the on-screen picker, and it takes effect system-wide.

Everything you can bind is one list, in two groups. Each row shows what it does, its shortcut (or *Not set*), and a **⋯** menu with **Assign…** and **Clear** — Clear is greyed out when there's nothing bound to that row.

- **Tablet actions** — **Move to next display** moves the active tablet's area to the next monitor (wrapping around). Shows a toast with the new monitor; no-ops (with a toast) if you only have one display or no tablet is active.
- **Load a preset** — one row per saved preset. Pressing its shortcut switches to it instantly, as a live-only override (your saved default isn't overwritten); a "Preset override" chip shows while one is active.

Presets come from the **Presets** tab. Delete a preset there and its shortcut goes with it — and a preset removed outside the app, or while OpenTabletArtist is closed, has its shortcut dropped the next time the app reads the list, so a new preset saved under that name never inherits an old shortcut.

If you press a shortcut whose preset has since been deleted or moved, a toast says **Couldn't load preset "…"** rather than the key quietly doing nothing. The shortcut itself is left alone, since the same failure covers a preset that is still there but unreadable; it's cleared when the app next rescans the list and finds the preset really gone.

The list is rebuilt whenever you open this tab, so a preset saved on the **Presets** tab is there the moment you step across to bind it.

> **Per-App Presets** (automatic preset switching by foreground app) is temporarily hidden and disabled while its switching model is being reconsidered. The feature and any saved app→preset mappings are retained and may return in a later version.

## Theme

Everything about how the app looks is here, split into three **subtabs** down the left: **Theme**, **Backdrop** and **Colors**. The backdrop's glows used to be a page away, under Developer → Gradients, which meant tuning a backdrop took two tabs and a lot of walking.

### Theme

- **Theme** — a selector with **System** (follows your Windows light/dark setting), **Light**, **Dark**, **Sakura** (a pink skin with a soft gradient backdrop and frosted-glass panels — the default), **Dark Sakura** (the same skin over a dark scheme), and **Custom** (a translucent skin you tune yourself). Applied immediately and remembered across restarts.

#### Petals

*(Sakura, Dark Sakura, and Custom; the section is hidden on the other themes.)*

- **Falling petals** — toggles the drifting cherry-blossom animation.
- **Petal opacity** — how prominent the petals are (defaults to a soft 25%).

### Backdrop

What fills the window behind the panels. Light and Dark paint nothing there, so the subtab says so and stops.

- **Solid color / Generated gradient** *(Sakura and Dark Sakura)* — a flat color (`#FDE4E8` on Sakura, `#1E0A14` on Dark Sakura), or a base color with soft glows over it. Applied live and remembered across restarts.
- **Base color** *(generated gradient only)* — a color picker (plus hex box) for the flat tint the glows sit on. Each skin keeps its own, as it does its glows.
- **Preview** — the whole backdrop as a 1280×800 window would draw it, so a base color can be judged against the glows on top of it without leaving the page.
- **Glows** — the list of glows for the current skin, each with a chip painted from the glow itself. Picking one opens its controls on the right. A glow is either **radial** (a soft blob placed along its edge, with **Center X** and **Width**) or **linear** (a wash spanning the whole edge, with a **Falloff** for the shape of its fade); **Edge** anchors it to the bottom, top, left or right of the window, and **Reach** is how far it comes in from there. Radial glows stay on the bottom and top edges. **Add glow** appends one; the list's **⋯** holds **Reset to defaults** and **Copy settings JSON**; a glow's own **⋯** duplicates or removes it.
- **Custom backdrop** *(Custom only)* — a base color, and optionally a background image to fill the window instead, with an opacity slider that fades it over the base.

### Colors

*(Sakura, Dark Sakura, and Custom; the subtab says so for the others.)* Each control is a swatch with the current hex beside it; clicking one opens a picker with a color wheel, a curated palette, and a hex box. Everything here applies live and is stored **per skin**, so tuning Sakura never changes how Dark Sakura or Custom look.

- **Highlight color** — the accent used for the current page and tab, selected options, links, and primary buttons.
- **Card color** — the tint of the frosted panels.
- **Card opacity** — how translucent those panels are; lower lets more of the backdrop show through.
- **Reset to defaults** — restores this skin's colors, opacity, backdrop base and petals. It only affects the skin you're on.

## System

The **System** tab holds each platform’s own integration: on Windows the Startup and Shortcut controls below, on Linux a single application-menu-entry card. Driver Cleanup used to sit here as a second column; it has its own **Drivers** tab now.

### Startup

A single toggle — **Start OpenTabletArtist when Windows starts** — that launches the app minimized to the tray at sign-in, so hotkeys are ready without opening it yourself (per-user Run key; Windows only).

### Shortcut

A single checkbox — **Create a Start-menu shortcut for this app** — that mirrors whether a per-user Start-menu shortcut exists: check it to create the shortcut, uncheck it to remove it. A dev build run straight from its build folder isn't a registered app; the shortcut registers it under its name so desktop-automation / screenshot tooling can find it (Windows only).

## Drivers

*(Windows-only.)* Both drivers the app has anything to say about, in two columns: on the left what is wrong with the ones already on this machine and the tool that removes them, on the right the virtual pen driver OpenTabletDriver needs. Home’s **Conflicting tablet driver detected** and **VMulti driver not installed** cards both link straight here.

- **Conflicting drivers detected** — When the daemon flags a manufacturer driver (parsed from its detection warnings), each is shown as its own card with the driver name, its impact ("Blocks OpenTabletDriver from detecting tablets" / "Can cause flaky tablet support"), the offending processes, the full (selectable) daemon message, and an **Open OpenTabletDriver FAQ** link. (OpenTabletArtist’s own process is filtered out so it isn’t mistaken for a conflict.) With none flagged the column says so — worded as *reported*, since these come out of the daemon’s log and a daemon that isn’t running has reported nothing either way.
- **TabletDriverCleanup** — Manages the [TabletDriverCleanup](https://github.com/OpenTabletDriver/TabletDriverCleanup) tool by the OTD team that removes leftover bits from previous manufacturer tablet drivers (Wacom, Huion, XP-Pen, etc.). Install the tool first via **Install** (downloads the latest release to `%LocalAppData%\TabletDriverCleanup`, no admin required); then **Run** launches it with a UAC prompt and a visible terminal so the cleanup output is readable. **Browse** opens the install folder; **Uninstall** removes it.

### VMulti driver

VMulti is the virtual pen device the Windows Ink plugin injects pressure and tilt through. Detection runs via both Setup API and HID enumeration. Its **⋯** menu holds **Refresh status**, **Install** or **Uninstall** (only one applies at a time), and **Open install folder**. Both **Install** and **Uninstall** run in-app (one UAC prompt each, no flashing cmd window) and offer to **restart** Windows afterward. Install creates the VMulti device via `devcon`; Uninstall removes the driver and the active device *and* cleans up the leftover driverless `djpnewton\vmulti` nodes (Device Manager Code 28) that the stock removal left behind. Detection reflects a *working* driver, so any remaining driverless leftovers are reported as **Not installed**, not as installed.

## Dev

Testing aids not needed for normal use — force/introduce *Needs attention* warnings, reveal the hidden tablet tabs, pin the window to an exact size, and screenshot every page. **See [Developer tools](DEVELOPER.md).**
