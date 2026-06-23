# Futures — Potential Work Items and Directions

## Recently Completed

Several items once listed here have shipped and are no longer "future":

- **Auto-start with the daemon** — the app locates and launches the submodule's `OpenTabletDriver.Daemon.exe` (and verifies it's the app-owned instance).
- **Saved Settings (preset snapshots)** — save / load / rename / delete configuration snapshots (the *Saved Settings* page).
- **Live input visualization** — the *Diagnostics* page streams pen position (area + dot), pressure, and tilt via `Controls/TabletVisualizer`.
- **Settings write-back (initial)** — the per-tablet dialog writes output mode and display mapping back to the daemon via `SetSettings`.
- **Windows Ink + VMulti management** — detect, install, update, and uninstall both from the Dashboard.
- **Daemon lifecycle UX** — Start / Stop / Restart with reliable reconnect and progress feedback.

The items below are still open.

## Near-Term (Polish the Prototype)

### Daemon Visibility & Control From Outside the App
Today, when our app is closed there is no obvious affordance for the user to
know that `OpenTabletDriver.Daemon.exe` is still running, or to stop it.
Options to address this:

1. **System tray icon in our own app** (recommended). Avalonia 12 has built-in
   `TrayIcon` support. Closing the window would minimize to tray instead of
   exiting; the tray icon would reflect daemon status (green/gray) and offer
   right-click actions: Show Window, Restart Daemon, Stop Daemon, Quit. The
   limitation is that the tray icon disappears if the user fully quits us.
2. **Tiny separate "tray controller" process**. A headless app whose only job
   is to live in the tray and surface daemon status / start-stop controls,
   independent of the main UX. Costs an extra binary to ship.
3. **Run the daemon as a Windows Service**. Manageable from `services.msc`.
   Requires admin to install and changes the daemon's lifecycle model — likely
   too invasive for a prototype.
4. **Document the OTD UX as a fallback** (interim). The official
   `OpenTabletDriver.UX.Wpf.exe` (which we already build from our submodule
   and surface via the dashboard's "Launch OTD UX" button) has its own tray
   icon. This works today with no code changes.

### Investigate App Shutdown Cleanliness
When the main window is closed, the app's `MainViewModel.Dispose()` is called via
the `Closed` event. We've observed the `OtdWindowsHelper.exe` file remaining locked
for a few seconds after window close, blocking subsequent rebuilds. Audit:
- Whether `_cts` (CancellationTokenSource) is being signalled in `Dispose()`
- Whether all background polling timers, HTTP requests, and async loops actually
  honor cancellation and exit promptly
- Whether StreamJsonRpc connections are cleanly disposed
- Whether the auto-launched OTD daemon child process behaviour (intentionally
  kept alive across UI sessions) interferes with our own process exit
Goal: window close should result in process exit within ~500ms with no lingering
file locks.

### Interactive Area Mapper
The area mapping SVG visualization currently displays static rectangles. Make them interactive:
- Drag to reposition the tablet active area within the full tablet surface
- Corner/edge handles to resize
- Rotation handle (drag to rotate the tablet area)
- Snap-to-center and snap-to-edges guides
- Live coordinate readout during drag

This is the single highest-impact feature for demonstrating the UX vision.

### Guided Setup for Creatives (Windows)
Setting up OTD for creative work on Windows currently requires multiple manual steps: installing vmulti, adding the Windows Ink plugin, switching the output mode to "Windows Ink Absolute Mode", and configuring the drawing app. Most artists don't know they need to do this. Build a first-run wizard or setup checklist that:
- Detects whether vmulti is installed (check Windows device tree for the virtual HID device)
- Checks if the Windows Ink plugin is present
- Verifies the output mode is set correctly for pressure/tilt
- Links to or automates vmulti installation
- Recommends drawing app configuration (Krita, Photoshop, Clip Studio, etc.)

Reference: [SevenPens OTD Windows install guide](https://docs.sevenpens.com/drawtab/guides/drivers/opentabletdriver/otd-windows-install)

### Advanced Settings Toggle
Several settings are hidden from the default UI to keep the experience clean for typical users: rotation (display and tablet areas), enable clipping, and area limiting. These remain active in the data model with sensible defaults. Add an "Advanced" toggle (or expandable section) that reveals these controls for power users.

### Broader Settings Write-Back
The per-tablet dialog already writes output mode and display mapping back to the daemon via `SetSettings`. Extend write-back to the remaining fields (bindings, filters, per-profile tweaks), with debouncing of rapid changes and save/apply confirmation.

### Bindings Page
Implement the pen bindings configuration:
- Visual representation of the pen (tip, eraser, barrel buttons)
- Click-to-configure binding for each button
- Dropdown or modal for selecting binding type (key press, mouse button, multi-key combo)
- Pressure threshold sliders for tip and eraser activation

### Filters Page
Implement the filter pipeline view:
- Ordered list of active filters (drag to reorder)
- Enable/disable toggle per filter
- Expandable settings panel per filter with its plugin-specific properties
- Add/remove filters from available plugins

### Console / Log Viewer
Stream live logs from the daemon:
- Auto-scrolling log list with level-colored entries (info, warning, error, debug)
- Filter by log level
- Search/filter by text
- Pause/resume auto-scroll
- Copy log entry to clipboard

### Tablet Detection UX
Add a "Detect Tablets" button that triggers re-detection. Show a brief scanning animation. Handle the case where detection finds a new tablet (auto-select it, load its profile).

## Mid-Term (Deepen the Experience)

### Preset System
- Save/load area mapping presets (per-game or per-application profiles)
- Quick-switch between presets via sidebar or keyboard shortcut
- Import/export presets as JSON files

### Multi-Monitor Support
The area mapping visualization should show multiple display rectangles when the user has multiple monitors. The user picks which monitor (or region across monitors) to map the tablet to.

### Pressure Curve Editor
A bezier curve editor for customizing the pressure response:
- Interactive curve with draggable control points
- Presets (linear, soft, firm, S-curve)
- Live pressure preview — draw on the tablet and see the mapped pressure in real time

### Live Input Visualization
Show real-time pen input data:
- Position dot moving on the tablet area visualization
- Pressure bar graph
- Tilt angle indicator
- Hover height indicator (for tablets that support it)
- Useful for debugging and for users to verify their setup

### Plugin Browser
Browse, install, and manage OTD plugins:
- Card grid of available plugins with descriptions
- Install/uninstall with one click
- Plugin settings inline

### Animations and Micro-Interactions
- Page transition animations (crossfade or slide)
- Subtle hover animations on glass panels (parallax tilt, glow shift)
- Loading skeletons while waiting for daemon data
- Success/error toast notifications with smooth enter/exit
- Sidebar collapse/expand animation

## Long-Term (Distribution and Platform)

### Packaging as a Standalone App
The app is a single .NET 10 Avalonia process, so distribution is mostly a publish-and-bundle problem:
- **Self-contained single-file publish** (`dotnet publish -p:PublishSingleFile=true --self-contained`) — one exe, no .NET install required on the target machine.
- **Framework-dependent publish** — much smaller, but requires the .NET runtime present.
- **Installer / MSIX** — for Start-menu integration, file associations, and auto-update.

Open question: how to ship the OTD daemon alongside the app (bundle the built daemon vs. depend on an installed OTD).

### Tray Icon and Background Mode
Run the UI as a system tray application (Avalonia 12 has built-in `TrayIcon`). Closing the window minimizes to tray instead of exiting; the tray reflects daemon status and offers Show / Restart Daemon / Stop Daemon / Quit. See also *Daemon Visibility & Control From Outside the App* above.

### Cross-Platform Verification
Avalonia and .NET named pipes both run on Windows/macOS/Linux, so the core is portable — but Windows-specific code (P/Invoke display enumeration, vmulti detection, the VMulti/Windows Ink install flows) gates it today. Testing on macOS and Linux is needed, particularly:
- Named pipe paths on Unix (maps to Unix domain sockets)
- Font rendering differences
- Replacing or guarding the Windows-only P/Invoke and driver-management paths

### Accessibility
- Keyboard navigation for all controls (Avalonia provides tab/focus handling; verify and fill gaps)
- Screen reader / automation-peer labels for the interactive area mapper and custom controls
- High contrast mode (a third theme or automatic adjustments)
- Reduced motion mode (respect `prefers-reduced-motion`)

### Localization
- Extract all user-facing strings into a translation system
- RTL layout support

## Exploratory Ideas

### AI-Assisted Configuration
Use tablet usage patterns to suggest optimal area mappings and sensitivity settings. "Based on your drawing style, we recommend a 70% active area with soft pressure curve."

### Community Presets
A shared repository of community-contributed presets tagged by use case (osu!, digital art, note-taking, CAD). Browse and apply with one click.

### Visual Tablet Skins
Show an accurate visual representation of the user's specific tablet model (rendered from SVG templates per-model) instead of a generic rectangle. The active area overlay sits on top of the tablet image.

### Split-Area Mapping
Map different regions of the tablet to different monitors or applications. A creative use case for large tablets.

### Gesture Zones
Define zones on the tablet surface that trigger actions (e.g., tapping the top-left corner opens a color picker). Visual zone editor with drag-to-create rectangles.
