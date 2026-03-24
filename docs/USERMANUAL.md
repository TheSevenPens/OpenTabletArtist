# User Manual

## Prerequisites

- **.NET 10 SDK** — for building and running the app
- **OpenTabletDriver** — installed on your system

## Quick Start

```bash
cd wpf
dotnet run
```

The app will launch and attempt to connect to the OTD daemon automatically.

## Using the Interface

### Dashboard

The landing page shows status cards:

- **OTD Install Location** — Set the path to your OpenTabletDriver installation. Click **Browse** to select the folder. This is persisted across sessions and used to start/restart the daemon.
- **OpenTabletDriver** — Connection status to the daemon. Shows **Start** button when disconnected (if install path is set), **Restart** to restart the daemon, and a **Refresh** icon to manually check the connection.
- **Tablet** — The detected tablet name, or "No tablet detected" if none is plugged in. Updates automatically when you plug in or unplug a tablet.
- **VMulti Driver** — Whether the vmulti virtual HID driver is installed (required for pressure and tilt on Windows). Shows detection results from both Setup API (sees disabled devices) and HID (sees only active devices). Has **Install** / **Uninstall** buttons, a **Refresh** icon to re-check status, and a **Browse** button to open the driver folder.
- **Windows Ink** — Whether the active output mode uses the Windows Ink plugin.

### Tablet Settings

Lists all tablet configurations stored in the OTD daemon's settings. Each card shows the tablet name and active output mode. Click **Open** or double-click a card to open the settings dialog.

The settings dialog has two tabs:

- **General** — Shows output mode (with a **Fix** button if not using Windows Ink), area mapping with display selection radio buttons and a **Set to display** button (automatically enforces aspect ratio lock), bindings, and filters.
- **JSON** — Raw JSON view of the profile data.

### Settings Snapshots

Snapshots are saved copies of your entire OTD configuration. Each snapshot card has:

- **Load** — Applies the snapshot's settings to the daemon.
- **Delete** — Removes the snapshot file.

### Console (Placeholder)

Will show live log output from the OTD daemon. Not yet implemented.

### About

Project information.

## Navigation

Click items in the sidebar to switch between pages. The active page is highlighted with an indigo accent bar.

## Troubleshooting

### "Not connected" on the OpenTabletDriver card

1. Set the OTD install path using the Browse button on the Install Location card.
2. Click **Start** to launch the daemon.
3. Click the refresh icon to check the connection.

### "No Tablet Detected" even though my tablet is plugged in

1. Make sure `OpenTabletDriver.Daemon.exe` is running (check the OTD status card).
2. Wait a few seconds — the app polls for changes every 3 seconds.
3. Click the refresh icon on the OTD card to force an immediate check.

### App shows "Unknown" for tablet name

This was a bug in earlier versions. Make sure you're running the latest build. The tablet name is read from the daemon's `Properties.Name` field.
