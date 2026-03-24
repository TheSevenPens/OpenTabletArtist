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

### Dashboard

The landing page shows status cards:

- **OpenTabletDriver Version** — Shows the OTD version from the referenced assembly.
- **OpenTabletDriver Daemon** — Connection status. Shows **Start** button when disconnected, **Restart** to restart the daemon, **Refresh** icon to check status, and **OTD UX** to launch the original OTD interface for comparison.
- **Tablet** — Detected tablet name, or "No tablet detected." Has an **Open** button to jump to the tablet's settings dialog. Updates automatically via polling.
- **VMulti Driver** — Detection via both Setup API and HID enumeration. Has **Install** / **Uninstall** wizards, **Refresh** to re-check, and **Browse** to open the driver folder.
- **Windows Ink** — Whether the active output mode uses the Windows Ink plugin.

### Tablet Settings

Lists all tablet profiles. Click **Open** or double-click a card to open the settings dialog.

The settings dialog has five tabs:

- **Screen Mapping** — Output mode selection (Windows Ink Absolute / Relative via radio buttons, with warning + Fix if using a non-Windows Ink mode). Display selection as radio buttons including "All displays" for multi-monitor setups. Selecting a display immediately maps the tablet to it with aspect ratio lock enforced.
- **Pen Tip & Eraser** — Current tip and eraser bindings with Fix buttons to set Adaptive Binding (recommended for creatives).
- **Pen Buttons** — Pen and auxiliary button bindings with Fix button to set all to Adaptive Binding.
- **Filters** — Configured input filters with enabled/disabled status.
- **JSON** — Raw JSON view of the profile data.

A **Refresh** button in the dialog header reloads settings from the daemon (useful after making changes in the OTD UX).

### Settings Snapshots

Snapshots are saved copies of your entire OTD configuration.

- **Save Snapshot** — Saves current settings with an auto-generated timestamp name (YYYY_MM_DD_HH_MM_SS).
- **Browse** — Opens the snapshots folder in Explorer.

Each snapshot card has:

- **Load** — Applies the snapshot's settings to the daemon.
- **Update** — Overwrites the snapshot with the current settings.
- **Rename** — Prompts for a new name.
- **Delete** — Removes the snapshot file.

### Console (Placeholder)

Will show live log output from the OTD daemon. Not yet implemented.

### About

Project information.

## Navigation

Click items in the sidebar to switch between pages. The active page is highlighted with an indigo accent bar.

## Troubleshooting

### "Not connected" on the OpenTabletDriver Daemon card

1. Click **Start** to launch the daemon (built from the submodule).
2. Click the refresh icon to check the connection.
3. The daemon auto-starts on app launch — if it didn't, check if another OTD instance is already running.

### "No Tablet Detected" even though my tablet is plugged in

1. Make sure the daemon is running (check the OTD Daemon card).
2. Wait a few seconds — the app polls for changes every 3 seconds.
3. Click the refresh icon to force an immediate check.
