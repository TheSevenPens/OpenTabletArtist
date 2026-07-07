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

The sidebar's top-level items are **Home**, **Tablets**, **Profiles** (with **Per-App Profiles** nested under it), **Hotkeys**, **Scribble**, and **About**. A collapsible **Advanced** group holds **OpenTabletDriver** (a hub with its own secondary tabs — **Daemon**, **Windows Ink Plugin**, **Custom Tablet Compatibility**, **Diagnostics**, **Log**, and **Plugins**), **VMulti Driver**, **Driver Cleanup**, **Startup**, **Developer**, and **Theme**.

**Tablets** is an always-expanded node: every tablet that's paired or currently connected appears as a child (with a status dot), ordered detected-first. Clicking a child opens that tablet's settings **in the right-hand pane** (no dialog); right-click a child (or use the button on its page) to **Forget** it. Clicking the **Tablets** header shows an overview / empty-state ("No tablets connected or remembered").

### Home

The landing page stays quiet when everything is healthy and surfaces things only when they need attention:

- **Needs attention** *(only when there's something to fix)* — the health-check list, worst problem first. Each card explains the issue and, where there's an in-app fix, has a **Fix** button that either performs the action in place or jumps to the page that owns it. It covers things like a missing Windows Ink plugin or VMulti driver, a detected tablet not using a Windows Ink output mode, a tablet whose display mapping isn't a clean single monitor (off-screen or custom), a conflicting manufacturer driver, running as administrator, and using an external (not app-owned) daemon.
- **Not connected to daemon** *(only when there's a daemon problem)* — a single card that appears when the app isn't connected to the daemon (or a start/connect attempt is failing or stalled). It has a **Fix** button (start + connect) and an **Open daemon page** button. Pressing **Fix** morphs the card into a "Connecting to daemon…" state; on success it disappears, and on failure it reverts to the problem. In the normal connected state the daemon isn't mentioned on Home at all — its full status and controls live on the **Daemon** page (Advanced → OpenTabletDriver).
- **Your tablets** — every paired or currently-connected tablet as a card (status dot, last-seen, and specs when known) with a **Settings** button that opens its per-tablet page. An explicit "No tablets connected or remembered" state shows when there are none.
- **Supported tablets** — a link out to OpenTabletDriver's full supported-tablets list on the OTD website.

### Tablets (per-tablet settings)

Selecting a tablet in the sidebar opens its settings **in the right-hand pane** (no separate dialog), with a header showing the tablet name, live connection status, a **Refresh**, and a **Forget** button. The settings are organized into tabs — **About**, **Pen Behavior**, **Pen Inputs**, **Pen Dynamics**, **Display Mapping**, **Active Area**, **Calibration**, **Buttons**, and **Wheels** (plus **Filters** and **JSON**, which are hidden unless enabled on the Developer page):

- **About** — The first tab: a read-out of the tablet's specifications (name, active area in mm & inches, its diagonal and aspect ratio, digitizer resolution in LP/mm and LPI, pressure levels, pen/express/mouse button counts, touch ring/strip/touch support, and the USB VID:PID), shown for the connected tablet. It also has a **Resources** area (currently a link to OpenTabletDriver's supported-tablets list) that will grow to hold documentation and compatible-pen info over time.
- **Pen Behavior** — The tablet's **output mode**, an **Absolute / Relative** toggle. Absolute is recommended for drawing since it carries pressure & tilt; a warning + **Fix** appears when the profile is on a non-Windows-Ink mode.
- **Pen Inputs** — All the switches on the pen — tip, eraser, and barrel buttons — laid out over a diagram of the pen. Each shows a green check + "Adaptive Binding (recommended)" when already on Adaptive Binding; otherwise a Fix (tip/eraser) or Fix All (pen buttons) button sets it.
- **Pen Dynamics** — An interactive pressure-curve editor **plus smoothing**. Toggle it on to apply custom pen dynamics to this tablet's profile; they're enforced by the bundled *OpenTabletArtist – Pen Dynamics* filter, so they affect **every** app (Krita, Clip Studio Paint, Photoshop, …), not just one.
  - **Curve** — drag the pink **min** node and cyan **max** node to set where pressure starts and saturates (input → output); the **Min/Max node** input/output values are shown read-only beside the chart. Use the **Softness** slider to bend the response (positive = lighter/concave, negative = firmer/convex; the ↺ button resets it to linear), and tick **Cut below input minimum** to turn the lead-in into a dead zone instead of a pressure floor. **Presets** (Linear / Soft / Firm) are quick starting points; **Reset** restores the identity curve. While you draw, a green dot tracks your **live pen pressure** on the curve so you can feel the mapping.
  - **Smoothing (jitter reduction)** — **Position** smoothing steadies wobbly lines and **Pressure** smoothing evens out pressure jitter (each 0 = off to 1 = max; the amount is perceptually scaled, like Slimy Scylla, so the slider feels even across its range). **Order** chooses whether smoothing runs after the curve (*Curve → Smooth*, default) or before it. Smoothing applies while the pen is down and resets each time it lifts, so strokes start crisp with no carry-over from the previous one.
  - **Reset all** (in the tab header) returns the curve, both smoothing amounts, and the order to their defaults in one click (it leaves the On/Off toggle as you set it). The curve's own **Reset** button only resets the curve.
  - Edits are debounced and applied to the daemon automatically.
- **Display Mapping** — Maps the tablet to a monitor. A diagram shows the whole picture in one view: your monitors across the top (to scale/position, numbered, with resolution + refresh), the **tablet's active area** below, and **red corner-to-corner lines** joining the active area to the selected display (Wacom-style, so the 1:1 correspondence is obvious). **Click a monitor** to select it, then **Apply mapping** to map the tablet to that whole display (aspect-locked); a "Display selection changed — click Apply mapping to save it" hint makes clear the choice isn't live until applied. If the stored mapping isn't a clean single display, the tab flags it — a **warning** when part of the area falls off-screen (the pen would reach dead zones) or a **note** for a custom/multi-display area — and Apply mapping fixes it. **Display Settings** opens Windows Display Settings and **Refresh** re-reads monitors (the diagram also updates when you add/remove a display).
- **Active Area** — A picture of the region of the tablet that's currently mapped to the display (the effective area inside the full digitizer), with usage stats — percentage of the tablet used, the effective and full sizes, and the diagonal. A **Millimeters / Inches** toggle switches every length between metric and imperial.
- **Calibration** — *(Absolute mode)* Three cards, one per density — **4 point**, **9 point**, and **25 point** — each with a diagram of the point grid on a screen, a note on when to use it, and a **Start** button. The currently-active calibration is marked with an accent border and an **"In use"** badge. Starting one opens a full-screen overlay on the mapped display where you tap the targets so the cursor lands where you see the nib.
  - **4 point** — the standard choice; tap the four corners. Fits a **perspective** correction (keystone/parallax + offset/scale/rotation).
  - **9 point** (3×3 grid) for harder cases and **25 point** (5×5 grid) for extreme cases both fit a **per-node** correction that also handles **localized** distortion. The correction is tied to the current mapping — recalibrate if you change it.
- **Buttons** — The tablet's auxiliary buttons, **fully editable**, one card each. Pick a binding **type** (None / Keyboard / Mouse button / Mouse scroll): Keyboard offers Ctrl/Shift/Alt modifiers + a key (a combo writes a Multi-Key binding); Mouse button is Left/Right/Middle/Back/Forward; Mouse scroll is a direction. **Pressing a physical button highlights its card live.** A **Buttons enabled** master toggle suspends all mappings (kept and restored when re-enabled), and **Clear all** removes every binding.
- **Wheels** — Bindings for the tablet's wheel / dial controls, on hardware that has them.
- **Filters** *(developer-only)* — The profile's input filters, one card each: friendly name, full type path, and enabled/disabled status. A stale filter left over from an older app name is flagged **Legacy** (and cleaned up automatically). Hidden unless enabled on the Developer page.
- **JSON** *(developer-only)* — Raw JSON view of the profile data. Hidden unless enabled on the Developer page.

A **Refresh** button in the page header reloads settings from the daemon (useful after making changes in the OTD UX).

### Profiles

At the top, a pinned **Current settings** card ("In use now") represents the configuration your tablet is using right now — the live `settings.json` you edit on the Tablet pages. It's what per-app and hotkey switches revert to, and every **profile** below is a *saved copy* of it.

A **profile** is a named backup of your entire OTD configuration (all tablets' settings). Cards show the profile name and file last-modified time, sorted newest first. Profiles are what hotkeys and per-app switching apply.

> Note: "profile" here means a whole-configuration backup saved by OpenTabletArtist — not OpenTabletDriver's own per-tablet `Profile`. Each OTA profile file contains the complete OTD `Settings` (all tablets).

On the Current settings card:

- **Save as profile** — Saves the current settings as a copy with an auto-numbered name: `Profile`, `Profile 2`, `Profile 3`, ... (lowest available number is reused if you delete one). Rename freely after saving.
- **Browse** — Opens the profiles folder in Explorer.

Each profile card has:

- **Load** — Applies the profile and makes it your **Current settings** (this is a permanent switch — see [Switching profiles](#switching-profiles)).
- **Update** — Overwrites the profile with the current settings.
- **Rename** — Prompts for a new name (simple text dialog, no file picker).
- **Delete** — Removes the profile file after a confirmation prompt.

The "No profiles" empty state appears only when the profiles folder is actually empty.

### Switching profiles

There are several ways to change the active configuration, split by whether the change is **permanent** (rewrites your Current settings) or **temporary** (a live override that's restored later).

Whole-profile switches:

| How | Where | Effect |
|---|---|---|
| **Load** a profile | Profiles page | **Permanent** — applies it and makes it your Current settings. |
| **Profile hotkey** | Hotkeys page | **Temporary** — a global keyboard shortcut applies a profile as a live override; a "Profile override" chip shows while active. Your Current settings are untouched. |
| **Per-app switching** | Per-App Profiles page | **Automatic + temporary** — the mapped profile is applied when its app comes to the foreground; unmapped apps fall back to your Current settings (or a profile you choose). An "App profile" chip shows the active one. Restored on disable/exit. |

Monitor-mapping switches (change only which monitor the active profile maps to — all **permanent**):

- **Cycle mapped monitor** hotkey (Hotkeys page).
- **Switch Display** in the system-tray menu.
- The display picker on a tablet's page.

Notes:

- Only **Load** is a permanent whole-profile switch; the hotkey and per-app methods are temporary and leave your Current settings intact.
- Per-app switching deliberately **does not** change the monitor mapping — set that once (tablet page / cycle-monitor hotkey) and it sticks across per-app switches.
- There's currently no one-click "back to Current settings" button for a hotkey-driven override — it clears when you switch again (per-app overrides restore automatically on disable/exit).

### Hotkeys

Global keyboard shortcuts that work even when OpenTabletArtist isn't focused. Assign a combination (a modifier — Ctrl / Alt / Shift / Win — plus a letter, digit, or F-key) with the on-screen picker, and it takes effect system-wide.

- **Cycle mapped monitor** — moves the active tablet's area to the next monitor (wrapping around). Shows a toast with the new monitor; no-ops (with a toast) if you only have one display or no tablet is active.
- **Profile switching** — assign a hotkey to a profile to switch to it instantly. The switch is a live-only override (your saved default isn't overwritten); a "Profile override" chip shows while one is active.

### Per-App Profiles

Automatically applies a profile when the foreground application changes — the way Wacom/XP-Pen/Huion drivers do. Map an app to a profile, and switching to that app reconfigures the tablet. (Nested under **Profiles** in the sidebar.)

To use it: create the profiles you want first (Profiles page), then here **Add app…** to map a running application to a profile. There's no separate on/off — switching turns on automatically as soon as at least one app is mapped to a saved profile (mapping an app to **Current settings** is a no-op and doesn't arm it). Use **Default for apps** to choose what unmapped apps get — your **Current settings** (default) or a specific profile. An **App profile** chip in the main window shows which profile is currently applied.

Caveats:

- **Switches are temporary.** They're applied live to the daemon only — your Current settings (`settings.json`) are never overwritten, the editor keeps showing/editing them, and they're restored when switching turns off (no app is mapped to a profile) or you quit the app.
- **It only works while OpenTabletArtist is running** (including minimized to the tray). There's no switching when the app is closed.
- **Profiles are whole-configuration.** A profile is your entire OTD `Settings`, so a per-app switch affects *all* tablets, not just one — set up profiles with that in mind if you have multiple tablets.
- **The monitor mapping is left alone.** A per-app switch does *not* change which monitor the tablet points at — only the current monitor mapping is kept (moving an app between displays won't yank the tablet to a stale monitor). Set the monitor from the tablet page or with the *Cycle mapped monitor* hotkey; that choice sticks across per-app switches.
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

### Scribble

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

The full daemon status and controls (this moved off Home, which now shows the daemon only when there's a problem). Connection status with **Start** when disconnected, **Restart** / **Stop** when running, and a **Refresh** to check status. The **Start / Stop / Restart** actions show an inline progress bar with live phase text (Stopping… → Starting… → Connecting…) while they run, and report a clear error if the daemon doesn't come online within 30 seconds.

Below the "Daemon running" line, a **daemon ownership indicator** shows which daemon the app is actually connected to (one of three states):

- **App-owned daemon** (green check) — connected to this project's build under `external/OpenTabletDriver/OpenTabletDriver.Daemon/bin/`.
- **External daemon (not app-owned)** (amber warning) — connected to a different OTD instance, e.g. a separately-installed daemon the user already had running. Hint text suggests clicking **Restart**, which kills any running daemon and relaunches this project's build.
- **Daemon source unknown** (grey) — connected, but the daemon's exe path couldn't be read (e.g. it's running elevated). The app reports this honestly rather than guessing.

Ownership is detected by resolving the process on the other end of the named pipe (`GetNamedPipeServerProcessId`) and comparing its exe path to the project's daemon build. The actual daemon exe path is shown on hover (app-owned / external states). The page also shows the embedded OTD version and an **OTD UX** launcher to open the original OpenTabletDriver interface for comparison.

### Windows Ink Plugin

Manages the third-party Windows Ink output-mode plugin (from Kuuuube's VoiDPlugins), which delivers pen pressure and tilt to your apps. Shows:

- **Install status** — a green dot + "Installed" (with the **plugin version** as a chip next to the name) or a grey dot + "Not installed."
- **Output mode** — whether the active profile actually uses a Windows Ink mode ("Plugin active" / "Not configured").
- **Supported driver vs OTD** — the plugin's declared supported driver version alongside the running OTD version. A warning indicator appears if the installed plugin doesn't declare support for the current OTD version (per OTD's own compatibility rule).
- **Buttons** — **Install** (when not installed); **Check for Update** (when installed) which queries the official OTD Plugin-Repository — if a newer plugin version is found the button becomes **Install Update (vX)**, otherwise it reports "Up to date"; **Uninstall**; and a **Refresh** icon (top-right) that re-reads the installed plugin and re-checks the repository in one step. Install/update/uninstall are driven through the daemon's plugin RPC; the card updates its status as soon as each operation completes.

### VMulti Driver

VMulti is the virtual pen device the Windows Ink plugin injects pressure and tilt through. Detection runs via both Setup API and HID enumeration. Has **Install** / **Uninstall** wizards, **Refresh** to re-check, and **Browse** to open the driver folder. Both **Install** and **Uninstall** run in-app (one UAC prompt each, no flashing cmd window) and offer to **restart** Windows afterward. Install creates the VMulti device via `devcon`; Uninstall removes the driver and the active device *and* cleans up the leftover driverless `djpnewton\vmulti` nodes (Device Manager Code 28) that the stock removal left behind. Detection reflects a *working* driver, so any remaining driverless leftovers are reported as **Not installed**, not as installed.

### Startup

A single toggle — **Start OpenTabletArtist when Windows starts** — that launches the app minimized to the tray at sign-in, so hotkeys and per-app profile switching are ready without opening it yourself (per-user Run key; Windows only).

### Developer

Testing aids, not needed for normal use. **Induce health warnings** adds a synthetic *Needs attention* card at each severity (for reviewing the card styling), and **Trigger actual health warnings** forces each real health check to appear with its true text and Fix button, so the warnings can be reviewed and screenshotted without reproducing the underlying problem. Right-clicking one of these induced cards on Home offers a hidden **Dismiss** (real warnings can't be dismissed this way). A **Tablet page tabs** section reveals the otherwise-hidden **Filters** and **JSON** tabs on a tablet's page.

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

### "Not connected to daemon" on Home

1. Click **Fix** on the "Not connected to daemon" card — it starts the daemon (built from the submodule) and connects, morphing into a "Connecting to daemon…" state while it works.
2. If that doesn't resolve it, click **Open daemon page** (Advanced → OpenTabletDriver → Daemon) for the full controls — **Start**, **Restart**, and a **Refresh** to re-check the connection.
3. The daemon auto-starts on app launch — if it didn't, check whether another OTD instance is already running.

If the daemon page reports that **OpenTabletDriver.Daemon.exe wasn't found**, the daemon exe was never built. The app checks for it before every connection attempt and says so plainly instead of silently timing out. Build the whole solution so the daemon is produced:

```bash
dotnet build OpenTabletArtist.slnx
```

Building only the app project, or only running the test suite, does **not** produce the daemon exe (it's a standalone project the app launches as a separate process).

### "No Tablet Detected" even though my tablet is plugged in

1. Make sure the daemon is running (Advanced → OpenTabletDriver → Daemon).
2. Wait a few seconds — the app reconciles with the daemon every 30 seconds (and immediately on connect).
3. Click the refresh icon to force an immediate check.

### Build fails with "file is locked by OpenTabletArtist"

Close the running app first. If a previous instance hasn't fully exited, it may still hold the .exe. We have an open investigation item in `docs/FUTURES.md` to make shutdown cleaner.
