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

The landing page. Shows your connected tablet's name, specifications (active area dimensions, pressure levels, pen button count), and current output mode. If no tablet is connected, you'll see an empty state prompting you to connect one.

Quick action links at the bottom take you directly to Area Mapping or Bindings.

### Area Mapping

The core configuration view. Displays a side-by-side visualization of your display area and tablet area:

- **Blue rectangle** (left): Your display area — the region of screen the tablet maps to.
- **Green rectangle** (right): Your tablet's active area — the physical region of the tablet that is mapped.
- **Dashed outline**: The full tablet surface.
- **Dashed arrow**: Indicates the mapping relationship between the two areas.

The right panel shows numeric controls for each area (width, height, center X/Y, rotation) and toggle options:

- **Enable clipping**: Prevents the cursor from leaving the mapped display area.
- **Limit to tablet area**: Constrains the active area to the tablet's physical boundaries.
- **Lock aspect ratio**: Maintains proportional width/height when resizing.

### Bindings (Placeholder)

Will display pen tip, eraser, barrel button, and auxiliary button configuration. Not yet implemented.

### Filters (Placeholder)

Will display the input processing pipeline — smoothing, anti-chatter, and other filter plugins. Not yet implemented.

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

The app reads `localStorage` and `prefers-color-scheme` on startup. If neither is set, it defaults to dark mode. Click the theme toggle to switch.

## Development Workflow

The frontend uses Vite's hot module replacement. When you edit any `.svelte`, `.ts`, or `.css` file in `frontend/src/`, the browser updates instantly without a full reload. This is the intended workflow for iterating on the UI.

To test with real tablet data, run all three components:
1. OTD Daemon
2. Bridge (`dotnet run` in `bridge/`)
3. Frontend (`npm run dev` in `frontend/`)

To work on the UI without a daemon, just run the frontend alone — the Dashboard shows the empty state, and the Area Mapping page renders with demo data.
