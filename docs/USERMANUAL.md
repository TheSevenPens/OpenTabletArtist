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

On launch the app auto-starts the daemon if it isn't already running, then connects.

## Using the Interface

The sidebar's top-level items are **Home**, **Tablets**, **Profiles** (with **Per-App Profiles** nested under it), **Hotkeys**, **Test Drawing**, and **About**. A collapsible **Advanced** group holds **Daemon**, **Windows Ink Plugin**, **VMulti Driver**, **Custom Tablet Compatibility**, **Diagnostics**, **Log**, **Plugins**, **Driver Cleanup**, and **Theme**.

**Tablets** is an always-expanded node: every tablet that's paired or currently connected appears as a child (with a status dot), ordered detected-first. Clicking a child opens that tablet's settings **in the right-hand pane** (no dialog); right-click a child (or use the button on its page) to **Forget** it. Clicking the **Tablets** header shows an overview / empty-state ("No tablets connected or remembered").

### Home

The landing page shows status cards:

- **Conflicting tablet driver** *(only when detected)* — a warning card when the daemon flags a manufacturer driver that can interfere with OpenTabletDriver, with a **Driver cleanup** button that jumps to that page.
- **OpenTabletDriver Daemon** — Connection status. Shows **Start** when disconnected, **Restart** / **Stop** when running, and a **Refresh** to check status. (The embedded OTD version and the **OTD UX** launcher now live on the **Daemon** page under Advanced.) Below the "Daemon running" line, a **daemon ownership indicator** shows which daemon the app is actually connected to (one of three states):
  - **App-owned daemon** (green check) — connected to this project's build under `external/OpenTabletDriver/OpenTabletDriver.Daemon/bin/`.
  - **External daemon (not app-owned)** (amber warning) — connected to a different OTD instance, e.g. a separately-installed daemon the user already had running. Hint text suggests clicking **Restart**, which kills any running daemon and relaunches this project's build.
  - **Daemon source unknown** (grey) — connected, but the daemon's exe path couldn't be read (e.g. it's running elevated). The app reports this honestly rather than guessing.

  Ownership is detected by resolving the process on the other end of the named pipe (`GetNamedPipeServerProcessId`) and comparing its exe path to the project's daemon build. The actual daemon exe path is shown on hover (app-owned / external states).
- **VMulti Driver** — Detection via both Setup API and HID enumeration. Has **Install** / **Uninstall** wizards, **Refresh** to re-check, and **Browse** to open the driver folder. Both **Install** and **Uninstall** run in-app (one UAC prompt each, no flashing cmd window) and offer to **restart** Windows afterward. Install creates the VMulti device via `devcon`; Uninstall removes the driver and the active device *and* cleans up the leftover driverless `djpnewton\vmulti` nodes (Device Manager Code 28) that the stock removal left behind. Detection reflects a *working* driver, so any remaining driverless leftovers are reported as **Not installed**, not as installed.
- **Kuuube's Windows Ink plugin** — Manages the third-party Windows Ink output-mode plugin (from Kuuuube's VoiDPlugins). Shows:
  - **Install status** — a green dot + "Installed" (with the **plugin version** as a chip next to the name) or a grey dot + "Not installed."
  - **Output mode** — whether the active profile actually uses a Windows Ink mode ("Plugin active" / "Not configured").
  - **Supported driver vs OTD** — the plugin's declared supported driver version alongside the running OTD version. A warning indicator appears if the installed plugin doesn't declare support for the current OTD version (per OTD's own compatibility rule).
  - **Buttons** — **Install** (when not installed); **Check for Update** (when installed) which queries the official OTD Plugin-Repository — if a newer plugin version is found the button becomes **Install Update (vX)**, otherwise it reports "Up to date"; **Uninstall**; and a **Refresh** icon (top-right) that re-reads the installed plugin and re-checks the repository in one step. Install/update/uninstall are driven through the daemon's plugin RPC; the card updates its status as soon as each operation completes.

The **Start / Stop / Restart** daemon actions show an inline progress bar with live phase text (Stopping… → Starting… → Connecting…) while they run, and report a clear error if the daemon doesn't come online within 30 seconds.

### Tablets (per-tablet settings)

Selecting a tablet in the sidebar opens its settings **in the right-hand pane** (no separate dialog), with a header showing the tablet name, live connection status, a **Refresh**, and a **Forget** button. The settings have seven tabs:

- **Screen Mapping** — Output mode is an **Absolute / Relative** toggle (Absolute is recommended for drawing, since it carries pressure & tilt; a warning + Fix appears if the profile is on a non-Windows-Ink mode). A **Display Mapping** diagram shows the whole picture in one view: your monitors across the top (to scale/position, numbered, with resolution + refresh), the **tablet's active area** below, and an **L-shaped arrow** from the tablet to the selected display. **Click a monitor** to select it, then **Apply mapping** to map the tablet to that whole display (aspect-locked); a "Display selection changed — click Apply mapping to save it" hint makes clear the choice isn't live until applied. A **live pen dot** tracks over the tablet area as you move the pen. **Display Settings** opens Windows Display Settings and **Refresh** re-reads monitors (the diagram also updates when you add/remove a display). Calibration is its own **Calibration** card below (Absolute mode only): a **mode** selector + **Calibrate…** opens a full-screen overlay on the mapped display where you tap targets so the cursor lands where you see the nib; you can Apply, Clear, and see whether the current calibration is stale.

  - **4 point** — the default; tap the four corners. Fits a **perspective** correction (keystone/parallax + offset/scale/rotation).
  - **9 point / 25 point** — tap a 3×3 or 5×5 grid; fits a **per-node** correction that also handles **localized** distortion. The correction is tied to the current mapping — recalibrate if you change it.
- **Pen Switches** — All the switches on the pen: tip, eraser, and pen barrel buttons. Each shows a green check + "Adaptive Binding (recommended)" when already on Adaptive Binding; otherwise a Fix (tip/eraser) or Fix All (pen buttons) button sets it.
- **ExpressKeys** — The tablet's auxiliary buttons, **fully editable**, one card each. Pick a binding **type** (None / Keyboard / Mouse button / Mouse scroll): Keyboard offers Ctrl/Shift/Alt modifiers + a key (a combo writes a Multi-Key binding); Mouse button is Left/Right/Middle/Back/Forward; Mouse scroll is a direction. **Pressing a physical button highlights its card live.** A **Buttons enabled** master toggle suspends all mappings (kept and restored when re-enabled), and **Clear all** removes every binding.
- **Dynamics** — An interactive pressure-curve editor **plus smoothing**. Toggle it on to apply custom pen dynamics to this tablet's profile; they're enforced by the bundled *OpenTabletArtist – Pen Dynamics* filter, so they affect **every** app (Krita, Clip Studio Paint, Photoshop, …), not just one.
  - **Curve** — drag the pink **min** node and cyan **max** node to set where pressure starts and saturates (input → output); the **Min/Max node** input/output values are shown read-only beside the chart. Use the **Softness** slider to bend the response (positive = lighter/concave, negative = firmer/convex; the ↺ button resets it to linear), and tick **Cut below input minimum** to turn the lead-in into a dead zone instead of a pressure floor. **Presets** (Linear / Soft / Firm) are quick starting points; **Reset** restores the identity curve. While you draw, a green dot tracks your **live pen pressure** on the curve so you can feel the mapping.
  - **Smoothing (jitter reduction)** — **Position** smoothing steadies wobbly lines and **Pressure** smoothing evens out pressure jitter (each 0 = off to 1 = max; the amount is perceptually scaled, like Slimy Scylla, so the slider feels even across its range). **Order** chooses whether smoothing runs after the curve (*Curve → Smooth*, default) or before it. Smoothing applies while the pen is down and resets each time it lifts, so strokes start crisp with no carry-over from the previous one.
  - **Reset all** (in the tab header) returns the curve, both smoothing amounts, and the order to their defaults in one click (it leaves the On/Off toggle as you set it). The curve's own **Reset** button only resets the curve.
  - Edits are debounced and applied to the daemon automatically.
- **Hover** — Limits the pen's **hover height** (#188). Toggle it on and set **Max hover** — once the pen lifts farther than this from the surface, the cursor stops tracking (it holds its last position) instead of being dragged around by a raised pen. Drawing is unaffected (in contact the hover distance is ~0, always within the limit). Hover distance is 0–255 and **not all tablets report it** — check the Diagnostics page for your tablet's live hover values to pick a limit. Enforced by the bundled *OpenTabletArtist – Hover Limit* filter; edits are debounced and applied automatically.
- **Filters** — The profile's input filters, one card each: friendly name, full type path, and enabled/disabled status. A stale filter left over from an older app name is flagged **Legacy** (and cleaned up automatically).
- **JSON** — Raw JSON view of the profile data.

A **Refresh** button in the page header reloads settings from the daemon (useful after making changes in the OTD UX).

### Profiles

A **profile** is a named backup of your entire OTD configuration (all tablets' settings). Cards show the profile name and file last-modified time, sorted newest first. Profiles are what hotkeys and per-app switching apply.

> Note: "profile" here means a whole-configuration backup saved by OpenTabletArtist — not OpenTabletDriver's own per-tablet `Profile`. Each OTA profile file contains the complete OTD `Settings` (all tablets).

Toolbar:

- **Save Profile** — Saves current settings with an auto-numbered name: `Profile`, `Profile 2`, `Profile 3`, ... (lowest available number is reused if you delete one). Rename freely after saving.
- **Browse** — Opens the profiles folder in Explorer.

Each profile card has:

- **Load** — Applies the profile's settings to the daemon **and saves them as your default** (this is a permanent switch — see [Switching profiles](#switching-profiles)).
- **Update** — Overwrites the profile with the current settings.
- **Rename** — Prompts for a new name (simple text dialog, no file picker).
- **Delete** — Removes the profile file after a confirmation prompt.

The "No profiles" empty state appears only when the profiles folder is actually empty.

### Switching profiles

There are several ways to change the active configuration, split by whether the change is **permanent** (rewrites your saved default) or **temporary** (a live override that's restored later).

Whole-profile switches:

| How | Where | Effect |
|---|---|---|
| **Load** a profile | Profiles page | **Permanent** — applies it and saves it as your default. |
| **Profile hotkey** | Hotkeys page | **Temporary** — a global keyboard shortcut applies a profile as a live override; a "Profile override" chip shows while active. Your saved default is untouched. |
| **Per-app switching** | Per-App Profiles page | **Automatic + temporary** — the mapped profile is applied when its app comes to the foreground; unmapped apps use a default. An "App profile" chip shows the active one. Restored on disable/exit. |

Monitor-mapping switches (change only which monitor the active profile maps to — all **permanent**):

- **Cycle mapped monitor** hotkey (Hotkeys page).
- **Switch Display** in the system-tray menu.
- The display picker on a tablet's page.

Notes:

- Only **Load** is a permanent whole-profile switch; the hotkey and per-app methods are temporary and leave your on-disk default intact.
- Per-app switching deliberately **does not** change the monitor mapping — set that once (tablet page / cycle-monitor hotkey) and it sticks across per-app switches.
- There's currently no one-click "clear override / back to default" button for a hotkey-driven override — it clears when you switch again (per-app overrides restore automatically on disable/exit).

### Hotkeys

Global keyboard shortcuts that work even when OpenTabletArtist isn't focused. Assign a combination (a modifier — Ctrl / Alt / Shift / Win — plus a letter, digit, or F-key) with the on-screen picker, and it takes effect system-wide.

- **Cycle mapped monitor** — moves the active tablet's area to the next monitor (wrapping around). Shows a toast with the new monitor; no-ops (with a toast) if you only have one display or no tablet is active.
- **Profile switching** — assign a hotkey to a profile to switch to it instantly. The switch is a live-only override (your saved default isn't overwritten); a "Profile override" chip shows while one is active.

### Per-App Profiles

Automatically applies a profile when the foreground application changes — the way Wacom/XP-Pen/Huion drivers do. Map an app to a profile, and switching to that app reconfigures the tablet; unmapped apps use a configurable default. (Nested under **Profiles** in the sidebar.)

To use it: create the profiles you want first (Profiles page), then here tick **Enable per-app profile switching**, pick a **Default profile** for unmapped apps, and **Add app…** to map a running application to a profile. An **App profile** chip in the main window shows which profile is currently applied.

Caveats:

- **Switches are temporary.** They're applied live to the daemon only — your saved default settings file is never overwritten, the settings editor keeps showing/editing your default, and your default is restored when you disable the feature or quit the app.
- **It only works while OpenTabletArtist is running** (including minimized to the tray). There's no switching when the app is closed.
- **Profiles are whole-configuration.** A profile is your entire OTD `Settings`, so a per-app switch affects *all* tablets, not just one — set up profiles with that in mind if you have multiple tablets.
- **The monitor mapping is left alone.** A per-app switch does *not* change which monitor the tablet points at — only the current monitor mapping is kept (moving an app between displays won't yank the tablet to a stale monitor). Set the monitor from the tablet page or with the *Cycle mapped monitor* hotkey; that choice sticks across per-app switches.
- **Wait for the pen to lift** (on by default) holds a switch until you finish the current stroke, so a mapping change can't jump the cursor mid-stroke.
- **Elevated and Store (UWP) apps** may not report a usable executable path; matching falls back to the process name, and some packaged apps report an `ApplicationFrameHost`-style path — mapping by name still works in most cases.
- **App-owned daemon only.** The feature is disabled (with a banner) while a daemon that OpenTabletArtist didn't start is running, because its settings and profile files may not match.

### Custom Tablet Compatibility

Lists tablet config JSON files in `%AppData%\OpenTabletDriver\Configurations\` (the folder is created on app startup if missing). Each row shows the tablet's friendly name (read from the JSON `Name` field, falling back to a manufacturer-folder + filename combo). Per-row **View** opens the formatted JSON in a read-only viewer; **Delete** removes the file after a confirmation prompt. The panel header has a **Refresh** icon to rescan and an **Open Folder** button.

### Driver Cleanup

Finds and removes conflicting manufacturer tablet drivers.

- **Conflicting drivers detected** — When the daemon flags a manufacturer driver (parsed from its detection warnings), each is shown as its own card with the driver name, its impact ("Blocks OpenTabletDriver from detecting tablets" / "Can cause flaky tablet support"), the offending processes, the full (selectable) daemon message, and an **Open OpenTabletDriver FAQ** link. (OpenTabletArtist's own process is filtered out so it isn't mistaken for a conflict.)
- **TabletDriverCleanup** — Manages the [TabletDriverCleanup](https://github.com/OpenTabletDriver/TabletDriverCleanup) tool by the OTD team that removes leftover bits from previous manufacturer tablet drivers (Wacom, Huion, XP-Pen, etc.). Install the tool first via **Install** (downloads the latest release to `%LocalAppData%\TabletDriverCleanup`, no admin required); then **Run** launches it with a UAC prompt and a visible terminal so the cleanup output is readable. **Browse** opens the install folder; **Uninstall** removes it.

### Diagnostics

Live tablet input visualization. See `docs/DIAGNOSTICS.md` for details. When more than one tablet is connected, a **Show** selector picks which tablet's live reports to display (the daemon's debug stream carries all tablets at once); with a single tablet it's hidden.

### Test Drawing

A paint canvas for confirming the pen is working — draw with the pen and watch pressure, tilt, and twist live.

- **Tablet picker** — when more than one tablet is connected, a selector chooses which tablet this page (and the other single-tablet flows) acts on; hidden with a single tablet.

- **Dynamics indicators** — when Pen Dynamics is enabled on the active tablet, the status banner is followed by an "Affecting your pen:" row of chips spelling out exactly what's altering the stroke — **Pressure curve** (the curve is bent, not linear), **Pressure smoothing**, and/or **Position smoothing** — so behavior changes are never a mystery. If dynamics is enabled but everything is at its default, it says so ("No curve or smoothing — behaves linear").
- **Pointer-only warning** — *Pointer-only* Mode draws nothing, so active dynamics can't be seen. Picking it while dynamics is on shows a short warning, and pressing the **Dynamics** button automatically switches Mode to a pressure view so you can feel your edits.
- **Input source** (toggle) — where both the position and the pressure/tilt come from:
  - **App input (Windows Ink)** — the OS pointer (what a drawing app actually receives). The stroke renders under the pen.
  - **Driver input (OTD)** — the raw OTD daemon signal, before the Windows Ink output stage — so it works even when Windows Ink isn't delivering pointer events. The raw tablet position is mapped to the canvas through the active tablet's **Absolute** area mapping, so the stroke still lands under the pen. This needs an **Absolute output mode** (e.g. Windows Ink Absolute); in **Relative** mode there's no absolute position to map, so the canvas is disabled with a note.
- **Mode** — what to visualize: pressure → brush size, tilt azimuth → brush rotation, tilt altitude → brush size, twist → brush rotation, or pointer-only (a crosshair, no drawing).
- **Readouts** — live values: Canvas X/Y (where the stroke lands), Raw X/Y (the source's raw coordinates — tablet units in Driver mode), pressure, tilt X/Y, azimuth, altitude, twist.
- **Clearing** — the **Clear** button, or press **Delete** / **Backspace**.
- **Dynamics** — opens a focused **Pen Dynamics** editor for the detected tablet (just the pressure curve + smoothing, no other tabs) without leaving Test, so you can tweak and immediately feel the result.

### Plugins

A read-only list of the OpenTabletDriver plugins installed in the daemon's plugin folder. Each row shows the plugin's name, version (when available), and whether it's **Active** (referenced by an enabled output mode or filter in a profile) or just **Installed**. The OpenTabletArtist – Pen Dynamics plugin appears here once it's installed. Use the refresh icon to rescan, or **Browse** to open the plugin folder in File Explorer. (Installing/removing plugins is done through OpenTabletDriver itself; this view is informational.)

### Log

The live OpenTabletDriver daemon log, streamed with per-level coloring and a **minimum-level** filter. **Copy** is a dropdown — copy the visible log as **text**, a **Markdown** table, or an **HTML** table. **Clear** empties the view.

### Daemon

Details about the bundled OpenTabletDriver engine: the embedded OTD version, and an **OTD UX** launcher to open the original OpenTabletDriver interface for comparison.

### About

Project information.

## Navigation

Click items in the sidebar to switch between pages. The active page is highlighted with an accent bar.

## Theme

The **Theme** page (under **Advanced**) holds appearance preferences:

- **Theme** — a selector with **System** (follows your Windows light/dark setting), **Light**, **Dark**, and **Sakura** (a pink skin with a cherry-blossom backdrop and frosted-glass panels — the default). Applied immediately and remembered across restarts.
- **Falling petals** *(Sakura only)* — toggles the drifting cherry-blossom animation.
- **Frosted glass** *(Sakura only)* — a **Card opacity** slider that tunes how translucent the cards are (the backdrop shows through). Live and persisted; scoped to the Sakura skin.

## System tray & background mode

The app is **single-instance**: launching it again while it's already running (including when it's minimized to the tray) doesn't open a second window or tray icon — it just brings the existing window to the front.

The app runs with a **system tray icon**. **Closing the window minimizes it to the tray** rather than exiting — the app keeps running so its daemon controls stay one click away (the first time you close, a one-time hint explains this). From the tray you can:

- **Click the icon** — reopen the window.
- **Show OpenTabletArtist** — reopen the window.
- **Pen dynamics status** — a read-only line revealing whether the bundled Pen Dynamics filter is affecting the active tablet's pen: *off*, *on (behaves linear)*, or *Affecting your pen: Pressure curve, Pressure smoothing, Position smoothing* (only the parts actually in effect). Mirrors the Test page's indicator so the effect is never a mystery with the window closed. Shown only when a tablet is connected.
- **Open Tablet Settings…** — reopens the window and shows the active tablet's settings. Shown when a tablet is connected. (The tray also offers a focused **Pen Dynamics** editor.)
- **Switch Display** — a submenu listing your monitors; pick one to map the active tablet to that whole display (aspect-locked, the same mapping as the Screen-Mapping tab's *Apply mapping*). The currently-mapped display is check-marked. Shown only when the active tablet is in an Absolute output mode (otherwise there's no display area to set).
- **Active Tablet** — when more than one tablet is connected, a submenu to choose which tablet the tray actions (and the Test / Diagnostics pages) act on. With a single tablet it's hidden and that tablet is used automatically.
- **Start / Stop / Restart Daemon** — control the daemon directly (Start appears when it's stopped; Stop/Restart when it's running). The tray tooltip shows the current daemon status.
- **Quit** — fully exit the app (the OTD daemon, a separate process, keeps running).

## Stopping the daemon from outside this app

The OTD daemon is a separate process and keeps running after our app's window closes. Quick options for stopping it:

- **Use the OTD UX**: Click **OTD UX** on the **Daemon** page (under Advanced) to launch `OpenTabletDriver.UX.Wpf.exe`, which has its own system tray icon with quit/show controls.
- **Use Task Manager**: `Ctrl+Shift+Esc`, find `OpenTabletDriver.Daemon.exe` in the Processes tab, right-click → End task.

The app's own tray icon (above) can also Stop/Restart the daemon directly.

## Troubleshooting

### "Not connected" on the OpenTabletDriver Daemon card

1. Click **Start** to launch the daemon (built from the submodule).
2. Click the refresh icon to check the connection.
3. The daemon auto-starts on app launch — if it didn't, check if another OTD instance is already running.

If the daemon card shows **"OpenTabletDriver.Daemon.exe wasn't found…"**, the daemon exe was never built. The app checks for it before every connection attempt and says so plainly instead of silently timing out. Build the whole solution so the daemon is produced:

```bash
dotnet build OpenTabletArtist.slnx
```

Building only the app project, or only running the test suite, does **not** produce the daemon exe (it's a standalone project the app launches as a separate process).

### "No Tablet Detected" even though my tablet is plugged in

1. Make sure the daemon is running (check the OTD Daemon card).
2. Wait a few seconds — the app polls for changes every 3 seconds.
3. Click the refresh icon to force an immediate check.

### Build fails with "file is locked by OpenTabletArtist"

Close the running app first. If a previous instance hasn't fully exited, it may still hold the .exe. We have an open investigation item in `docs/FUTURES.md` to make shutdown cleaner.
