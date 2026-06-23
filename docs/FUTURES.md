# Futures — Potential Work Items and Directions

Each item links to a tracking issue with the full detail.

## Recently Completed

Several items once listed here have shipped and are no longer "future":

- **Auto-start with the daemon** — the app locates and launches the submodule's `OpenTabletDriver.Daemon.exe` (and verifies it's the app-owned instance).
- **Saved Settings (preset snapshots)** — save / load / rename / delete configuration snapshots (the *Saved Settings* page).
- **Live input visualization** — the *Diagnostics* page streams pen position (area + dot), pressure, and tilt via `Controls/TabletVisualizer`.
- **Settings write-back (initial)** — the per-tablet dialog writes output mode and display mapping back to the daemon via `SetSettings`.
- **Windows Ink + VMulti management** — detect, install, update, and uninstall both from the Dashboard.
- **Daemon lifecycle UX** — Start / Stop / Restart with reliable reconnect and progress feedback.

## Near-Term (Polish the Prototype)

- **Daemon Visibility & Control From Outside the App** — Surface daemon status/control when the app window is closed (tray icon, etc.). ([#57](https://github.com/TheSevenPens/OTDWindowsHelper/issues/57))
- **Investigate App Shutdown Cleanliness** — Ensure the app exits within ~500ms on window close with no lingering file locks. ([#58](https://github.com/TheSevenPens/OTDWindowsHelper/issues/58))
- **Interactive Area Mapper** — Make the area visualization draggable/resizable/rotatable with snap guides and a live readout. ([#59](https://github.com/TheSevenPens/OTDWindowsHelper/issues/59))
- **Guided Setup for Creatives (Windows)** — First-run wizard chaining vmulti + Windows Ink + output-mode setup for creatives. ([#60](https://github.com/TheSevenPens/OTDWindowsHelper/issues/60))
- **Advanced Settings Toggle** — Reveal hidden settings (rotation, clipping, area limiting) behind an Advanced expander. ([#61](https://github.com/TheSevenPens/OTDWindowsHelper/issues/61))
- **Broader Settings Write-Back** — Extend write-back beyond output mode/display to bindings, filters, and per-profile tweaks. ([#62](https://github.com/TheSevenPens/OTDWindowsHelper/issues/62))
- **Bindings Page** — Configure pen tip/eraser/button bindings with a visual pen and per-button editors. ([#63](https://github.com/TheSevenPens/OTDWindowsHelper/issues/63))
- **Filters Page** — View, reorder, toggle, and configure the daemon's filter pipeline. ([#64](https://github.com/TheSevenPens/OTDWindowsHelper/issues/64))
- **Console / Log Viewer** — Stream live daemon logs with level coloring, filtering, and pause/resume. ([#65](https://github.com/TheSevenPens/OTDWindowsHelper/issues/65))
- **Tablet Detection UX** — A 'Detect Tablets' button with scanning feedback and auto-select of new tablets. ([#66](https://github.com/TheSevenPens/OTDWindowsHelper/issues/66))

## Mid-Term (Deepen the Experience)

- **Multi-Monitor Support** — Show all displays in the area mapper and map the tablet to a chosen monitor/region. ([#67](https://github.com/TheSevenPens/OTDWindowsHelper/issues/67))
- **Pressure Curve Editor** — Bezier pressure-response editor with presets and a live draw-to-preview. ([#68](https://github.com/TheSevenPens/OTDWindowsHelper/issues/68))
- **Plugin Browser** — Browse, install, and configure OTD plugins from a card grid. ([#69](https://github.com/TheSevenPens/OTDWindowsHelper/issues/69))
- **Animations and Micro-Interactions** — Page transitions, hover effects, loading skeletons, and toast notifications. ([#70](https://github.com/TheSevenPens/OTDWindowsHelper/issues/70))

## Long-Term (Distribution and Platform)

- **Packaging as a Standalone App** — Publish/bundle options (single-file, framework-dependent, installer/MSIX) and shipping the daemon. ([#71](https://github.com/TheSevenPens/OTDWindowsHelper/issues/71))
- **Tray Icon and Background Mode** — Run as a tray app: minimize-to-tray with daemon status and start/stop controls. ([#72](https://github.com/TheSevenPens/OTDWindowsHelper/issues/72))
- **Cross-Platform Verification** — Test/guard the Windows-specific paths so the app runs on macOS/Linux. ([#73](https://github.com/TheSevenPens/OTDWindowsHelper/issues/73))
- **Accessibility** — Keyboard nav, screen-reader labels, high-contrast theme, and reduced-motion support. ([#74](https://github.com/TheSevenPens/OTDWindowsHelper/issues/74))
- **Localization** — Extract user-facing strings into a translation system; add RTL support. ([#75](https://github.com/TheSevenPens/OTDWindowsHelper/issues/75))

## Exploratory Ideas

- **AI-Assisted Configuration** — Suggest area/sensitivity settings from usage patterns. ([#76](https://github.com/TheSevenPens/OTDWindowsHelper/issues/76))
- **Community Presets** — A shared, tagged repository of community presets to browse and apply. ([#77](https://github.com/TheSevenPens/OTDWindowsHelper/issues/77))
- **Visual Tablet Skins** — Render the user's actual tablet model from per-model SVGs with the active area overlaid. ([#78](https://github.com/TheSevenPens/OTDWindowsHelper/issues/78))
- **Split-Area Mapping** — Map different tablet regions to different monitors/apps. ([#79](https://github.com/TheSevenPens/OTDWindowsHelper/issues/79))
- **Gesture Zones** — Define tablet-surface zones that trigger actions, via a visual zone editor. ([#80](https://github.com/TheSevenPens/OTDWindowsHelper/issues/80))

## Community Suggestions

User-submitted feature ideas from GitHub issues (labeled `suggestion`):

- **Special handling for ultrawide** — better area-mapping for ultrawide displays. ([#3](https://github.com/TheSevenPens/OTDWindowsHelper/issues/3))
- **Map active area to a specific application** — per-application active-area mappings. ([#4](https://github.com/TheSevenPens/OTDWindowsHelper/issues/4))
- **Hotkey to switch between mappings** — global hotkey to cycle/switch saved mappings. ([#5](https://github.com/TheSevenPens/OTDWindowsHelper/issues/5))
- **Implement smoothing** — an input smoothing filter. ([#6](https://github.com/TheSevenPens/OTDWindowsHelper/issues/6))
- **On-Screen-Menu for shortcuts** — a Wacom-style on-screen radial/menu for shortcuts. ([#7](https://github.com/TheSevenPens/OTDWindowsHelper/issues/7))
- **Per-application settings** — distinct settings/profiles auto-applied per foreground app. ([#8](https://github.com/TheSevenPens/OTDWindowsHelper/issues/8))
- **Pressure curve calibrator** — auto-calibrate the curve from sampled presses (perceptual → linear feel); complements the manual editor in [#68](https://github.com/TheSevenPens/OTDWindowsHelper/issues/68). ([#9](https://github.com/TheSevenPens/OTDWindowsHelper/issues/9))
