# User Manual

## Prerequisites

- **.NET 10 SDK** — for building and running the app
- OpenTabletDriver is included as a submodule — no separate install needed

## Quick Start

```bash
git clone --recursive https://github.com/TheSevenPens/OpenTabletArtist.git
cd OpenTabletArtist
dotnet build OpenTabletArtist.slnx   # builds the app AND the OTD daemon from the submodule
dotnet run --project OpenTabletArtist
```

> Build the **solution** (`.slnx`), not just `OpenTabletArtist/`. The daemon (`OpenTabletDriver.Daemon.exe`) is a separate project built from the submodule; if you build only the app it won't exist and the app will sit at "Not connected".

> **Prefer the build script for a predictable build.** `scripts/build.ps1` builds the solution and
> first clears the usual blockers — it stops a running app/daemon that would lock the build outputs,
> initializes the OTD submodule if it's missing, and confirms the daemon exe was produced. Run
> `./scripts/build.ps1` (add `-Test` to run the suite, `-Configuration Release` for a release build).

On launch the app auto-starts the daemon if it isn't already running, then connects.

## Using the Interface

A **top navigation bar** runs across the top of the window with six pages — **Home**, **Tablet**, **Pen**, **Scribble**, **Settings**, and **Advanced** — and the active page is underlined in the accent colour. Most pages have their own **pivot row** of tabs just beneath the bar.

- **Tablet** and **Pen** carry the selected tablet's settings. A tablet **switcher** dropdown at the top-right picks which tablet you're editing (shown when more than one is connected). The **Tablet** page's tabs are **about · dynamics · mapping · calibration · buttons · wheels**; the **Pen** page's tabs are **movement · inputs**.
- **Settings** holds OpenTabletArtist's own preferences: **Presets**, **Hotkeys**, **Appearance** (theme), **System** (Startup + Shortcut + Driver Cleanup, Windows-only), and **Developer** *(debugging tools)*. (**Per-App Presets** is hidden while the feature is disabled.)
- **Advanced** hosts OpenTabletDriver's pages: **Daemon** (status + version), **Console** (the daemon log), **Drivers** (Windows Ink Plugin + VMulti — Windows-only), **Configs** (custom tablet compatibility), **Diagnostics**, and **Plugins**.

Every paired or connected tablet also appears on **Home** under *Your tablets*, each with a **Settings** button (opens the Tablet page for it) and a **Forget** button.

### Home

The landing page stays quiet when everything is healthy and surfaces things only when they need attention:

- **Needs attention** *(only when there's something to fix)* — the health-check list, worst problem first. Each card explains the issue and, where there's an in-app fix, has a **Fix** button that either performs the action in place or jumps straight to the page — and the specific tab — that owns it (for example, a display-mapping problem opens the tablet's **mapping** tab, not its default about tab). It covers things like a missing Windows Ink plugin or VMulti driver, a detected tablet not using a Windows Ink output mode, a tablet whose display mapping isn't a clean single monitor (off-screen or custom), a tablet with the Pen Dynamics filter turned off, a conflicting manufacturer driver, running as administrator, and using an external (not app-owned) daemon. Cards range from **Broken** (worst) through **Misconfigured** and **Recommendation** down to a purely **informational** note — e.g. a tablet you've deliberately switched to mouse-compatibility mode (Windows Ink off) shows an informational card, since nothing is wrong. Each card carries a per-severity icon and a tinted background/border so its tier reads at a glance.
- **Not connected to daemon** *(only when there's a daemon problem)* — a single card that appears when the app isn't connected to the daemon (or a start/connect attempt is failing or stalled). It has a **Fix** button (start + connect) and an **Open daemon page** button. Pressing **Fix** morphs the card into a "Connecting to daemon…" state; on success it disappears, and on failure it reverts to the problem. In the normal connected state the daemon isn't mentioned on Home at all — its full status and controls live on the **Daemon** page (Advanced → Daemon).
- **Your tablets** — every paired or currently-connected tablet as a card (status dot + connection state) with a **Settings** button (a pencil icon) that opens its Tablet page and a **Forget** button. An explicit "No tablets connected or remembered" state shows when there are none.
- **Supported tablets** — **View list** opens an in-app dialog listing every tablet OpenTabletDriver supports (read from the bundled daemon, offline). It's **searchable**, has a **brand filter**, and its columns are **sortable** (click a header; click again to reverse). A **X of 339** count updates as you filter, each row is numbered with its active area / pressure levels / button count, and a **support status** (Supported / Has Quirks / Missing Features, colour-coded) plus any **notes** (the same as opentabletdriver.net — e.g. setup caveats). Your connected tablet is highlighted (and scrolled to) if it's in the list.

### Tablet & Pen pages (per-tablet settings)

A tablet's settings live on two top-nav pages — **Tablet** and **Pen** — each with a **switcher** at the top (shown when more than one tablet is connected) and a **Refresh** in the header that re-reads settings from the daemon (useful after changes in the OTD UX). **Forget** a tablet from its card on Home.

The **Tablet** page's tabs are **about**, **dynamics**, **mapping**, **calibration**, **buttons**, and **wheels** (plus **hover**, **filters**, and **json**, hidden unless enabled on **Settings → Developer**):

- **about** — A read-out of the connected tablet's specs, split into two cards — **Basics** (name, active area in mm & inches, its diagonal, and aspect ratio) and **Features** (digitizer resolution in LP/mm and LPI, pressure levels, pen/express/mouse button counts, and wheels/touch-strip/touch support).
- **dynamics** — Pressure, position, and tilt dynamics, grouped on one tab. The pressure/position parts are enforced by the bundled *OpenTabletArtist – Pen Dynamics* filter so they affect **every** app (Krita, Clip Studio Paint, Photoshop, …), not just one. There's **no on/off switch** — dynamics simply do nothing until you shape the curve or raise a smoothing slider (a linear curve with zero smoothing is a true no-op), and the filter stays enabled from then on. Edits are debounced and applied to the daemon automatically. The tab has three sections:
  - **Pressure** — a live pressure bar, the pressure curve, and pressure smoothing. A **Live pressure** bar at the top shows a dot for the raw incoming pressure and, when the curve or smoothing are shaping it, a second dot for the processed value (curve + smoothing, so it lags exactly as your apps receive it). Below that, **Binary pressure** (#494, moved here from Pen Inputs) gives apps a flat on/off contact by dropping pressure entirely; while it's on the curve is inert and the tab flags it. (Smoothing always runs after the curve; the old order toggle was removed.)
  - **Curve** — drag the pink **min** node and cyan **max** node to set where pressure starts and saturates (input → output); the **MIN** and **MAX** node values are shown read-only beside the chart as `input → output`. Use the **Softness** slider to bend the response (positive = lighter/concave, negative = firmer/convex; the ↺ button resets it to linear). **Presets** (Linear / Soft / Firm) are quick starting points. While you draw, a green dot tracks your **live pen pressure** on the curve.
  - **Pressure smoothing (jitter reduction)** — evens out pressure jitter (0 = off to 1 = max; perceptually scaled, like Slimy Scylla, so the slider feels even across its range). Smoothing runs after the curve. It applies while the pen is down and resets each time it lifts, so strokes start crisp with no carry-over.
  - **Position** — **Position smoothing** steadies wobbly lines (0 = off to 1 = max, perceptually scaled). Applies while the pen is down and resets on lift.
  - **Tilt** — a single **Disable tilt** toggle that stops any tilt being reported to your apps; a tilt curve/smoothing may join it later.
- **mapping** — Maps the tablet to a monitor and sets its active area, side by side in one two-column tab. On the **display** side, a diagram shows the whole picture in one view: your monitors across the top (to scale/position, numbered, with the primary monitor marked — resolution, refresh, and port are listed below the diagram, with **(PRIMARY)** beside the primary monitor's name, rather than crowding the boxes), the **tablet's active area** below, and two **gradient beams** joining the active area's left and right edges to the selected display's matching edges (so the left↔left / right↔right correspondence is obvious). **Click a monitor** to map the tablet to that whole display (aspect-locked) — it applies immediately, with no separate Apply step. If the stored mapping isn't a clean single display, the tab flags it — a **warning** when part of the area falls off-screen (the pen would reach dead zones) or a **note** for a custom/multi-display area — and the diagram also **overlays the mapped region** on the monitors (spilling into empty space for an off-screen mapping, or a dashed outline for a custom one) so the problem is visible, not just described; clicking a display fixes it. **Display Settings** opens Windows Display Settings; the diagram refreshes automatically when you add or remove a display.
  - **Active area** — A picture of the region of the tablet that's currently mapped to the display (the effective area inside the full digitizer), with usage stats — percentage of the tablet used, the effective and full sizes, and the diagonal. A **Millimeters / Inches** toggle switches every length between metric and imperial. The diagram is **interactive** (#199/#59): **drag the highlighted area** to move it, or **drag a corner** to resize it proportionally (the shape stays locked to your display's aspect, so the mapping is never distorted). A **Size** slider (10–100%) does the same resize precisely, and **Maximize** snaps back to the largest centered fit. A smaller active area maps less of the tablet to the whole screen, so the pen is effectively more precise. Changes apply when you release.
  - **Rotation** — a **None / 90° / 180° / 270°** selector rotates the active area to match how you physically turn the tablet — **180°** puts the express keys on the opposite side; **90°/270°** are portrait, which shrink the active area to the largest that fits the tablet once rotated (the display stays filled and undistorted). When rotated, the diagram draws the **tablet turned** the way you're holding it (with a marker on its top edge) while the active area stays upright, so you always edit the same rectangle. Rotating changes the mapping, so re-run calibration if you use it. (Portrait on a **pen display** — rotating the whole panel + the Windows display — is tracked separately in #490.)
- **calibration** — *(Absolute mode)* Three cards, one per density — **4 point**, **9 point**, and **25 point** — each with a diagram of the point grid on a screen, a note on when to use it, and a **Start** button. The currently-active calibration is marked with an accent border and an **"In use"** badge. A status card shows the current state with an **On/Off toggle** that turns the correction off and back on *without clearing it* — so you can compare the pointer with and without calibration — and a **Clear calibration** button that removes it entirely. Starting one opens a full-screen overlay on the mapped display where you record each target so the cursor lands where you see the nib. **Rest the pen on the target and hold still** — a ring fills as you hold, and the point is recorded once it's full (holding averages out jitter for an accurate point; lifting or drifting off the target before it fills just resets the ring). **Hold the pen at your natural drawing angle** — tilted the way you actually draw, not bolt-upright — so the calibration matches your real hand position; the overlay reminds you of this. If you misfire, **Undo last point** removes just the most recent point and re-arms that target (versus **Redo all**, which starts over).
  - **4 point** — the recommended choice; tap the four corners. Fits an **affine** correction (offset/scale/rotation/shear), averaged over the four taps so it stays steady even if a tap is slightly off.
  - **9 point** (3×3 grid) and **25 point** (5×5 grid) currently fit the **same affine** correction — the extra taps just make the least-squares fit more robust, so try them if 4-point feels slightly off. (On flat, accurate pen displays affine measured best at every density; a per-node grid over-corrects. The grid solver is kept for tablets with genuine localized distortion, #486.) The correction is tied to the current mapping — recalibrate if you change it.
  - **Calibration report** — after a calibration, a **View report** button on the status card opens a dialog (#500/#501) listing the recorded points as a positional report. Screen coordinates are **relative to the display you calibrated** (0 to its width/height), not whole-desktop pixels, so they read naturally against that one display. For each point it shows the on-screen **target** (display pixels), the **measured (px)** — where the *uncorrected* pen actually landed, in the same pixels, so it's directly comparable to the target — the **Δ (px)** between them (the parallax that point had), the **raw** tablet coordinate the pen reported (a debugging detail; it's in the tablet's own digitizer units, which run to tens of thousands, so it looks nothing like the pixel columns), and the number of samples averaged. A **fit line** summarizes the pointing error the calibration corrected (RMS and worst-case px) and flags a tap that stands out as a likely misfire. When the tablet reports pen tilt, a **tilt line** shows the average angle you held the pen at (altitude above the surface + lean direction), so you can see your natural drawing angle was captured. **Copy** puts the whole table plus the fit and tilt summary on the clipboard for sharing or debugging. It stays with the calibration, so it's there whenever one is active.
  - **Backup & Restore** — **Export calibration** writes the calibration (its recorded taps + solved model) to a file; **Import calibration** loads one back so you can restore it without re-tapping. Import is matching-only — it applies only when the tablet's current mapping matches the saved one.
- **buttons** — The tablet's auxiliary buttons (express keys on the tablet body; the pen's own switches live on the **Pen** page), **fully editable**, one card each. Pick a binding **type** (None / Keyboard / Mouse button / Mouse scroll): Keyboard offers Ctrl/Shift/Alt modifiers + a key (a combo writes a Multi-Key binding); Mouse button is Left/Right/Middle/Back/Forward; Mouse scroll is a direction. **Pressing a physical button highlights its card live.** A **Buttons enabled** master toggle suspends all mappings (kept and restored when re-enabled), and **Clear all** removes every binding.
- **wheels** — Per-direction bindings (clockwise / counter-clockwise) and any wheel buttons, on hardware with a wheel or dial. Each wheel is one card with a live gauge; a **Wheel enabled** master toggle suspends all wheel mappings and **Clear all** removes them.
- **hover / filters / json** *(hidden — enable on **Settings → Developer → Interface**)* — **hover** shows the live hover distance; **filters** lists the tablet's input filters (friendly name, full type path, enabled/disabled — a stale filter from an older app name is flagged **Legacy** and cleaned up; OpenTabletArtist keeps only its own approved filters enabled, notably **Pen Dynamics**, and disables any third-party/driver-built-in one so it can't alter the stroke); **json** is a raw view of the tablet's settings.

The **Pen** page's tabs are **movement** and **inputs**:

- **movement** — The tablet's **output mode**. Pick a movement mode — **Normal (Absolute)** maps the pen 1:1 to the screen (recommended for drawing) or **Mouse-like (Relative)** moves the cursor like a mouse (often for games) — and below both is a **Don't use Windows Ink** toggle *(Windows only)*. Windows Ink carries pressure & tilt, but Windows treats the pen like touch, so in some apps dragging the pen scrolls the page instead of selecting. Turning Windows Ink off switches to OpenTabletDriver's plain output, so the pen acts like a mouse — dragging selects text and objects — at the cost of pressure and tilt. The toggle is independent of the movement mode, so all four combinations are possible. While Windows Ink is off, Home shows an **informational** note (not a warning) that pressure/tilt are disabled. *(A tablet that lands on a non-Windows-Ink mode **without** this being an intentional choice still gets a warning + **Fix** to switch it back.)*
- **inputs** — The pen's switches in three columns: on the **left**, the **tip** and **eraser** cards plus a **Pen input** card; in the **middle**, a diagram of the pen standing tip-down with its side buttons highlighted and **numbered** for the ones this pen has; on the **right**, a card per **barrel button** (numbered 3·2·1 top-to-bottom, to line up with the diagram). Each tip/eraser/button is a status card (#495): a green **"Adaptive Binding — recommended"** badge when it's on Adaptive Binding, or an amber "Not the recommended setting" + a **Use Adaptive** button when it's drifted onto anything else. Adaptive is the only supported choice — under Windows Ink (which OTA's output modes use) the other binding types don't work — so there's nothing else to pick. The **Pen input** card holds **Disable pen tip** (#493 — clears the tip binding so tapping does nothing; your previous tip binding is stashed and restored when you turn it back off). A pen with no barrel buttons just shows the left column beside a plain pen.

### Presets

*(Settings → Presets tab.)* At the top, a pinned **Current settings** card ("In use now") represents the configuration your tablet is using right now — the live `settings.json` you edit on the Tablet pages. It's what hotkey switches revert to, and every **preset** below is a *saved copy* of it.

A **preset** is a named backup of your entire OTD configuration (all tablets' settings). Cards show the preset name and file last-modified time, sorted newest first. Presets are what **Load** and preset hotkeys apply.

> Note: a **preset** is a whole-configuration backup saved by OpenTabletArtist — not OpenTabletDriver's own per-tablet `Profile`. Each preset file contains the complete OTD `Settings` (all tablets).

On the Current settings card:

- **Save as preset** — Saves the current settings as a copy with an auto-numbered name: `Preset`, `Preset 2`, `Preset 3`, ... (lowest available number is reused if you delete one). Rename freely after saving.
- **Browse** — Opens the presets folder in Explorer.

Each preset card has:

- **Load** — Applies the preset and makes it your **Current settings** (this is a permanent switch — see [Switching presets](#switching-presets)).
- **Update** — Overwrites the preset with the current settings.
- **Duplicate** — Saves a copy of the preset under a new auto-named `<name> copy` (numbered if that's taken, e.g. `<name> copy 2`). The copy is independent — it doesn't carry the original's hotkey.
- **Rename** — Prompts for a new name (simple text dialog, no file picker).
- **Delete** — Removes the preset file after a confirmation prompt.

The "No presets" empty state appears only when the presets folder is actually empty.

### Switching presets

There are several ways to change the active configuration, split by whether the change is **permanent** (rewrites your Current settings) or **temporary** (a live override that's restored later).

Whole-preset switches:

| How | Where | Effect |
|---|---|---|
| **Load** a preset | Presets page | **Permanent** — applies it and makes it your Current settings. |
| **Preset hotkey** | Settings → Hotkeys | **Temporary** — a global keyboard shortcut applies a preset as a live override; a "Preset override" chip shows while active. Your Current settings are untouched. |

Monitor-mapping switches (change only which monitor the tablet maps to — all **permanent**):

- **Cycle mapped monitor** hotkey (Settings → Hotkeys).
- **Switch Display** in the system-tray menu.
- The display picker on a tablet's page.

Notes:

- Only **Load** is a permanent whole-preset switch; the preset hotkey is temporary and leaves your Current settings intact.
- There's currently no one-click "back to Current settings" button for a hotkey-driven override — it clears when you switch again.

### Hotkeys

*(Settings → Hotkeys tab.)* Global keyboard shortcuts that work even when OpenTabletArtist isn't focused. Assign a combination (a modifier — Ctrl / Alt / Shift / Win — plus a letter, digit, or F-key) with the on-screen picker, and it takes effect system-wide.

- **Cycle mapped monitor** — moves the active tablet's area to the next monitor (wrapping around). Shows a toast with the new monitor; no-ops (with a toast) if you only have one display or no tablet is active.
- **Preset switching** — assign a hotkey to a preset to switch to it instantly. The switch is a live-only override (your saved default isn't overwritten); a "Preset override" chip shows while one is active.

> **Per-App Presets** (automatic preset switching by foreground app) is temporarily hidden and disabled while its switching model is being reconsidered. The feature and any saved app→preset mappings are retained and may return in a later version.

### Configs

Manages OpenTabletDriver's tablet **configuration** files — the per-tablet JSON definitions that let the daemon recognise and drive a tablet. Two things live here (see `docs/design/tablet-configs.md` for the full model):

- **Your config folder** — lists the loose config JSONs in the daemon's actual configurations folder (queried from the running daemon, so it's the folder OTD really reads — on Windows the portable `userdata\Configurations` or `%LOCALAPPDATA%\OpenTabletDriver\Configurations`). Each row shows the tablet's friendly name (from the JSON `Name`, falling back to a manufacturer-folder + filename combo). Per-row **View** opens the formatted JSON read-only; **Delete** removes the file after a confirmation. The header has **Refresh** and **Open Folder**.
- **Add tablet support** — **Check for more configs** fetches OpenTabletDriver's approved tablet configs for the bundled driver version and lists any your install doesn't already have (useful for a newly-supported tablet). **Install** downloads one into your config folder; reconnect the tablet (or restart the daemon) to use it.

> A tablet driven by a config file that **replaces** one of OpenTabletDriver's built-in, vetted configs (same name) raises a gentle *Needs attention* recommendation on Home — deliberate overrides are fine, but it's worth knowing you're off the vetted default if the pen behaves oddly. Its **Review** button opens this page.

### Driver Cleanup

*(Settings → Driver Cleanup; Windows-only.)* Finds and removes conflicting manufacturer tablet drivers.

- **Conflicting drivers detected** — When the daemon flags a manufacturer driver (parsed from its detection warnings), each is shown as its own card with the driver name, its impact ("Blocks OpenTabletDriver from detecting tablets" / "Can cause flaky tablet support"), the offending processes, the full (selectable) daemon message, and an **Open OpenTabletDriver FAQ** link. (OpenTabletArtist's own process is filtered out so it isn't mistaken for a conflict.)
- **TabletDriverCleanup** — Manages the [TabletDriverCleanup](https://github.com/OpenTabletDriver/TabletDriverCleanup) tool by the OTD team that removes leftover bits from previous manufacturer tablet drivers (Wacom, Huion, XP-Pen, etc.). Install the tool first via **Install** (downloads the latest release to `%LocalAppData%\TabletDriverCleanup`, no admin required); then **Run** launches it with a UAC prompt and a visible terminal so the cleanup output is readable. **Browse** opens the install folder; **Uninstall** removes it.

### Diagnostics

Live tablet input visualization. See `docs/DIAGNOSTICS.md` for details. When more than one tablet is connected, a **Show** selector picks which tablet's live reports to display (the daemon's debug stream carries all tablets at once); with a single tablet it's hidden.

### Scribble

A paint canvas for confirming the pen is working — draw with the pen and watch pressure, tilt, and twist live.

- **Tablet picker** — when more than one tablet is connected, a selector chooses which tablet this page (and the other single-tablet flows) acts on; hidden with a single tablet.

- **Dynamics indicators** — when Pen Dynamics is enabled on the active tablet, a row of chips spells out exactly what's altering the stroke — a **Dynamics on** chip, plus **Pressure curve** (the curve is bent, not linear), **Pressure smoothing**, and/or **Position smoothing** — so behavior changes are never a mystery. If dynamics is enabled but everything is at its default, it says so ("No curve or smoothing — behaves linear"). The **Dynamics** button sits on this same row.
- **Pointer-only warning** — *Pointer-only* Mode draws nothing, so active dynamics can't be seen. Picking it while dynamics is on shows a short warning, and pressing the **Dynamics** button automatically switches Mode to a pressure view so you can feel your edits.
- **Input source** (toggle) — where both the position and the pressure/tilt come from:
  - **Windows Ink** — the OS pointer (what a drawing app actually receives). The stroke renders under the pen.
  - **Raw input** — the raw OTD daemon signal, before the Windows Ink output stage — so it works even when Windows Ink isn't delivering pointer events. The raw tablet position is mapped to the canvas through the active tablet's **Absolute** area mapping, so the stroke still lands under the pen. This needs an **Absolute output mode** (e.g. Windows Ink Absolute); in **Relative** mode there's no absolute position to map, so the canvas is disabled with a note.
- **Mode** — what to visualize: **Pressure Brush** (pressure → brush size), **Tilt Brush 1** (tilt azimuth → brush rotation), **Tilt Brush 2** (tilt altitude → brush size), **Barrel Rotation Brush** (twist → brush rotation), or **Crosshairs (No drawing)** (a crosshair, no drawing).
- **Readouts** — live values, with X/Y shown paired in one cell: **Canvas** (where the stroke lands), **Raw** (the source's raw coordinates — tablet units in Raw-input mode), pressure, **Tilt** X/Y, azimuth, altitude, twist, and hover.
- **Clearing** — the **Clear** button, or press **Delete** / **Backspace**.
- **Dynamics** — opens a focused **Pen Dynamics** editor for the detected tablet (just the pressure curve + smoothing, no other tabs) without leaving Scribble, so you can tweak and immediately feel the result.

### Plugins

A read-only list of the OpenTabletDriver plugins installed in the daemon's plugin folder. Each row shows the plugin's name, version (when available), and whether it's **Active** (referenced by an enabled output mode or filter on a tablet) or just **Installed**. The OpenTabletArtist – Pen Dynamics plugin appears here once it's installed. Use the refresh icon to rescan, or **Browse** to open the plugin folder in File Explorer. (Installing/removing plugins is done through OpenTabletDriver itself; this view is informational.)

### Console

*(Advanced → Console.)* The live OpenTabletDriver daemon log, streamed with per-level coloring and a **minimum-level** filter. **Copy** is a dropdown — copy the visible log as **text**, a **Markdown** table, or an **HTML** table. **Clear** empties the view.

### Daemon

The full daemon status and controls (this moved off Home, which now shows the daemon only when there's a problem). Connection status with **Start** when disconnected, **Restart** / **Stop** when running, and a **Refresh** to check status. The **Start / Stop / Restart** actions show an inline progress bar with live phase text (Stopping… → Starting… → Connecting…) while they run, and report a clear error if the daemon doesn't come online within 30 seconds.

Below the "Daemon running" line, a **daemon ownership indicator** shows which daemon the app is actually connected to (one of three states):

- **App-owned daemon** (green check) — connected to this project's build under `external/OpenTabletDriver/OpenTabletDriver.Daemon/bin/`.
- **External daemon (not app-owned)** (amber warning) — connected to a different OTD instance, e.g. a separately-installed daemon the user already had running. Hint text suggests clicking **Restart**, which kills any running daemon and relaunches this project's build.
- **Daemon source unknown** (grey) — connected, but the daemon's exe path couldn't be read (e.g. it's running elevated). The app reports this honestly rather than guessing.

Ownership is detected by resolving the process on the other end of the named pipe (`GetNamedPipeServerProcessId`) and comparing its exe path to the project's daemon build. The actual daemon exe path is shown on hover (app-owned / external states). The page also shows the embedded OTD version and an **OTD UX** launcher to open the original OpenTabletDriver interface for comparison.

### Windows Ink Plugin

*(Advanced → Drivers — the top half of the Windows-only Drivers tab, above VMulti.)* Manages the third-party Windows Ink output-mode plugin (from Kuuuube's VoiDPlugins), which delivers pen pressure and tilt to your apps. Shows:

- **Install status** — a green dot + "Installed" (with the **plugin version** as a chip next to the name) or a grey dot + "Not installed."
- **Output mode** — whether the tablet actually uses a Windows Ink mode ("Plugin active" / "Not configured").
- **Supported driver vs OTD** — the plugin's declared supported driver version alongside the running OTD version. A warning indicator appears if the installed plugin doesn't declare support for the current OTD version (per OTD's own compatibility rule).
- **Buttons** — **Install** (when not installed); **Check for Update** (when installed) which queries the official OTD Plugin-Repository — if a newer plugin version is found the button becomes **Install Update (vX)**, otherwise it reports "Up to date"; **Uninstall**; and a **Refresh** icon (top-right) that re-reads the installed plugin and re-checks the repository in one step. Install/update/uninstall are driven through the daemon's plugin RPC; the card updates its status as soon as each operation completes.

### VMulti Driver

*(Advanced → Drivers — the bottom half of the Windows-only Drivers tab, below Windows Ink Plugin.)* VMulti is the virtual pen device the Windows Ink plugin injects pressure and tilt through. Detection runs via both Setup API and HID enumeration. Has **Install** / **Uninstall** wizards, **Refresh** to re-check, and **Browse** to open the driver folder. Both **Install** and **Uninstall** run in-app (one UAC prompt each, no flashing cmd window) and offer to **restart** Windows afterward. Install creates the VMulti device via `devcon`; Uninstall removes the driver and the active device *and* cleans up the leftover driverless `djpnewton\vmulti` nodes (Device Manager Code 28) that the stock removal left behind. Detection reflects a *working* driver, so any remaining driverless leftovers are reported as **Not installed**, not as installed.

### Startup

A single toggle — **Start OpenTabletArtist when Windows starts** — that launches the app minimized to the tray at sign-in, so hotkeys are ready without opening it yourself (per-user Run key; Windows only).

### Developer

*(Settings → Developer tab.)* Testing aids, not needed for normal use. Three sub-tabs:

- **Force warnings** — two columns. **Trigger health warnings** (left) is a set of toggles that force each real *Needs attention* warning to appear on Home with its true text and **Fix** button, so the cards can be reviewed and screenshotted without reproducing the underlying problem (right-clicking an induced card on Home offers a hidden **Dismiss**; genuine warnings can't be dismissed this way). **Introduce config errors** (right) instead makes a *real, persisted* change to the active tablet so a genuinely broken state can be reproduced: **Push active area off-screen** (trips the off-screen mapping check — undo by remapping to a display), **Set rotation to 20°** (a non-standard angle that trips the rotation check — undo by picking a standard rotation on the mapping tab), and **Create off calibration** (fabricates a slightly-wrong 4-point calibration — undo with **Clear calibration** on the Calibration tab).
- **Interface** — two columns. **Tablet page extras** reveals the otherwise-hidden **hover**, **filters**, and **json** tabs on a tablet's page, re-enables the **Cut below input minimum** dead-zone checkbox on the pressure curve, and toggles **card-role tags** (a dev overlay). **Window size** forces the app window to an exact pixel size (presets from 2560×1440 down to 1280×720), for previewing small-display layouts or consistent screenshots; reset by restarting.
- **Screenshot** — **Screenshot all pages** visits every page and sub-tab in turn, saving an image of each (PNG, JPG, or both) into a new timestamped folder under `Pictures\OpenTabletArtist`; a separate toggle adds an on-page **Capture** button so you can screenshot whichever page you're viewing. Because it renders the actual controls, captures are pixel-perfect and free of the window-focus / occlusion / DPI issues of manual capture.

### About

Project information — what OpenTabletArtist is, the app version, and a **Resources** list (source code, releases, user manual).

A **Help** card is the obvious place to get support: it points you to the **#help-forum** channel on the [Drawing Tablet Discord](https://discord.gg/Rr2MXeM7Ny) and asks you to put **"OTA"** in the title so it reaches the right people. (Start here rather than the OpenTabletDriver forums — most questions aren't OTD-specific, and anything that turns out to be an OTD issue can be escalated from there.)

## Navigation

Click a page in the top navigation bar to switch pages; the active page is underlined in the accent colour. Pages with sub-sections (Tablet, Pen, Settings, Advanced) show a pivot row of tabs beneath the bar.

## Theme

The **Appearance** tab (under **Settings**) holds theme preferences:

- **Theme** — a selector with **System** (follows your Windows light/dark setting), **Light**, **Dark**, and **Sakura** (a pink skin with a cherry-blossom backdrop and frosted-glass panels — the default). Applied immediately and remembered across restarts.
- **Falling petals** *(Sakura only)* — toggles the drifting cherry-blossom animation.
- **Frosted glass** *(Sakura only)* — a **Card opacity** slider that tunes how translucent the cards are (the backdrop shows through). Live and persisted; scoped to the Sakura skin.

## System tray & background mode

The app is **single-instance**: launching it again while it's already running (including when it's minimized to the tray) doesn't open a second window or tray icon — it just brings the existing window to the front.

The app runs with a **system tray icon**. **Closing the window minimizes it to the tray** rather than exiting — the app keeps running so its daemon controls stay one click away (the first time you close, a one-time hint explains this). From the tray you can:

- **Click the icon** — reopen the window.
- **Show OpenTabletArtist** — reopen the window.
- **Pen dynamics status** — a read-only line revealing whether the bundled Pen Dynamics filter is affecting the active tablet's pen: *off*, *on (behaves linear)*, or *Affecting your pen: Pressure curve, Pressure smoothing, Position smoothing* (only the parts actually in effect). Mirrors the Scribble page's indicator so the effect is never a mystery with the window closed. Shown only when a tablet is connected.
- **Open Tablet Settings…** — reopens the window and shows the active tablet's settings. Shown when a tablet is connected. (The tray also offers a focused **Pen Dynamics** editor.)
- **Switch Display** — a submenu listing your monitors; pick one to map the active tablet to that whole display (aspect-locked, the same mapping as clicking a display on the Tablet page's **mapping** tab). The currently-mapped display is check-marked. Shown only when the active tablet is in an Absolute output mode (otherwise there's no display area to set).
- **Active Tablet** — when more than one tablet is connected, a submenu to choose which tablet the tray actions (and the Scribble / Diagnostics pages) act on. With a single tablet it's hidden and that tablet is used automatically.
- **Start / Stop / Restart Daemon** — control the daemon directly (Start appears when it's stopped; Stop/Restart when it's running). The tray tooltip shows the current daemon status.
- **Quit** — fully exit the app (the OTD daemon, a separate process, keeps running).

## Stopping the daemon from outside this app

The OTD daemon is a separate process and keeps running after our app's window closes. Quick options for stopping it:

- **Use the OTD UX**: Click **OTD UX** on the **Daemon** tab (Advanced → Daemon) to launch `OpenTabletDriver.UX.Wpf.exe`, which has its own system tray icon with quit/show controls.
- **Use Task Manager**: `Ctrl+Shift+Esc`, find `OpenTabletDriver.Daemon.exe` in the Processes tab, right-click → End task.

The app's own tray icon (above) can also Stop/Restart the daemon directly.

## Troubleshooting

### "Not connected to daemon" on Home

1. Click **Fix** on the "Not connected to daemon" card — it starts the daemon (built from the submodule) and connects, morphing into a "Connecting to daemon…" state while it works.
2. If that doesn't resolve it, click **Open daemon page** (Advanced → Daemon) for the full controls — **Start**, **Restart**, and a **Refresh** to re-check the connection.
3. The daemon auto-starts on app launch — if it didn't, check whether another OTD instance is already running.

If the daemon page reports that **OpenTabletDriver.Daemon.exe wasn't found**, the daemon exe was never built. The app checks for it before every connection attempt and says so plainly instead of silently timing out. Build the whole solution so the daemon is produced:

```bash
dotnet build OpenTabletArtist.slnx
```

Building only the app project, or only running the test suite, does **not** produce the daemon exe (it's a standalone project the app launches as a separate process).

### "No Tablet Detected" even though my tablet is plugged in

1. Make sure the daemon is running (Advanced → Daemon).
2. Wait a few seconds — the app reconciles with the daemon every 30 seconds (and immediately on connect).
3. Click the refresh icon to force an immediate check.

### Build fails with "file is locked by OpenTabletArtist"

The running app or daemon holds the build outputs (the app exe or the OTD DLLs) open. Use
`scripts/build.ps1`, which stops those processes before building so this can't happen. To fix it by
hand, close the running app **and** stop the daemon (Task Manager → `OpenTabletDriver.Daemon.exe`, or
the tray's Quit + Stop) before rebuilding. If a previous instance hasn't fully exited it may still
hold the .exe. We have an open investigation item in `docs/FUTURES.md` to make shutdown cleaner.
