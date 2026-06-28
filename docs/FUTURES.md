# Futures — Potential Work Items and Directions

Each item links to a tracking issue with the full detail.

## Recently Completed

Several items once listed here have shipped and are no longer "future":

- **Auto-start with the daemon** — the app locates and launches the submodule's `OpenTabletDriver.Daemon.exe` (and verifies it's the app-owned instance).
- **Saved Settings (preset snapshots)** — save / load / rename / delete configuration snapshots (the *Saved Settings* page).
- **Live input visualization** — the *Diagnostics* page streams pen position (area + dot), pressure, and tilt via `Controls/TabletVisualizer`.
- **Settings write-back (initial)** — the per-tablet dialog writes output mode and display mapping back to the daemon via `SetSettings`.
- **Windows Ink + VMulti management** — detect, install, update, and uninstall both from the Dashboard. VMulti install/uninstall now run **in-app** (single UAC, no console window) and clean up leftover device nodes (#110/#111/#112).
- **Daemon lifecycle UX** — Start / Stop / Restart with reliable reconnect and progress feedback.
- **Pen Dynamics** — interactive pressure-curve editor **plus** position/pressure smoothing, enforced by the bundled OTD filter; presets, numeric node read-outs, live pressure dot, and a focused dynamics-only editor reachable from the Test view (#68/#101–#104/#119/#133).
- **Pointer calibration** — 4-tap calibration for pen displays: capture, least-squares affine correction via a bundled filter, with a "may be stale" hint when the mapping changes (#127/#147).
- **Graphical area mapping** — Screen-Mapping tab with a to-scale monitor picker, Absolute/Relative toggle, and aspect-locked apply (#117); supersedes the old multi-monitor item (#67).
- **Light / Dark / System theming** — variant-aware palette with a sidebar selector, defaulting to follow-system (#139).
- **Paired Tablets ordering** — detected tablet first, then most-recently-seen (persisted), then never-seen (#137/#138).
- **Plugins page** — read-only list of installed daemon plugins with active/installed status.
- **Simplified iconography** — replaced the Windows-only Segoe MDL2 icon font with text labels + colored status dots (#150).
- **System tray & background mode** — a tray icon reflects daemon status and offers Show / Start-or-Stop / Restart / Quit; closing the window minimizes to the tray (#72).

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

- **Plugin Browser** — A read-only Plugins page shipped; full browse/install/configure from a card grid remains. ([#69](https://github.com/TheSevenPens/OTDWindowsHelper/issues/69))
- **Supported-tablets catalog** — show the full list of OTD-compatible tablets (read from the embedded configs); investigated and scoped, implementation deferred. ([#155](https://github.com/TheSevenPens/OTDWindowsHelper/issues/155))
- **Animations and Micro-Interactions** — Page transitions, hover effects, loading skeletons, and toast notifications. ([#70](https://github.com/TheSevenPens/OTDWindowsHelper/issues/70))

## Long-Term (Distribution and Platform)

- **Packaging as a Standalone App** — Publish/bundle options (single-file, framework-dependent, installer/MSIX) and shipping the daemon. ([#71](https://github.com/TheSevenPens/OTDWindowsHelper/issues/71))
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
