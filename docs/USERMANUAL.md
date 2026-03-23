# User Manual

## Prerequisites

- **Node.js** (v18+) — for running the frontend dev server
- **.NET 8 SDK** — for building and running the bridge
- **OpenTabletDriver** — the daemon must be installed and running

## Quick Start

### 1. Start the OTD Daemon

Make sure `OpenTabletDriver.Daemon.exe` is running. If you use OTD normally, the daemon is likely already running in the background. If not, start it manually:

```
OpenTabletDriver.Daemon.exe
```

### 2. Start the Bridge

```bash
cd bridge
dotnet run
```

The bridge will connect to the OTD daemon via named pipe and start serving on `http://localhost:5000`. You should see:

```
Connecting to OTD daemon on pipe 'OpenTabletDriver.Daemon'...
Connected to OTD daemon
```

If the daemon is not running, the bridge will retry every 3 seconds until it connects.

### 3. Start the Frontend

```bash
cd frontend
npm install    # first time only
npm run dev
```

Open `http://localhost:5173` in your browser.

## Using the Interface

### Dashboard

The landing page. Shows four status cards in a vertical column, each reflecting live state from the OTD daemon:

- **OpenTabletDriver** — whether the bridge is connected to the daemon (green = running, gray = not connected)
- **Tablet** — the detected tablet name, or "No tablet detected" if none is plugged in
- **VMulti Driver** — whether the vmulti virtual HID driver is installed (required for pressure and tilt on Windows). Detected via HID device enumeration (VID `0x00FF` / PID `0xBACC`).
- **Windows Ink** — whether the active output mode uses the Windows Ink plugin (detected from the profile's output mode path). Shows "Plugin active" when the profile uses `WinInkAbsoluteMode`.

When a tablet is connected, additional cards appear below: tablet specifications, current output mode, and quick action links to the tablet's Area Mapping and Bindings pages.

### Tablet Settings

Lists all tablet configurations stored in the OTD daemon's settings. Each card shows the tablet name and active output mode. A "Forget" button (✕) appears on hover in the top-right corner of each card — clicking it removes that tablet's settings from the daemon (with confirmation). Clicking a card opens the tablet's detail view with three sub-tabs:

- **Area Mapping** — Side-by-side visualization of display and tablet areas with numeric controls for width, height, X, Y. "Force proportions" toggle available. Advanced settings (rotation, clipping, area limiting) are hidden by default.
- **Bindings** — Shows pen tip, eraser, pen buttons, and auxiliary button bindings with their assigned actions and activation thresholds. Data pulled live from the daemon.
- **Filters** — Lists configured input filters (smoothing, anti-chatter, etc.) with enabled/disabled status. Shows empty state if no filters are configured.

A back button in the tablet detail header returns to the tablet list.

### Settings Snapshots

Snapshots are saved copies of your entire OTD configuration (all tablet settings, bindings, and filters). Each snapshot card shows the name and number of tablet profiles it contains, with three actions:

- **Load** — Applies the snapshot's settings to the daemon, replacing the current configuration.
- **Open** — Opens a dialog with General and JSON tabs showing the snapshot's contents.
- **Delete** — Removes the snapshot file (with confirmation).

Use the **Save Snapshot** button to capture the current configuration. The **Open Folder** button opens the snapshots directory in your file manager.

### Console (Placeholder)

Will show live log output streamed from the OTD daemon. Not yet implemented.

### About

Shows project information and the system architecture stack.

### Theme Toggle

Click the sun/moon icon in the bottom-right corner of the status bar to switch between dark and light themes. Your preference is saved to localStorage and persists across sessions. On first visit, the app respects your system's color scheme preference.

### Status Bar

The bar at the bottom of the window shows:

- **Left**: Connection status (green dot = connected to bridge, yellow pulsing = connecting, gray = disconnected)
- **Center**: Name of the connected tablet, or "No tablet connected"
- **Right**: Theme toggle button

## Troubleshooting

### "No Tablet Detected" even though my tablet is plugged in

1. Make sure `OpenTabletDriver.Daemon.exe` is running.
2. Make sure the bridge (`dotnet run` in `bridge/`) is running and shows "Connected to OTD daemon."
3. Check that the daemon detects your tablet — the OTD daemon logs will show tablet detection.

### Bridge shows "OTD daemon not found, retrying..."

The daemon is not running or is not reachable. Start `OpenTabletDriver.Daemon.exe` and the bridge will auto-connect.

### Blank page in the browser

Check the browser console (F12 > Console) for JavaScript errors. Make sure you ran `npm install` in the `frontend/` directory before starting the dev server.

### Theme doesn't apply on first load

The app reads `localStorage` and `prefers-color-scheme` on startup. If neither is set, it defaults to light mode. Click the theme toggle to switch.

## Development Workflow

The frontend uses Vite's hot module replacement. When you edit any `.svelte`, `.ts`, or `.css` file in `frontend/src/`, the browser updates instantly without a full reload. This is the intended workflow for iterating on the UI.

To test with real tablet data, run all three components:
1. OTD Daemon
2. Bridge (`dotnet run` in `bridge/`)
3. Frontend (`npm run dev` in `frontend/`)

To work on the UI without a daemon, just run the frontend alone — the Dashboard shows the empty state, and the Area Mapping page renders with demo data.
