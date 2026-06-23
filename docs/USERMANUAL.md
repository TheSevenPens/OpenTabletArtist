# User Manual

## Prerequisites

- **.NET 10 SDK** — for building and running the app
- OpenTabletDriver is included as a submodule — no separate install needed

## Quick Start

```bash
git clone --recursive https://github.com/TheSevenPens/OTDWindowsHelper.git
cd OTDWindowsHelper
dotnet build OTDWindowsHelper.slnx   # builds the app AND the OTD daemon from the submodule
dotnet run --project wpf
```

> Build the **solution** (`.slnx`), not just `wpf/`. The daemon (`OpenTabletDriver.Daemon.exe`) is a separate project built from the submodule; if you build only the app it won't exist and the app will sit at "Not connected".

On launch the app auto-starts the daemon if it isn't already running, then connects.

## Using the Interface

The sidebar shows: **Dashboard**, **Paired Tablets**, **Saved Settings**, **Custom Tablet Configs**, **Utilities**, **Diagnostics**, **About**.

### Dashboard

The landing page shows status cards:

- **OpenTabletDriver Version** — Shows the OTD version from the referenced assembly.
- **OpenTabletDriver Daemon** — Connection status. Shows **Start** button when disconnected, **Restart** to restart the daemon, **Refresh** icon to check status, and **OTD UX** to launch the original OTD interface for comparison. Below the "Daemon running" line, a **daemon ownership indicator** shows which daemon the app is actually connected to (one of three states):
  - **App-owned daemon** (green check) — connected to this project's build under `external/OpenTabletDriver/OpenTabletDriver.Daemon/bin/`.
  - **External daemon (not app-owned)** (amber warning) — connected to a different OTD instance, e.g. a separately-installed daemon the user already had running. Hint text suggests clicking **Restart**, which kills any running daemon and relaunches this project's build.
  - **Daemon source unknown** (grey) — connected, but the daemon's exe path couldn't be read (e.g. it's running elevated). The app reports this honestly rather than guessing.

  Ownership is detected by resolving the process on the other end of the named pipe (`GetNamedPipeServerProcessId`) and comparing its exe path to the project's daemon build. The actual daemon exe path is shown on hover (app-owned / external states).
- **Tablet** — Detected tablet name, or "No tablet detected." Has an **Open** button to jump to the tablet's settings dialog. Updates automatically via polling.
- **VMulti Driver** — Detection via both Setup API and HID enumeration. Has **Install** / **Uninstall** wizards, **Refresh** to re-check, and **Browse** to open the driver folder.
- **Kuuube's Windows Ink plugin** — Manages the third-party Windows Ink output-mode plugin (from Kuuuube's VoiDPlugins). Shows:
  - **Install status** — a green dot + "Installed" (with the **plugin version** as a chip next to the name) or a grey dot + "Not installed."
  - **Output mode** — whether the active profile actually uses a Windows Ink mode ("Plugin active" / "Not configured").
  - **Supported driver vs OTD** — the plugin's declared supported driver version alongside the running OTD version. A warning indicator appears if the installed plugin doesn't declare support for the current OTD version (per OTD's own compatibility rule).
  - **Buttons** — **Install** (when not installed); **Check for Update** (when installed) which queries the official OTD Plugin-Repository — if a newer plugin version is found the button becomes **Install Update (vX)**, otherwise it reports "Up to date"; **Uninstall**; and a **Refresh** icon (top-right) that re-reads the installed plugin and re-checks the repository in one step. Install/update/uninstall are driven through the daemon's plugin RPC; the card updates its status as soon as each operation completes.

The **Start / Stop / Restart** daemon actions show an inline progress bar with live phase text (Stopping… → Starting… → Connecting…) while they run, and report a clear error if the daemon doesn't come online within 30 seconds.

### Paired Tablets

Lists all tablet profiles. Click **Open** or double-click a card to open the settings dialog.

The settings dialog has five tabs:

- **Screen Mapping** — Output mode selection (Windows Ink Absolute / Relative via radio buttons, with warning + Fix if using a non-Windows Ink mode). Display selection as radio buttons including "All displays" for multi-monitor setups. Selecting a display immediately maps the tablet to it with aspect ratio lock enforced.
- **Pen Tip & Eraser** — Current tip and eraser bindings with Fix buttons to set Adaptive Binding (recommended for creatives).
- **Pen Buttons** — Pen and auxiliary button bindings with Fix button to set all to Adaptive Binding.
- **Filters** — Configured input filters with enabled/disabled status.
- **JSON** — Raw JSON view of the profile data.

A **Refresh** button in the dialog header reloads settings from the daemon (useful after making changes in the OTD UX).

### Saved Settings

Saved copies of your entire OTD configuration. Cards show the snapshot name and file last-modified time, sorted newest first.

Toolbar:

- **Save Snapshot** — Saves current settings with an auto-numbered name: `Snapshot`, `Snapshot 2`, `Snapshot 3`, ... (lowest available number is reused if you delete one). Rename freely after saving.
- **Browse** — Opens the snapshots folder in Explorer.

Each snapshot card has:

- **Load** — Applies the snapshot's settings to the daemon.
- **Update** — Overwrites the snapshot with the current settings.
- **Rename** — Prompts for a new name (simple text dialog, no file picker).
- **Delete** — Removes the snapshot file after a confirmation prompt.

The "No Snapshots" empty state appears only when the snapshots folder is actually empty.

### Custom Tablet Configs

Lists tablet config JSON files in `%AppData%\OpenTabletDriver\Configurations\` (the folder is created on app startup if missing). Each row shows the tablet's friendly name (read from the JSON `Name` field, falling back to a manufacturer-folder + filename combo). Per-row **View** opens the formatted JSON in a read-only viewer; **Delete** removes the file after a confirmation prompt. The panel header has a **Refresh** icon to rescan and an **Open Folder** button.

### Utilities

Helper tools for diagnosing and fixing tablet-driver problems.

- **TabletDriverCleanup** — Manages the [TabletDriverCleanup](https://github.com/OpenTabletDriver/TabletDriverCleanup) tool by the OTD team that removes leftover bits from previous manufacturer tablet drivers (Wacom, Huion, XP-Pen, etc.). Install the tool first via **Install** (downloads the latest release to `%LocalAppData%\TabletDriverCleanup`, no admin required); then **Run** launches it with a UAC prompt and a visible terminal so the cleanup output is readable. **Browse** opens the install folder; **Uninstall** removes it.

### Diagnostics

Live tablet input visualization. See `docs/DIAGNOSTICS.md` for details.

### About

Project information.

## Navigation

Click items in the sidebar to switch between pages. The active page is highlighted with an indigo accent bar.

## Stopping the daemon from outside this app

The OTD daemon is a separate process and keeps running after our app's window closes. Quick options for stopping it:

- **Use the OTD UX**: Click **OTD UX** on the Dashboard's daemon card to launch `OpenTabletDriver.UX.Wpf.exe`, which has its own system tray icon with quit/show controls.
- **Use Task Manager**: `Ctrl+Shift+Esc`, find `OpenTabletDriver.Daemon.exe` in the Processes tab, right-click → End task.

A future improvement may add a system tray icon to our own app — see `docs/FUTURES.md`.

## Troubleshooting

### "Not connected" on the OpenTabletDriver Daemon card

1. Click **Start** to launch the daemon (built from the submodule).
2. Click the refresh icon to check the connection.
3. The daemon auto-starts on app launch — if it didn't, check if another OTD instance is already running.

### "No Tablet Detected" even though my tablet is plugged in

1. Make sure the daemon is running (check the OTD Daemon card).
2. Wait a few seconds — the app polls for changes every 3 seconds.
3. Click the refresh icon to force an immediate check.

### Build fails with "file is locked by OtdWindowsHelper"

Close the running app first. If a previous instance hasn't fully exited, it may still hold the .exe. We have an open investigation item in `docs/FUTURES.md` to make shutdown cleaner.
