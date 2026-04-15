# User Manual

## Prerequisites

- **.NET 10 SDK** — for building and running the app
- OpenTabletDriver is included as a submodule — no separate install needed

## Quick Start

```bash
git clone --recursive https://github.com/TheSevenPens/TabletDriverUXPrototype.git
cd TabletDriverUXPrototype/wpf
dotnet run
```

The app will build the OTD daemon from the submodule, auto-start it if not running, and connect.

## Using the Interface

The sidebar shows: **Dashboard**, **Tablet Settings**, **Saved Settings**, **Diagnostics**, **About**.

### Dashboard

The landing page shows status cards:

- **OpenTabletDriver Version** — Shows the OTD version from the referenced assembly.
- **OpenTabletDriver Daemon** — Connection status. Shows **Start** button when disconnected, **Restart** to restart the daemon, **Refresh** icon to check status, and **OTD UX** to launch the original OTD interface for comparison.
- **Tablet** — Detected tablet name, or "No tablet detected." Has an **Open** button to jump to the tablet's settings dialog. Updates automatically via polling.
- **VMulti Driver** — Detection via both Setup API and HID enumeration. Has **Install** / **Uninstall** wizards, **Refresh** to re-check, and **Browse** to open the driver folder.
- **TabletDriverCleanup** — Manages the [TabletDriverCleanup](https://github.com/OpenTabletDriver/TabletDriverCleanup) tool by the OTD team that removes leftover bits from previous manufacturer tablet drivers (Wacom, Huion, XP-Pen, etc.). User explicitly installs the tool first via **Install** (downloads the latest release to `%LocalAppData%\TabletDriverCleanup`, no admin required); then **Run** launches it with a UAC prompt and a visible terminal so the cleanup output is readable. **Browse** opens the install folder; **Uninstall** removes it.
- **Windows Ink** — Whether the active output mode uses the Windows Ink plugin.
- **Tablet Configurations** — Lists tablet config JSON files in `%AppData%\OpenTabletDriver\Configurations\` (the folder is created on app startup if missing). Each row shows the tablet's friendly name (read from the JSON `Name` field, falling back to a manufacturer-folder + filename combo). Per-row **View** opens the formatted JSON in a read-only viewer; **Delete** removes the file after a confirmation prompt. The panel header has a **Refresh** icon to rescan and an **Open Folder** button.

### Tablet Settings

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

### Build fails with "file is locked by TabletDriverUX"

Close the running app first. If a previous instance hasn't fully exited, it may still hold the .exe. We have an open investigation item in `docs/FUTURES.md` to make shutdown cleaner.
