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
- **System tray & background mode** — a tray icon reflects daemon status and offers Show / Start-or-Stop / Restart / Quit; closing the window minimizes to the tray (#72). Fully covers the "daemon visibility & control from outside the app" ask ([#57](https://github.com/TheSevenPens/OpenTabletArtist/issues/57)).
- **Console / Log Viewer** — the *Log* page streams daemon logs with level filtering, level-colored entries, an auto-scroll toggle, and copy (text / Markdown / HTML). Free-text search is the one remaining follow-up ([#275](https://github.com/TheSevenPens/OpenTabletArtist/issues/275)). ([#65](https://github.com/TheSevenPens/OpenTabletArtist/issues/65))
- **Broader settings write-back** — express-key, pen-switch, and wheel bindings, pen dynamics, hover limit, and calibration all write back to the daemon with debouncing, extending the initial output-mode/mapping write-back. Full filter add/remove/reorder remains ([#64](https://github.com/TheSevenPens/OpenTabletArtist/issues/64)). ([#62](https://github.com/TheSevenPens/OpenTabletArtist/issues/62))
- **Express-key (auxiliary button) bindings** — editable per-button bindings (keyboard key, multi-key combo, mouse button, mouse scroll) with a live press highlight, a master enable toggle, and clear-all.
- **Tablet wheel bindings** — a *Wheel* tab for tablets that report a wheel / touch ring: bind clockwise and counter-clockwise rotation and the wheel button (same editor as express keys), per-direction sensitivity (activation threshold) with step-size info, a live circular gauge (position marker + rotation direction), and a master enable / clear-all. ([PR #273](https://github.com/TheSevenPens/OpenTabletArtist/pull/273), [PR #274](https://github.com/TheSevenPens/OpenTabletArtist/pull/274))
- **Active Area tab** — read-only visualization of the tablet's full vs. effective (used) area, drawn to scale, with usage stats (area %, per-axis %, mm dimensions, and both diagonals) and a note that the area auto-matches the display's proportions. The interactive editor (drag/resize/rotate) remains ([#59](https://github.com/TheSevenPens/OpenTabletArtist/issues/59)).
- **Display hardware details** — each monitor now shows its connector/port (HDMI / DisplayPort / USB-C / Internal / …) and the GPU driving it, in the display picker and a per-display list on the *Display Mapping* tab.
- **Tablet-settings layout** — the per-tablet tabs moved to a vertical left rail, and the old *Screen Mapping* tab was split into focused **Output Mode**, **Display Mapping**, and **Calibration** tabs.
- **Window fit on scaled/low-res displays** — the window clamps to the current screen's working area (DPI-aware) on open, so it no longer overruns the top edge or hides under the taskbar (e.g. 1080p at 150% scale).
- **Clean, prompt shutdown** — the disposal chain tears down the session RPC/pipe, hotkey window, foreground watcher, and timers on exit; the process then force-terminates so the `.exe` lock releases at once (no multi-second lock blocking rebuilds), and the tray icon is removed rather than left to ghost. ([#58](https://github.com/TheSevenPens/OpenTabletArtist/issues/58))

## Near-Term (Polish the Prototype)

- **Interactive Area Mapper** — Make the area visualization draggable/resizable/rotatable with snap guides and a live readout. ([#59](https://github.com/TheSevenPens/OpenTabletArtist/issues/59))
- **Guided Setup for Creatives (Windows)** — First-run wizard chaining vmulti + Windows Ink + output-mode setup for creatives. ([#60](https://github.com/TheSevenPens/OpenTabletArtist/issues/60))
- **Advanced Settings Toggle** — Reveal hidden settings (rotation, clipping, area limiting) behind an Advanced expander. ([#61](https://github.com/TheSevenPens/OpenTabletArtist/issues/61))
- **Bindings Page (remaining)** — Express-key, pen-switch, and wheel bindings ship; still to do are pen tip/eraser *pressure-threshold* controls and a visual pen. ([#63](https://github.com/TheSevenPens/OpenTabletArtist/issues/63))
- **Filters Page** — A read-only filter list ships; make it reorderable/toggleable with per-filter settings and add/remove. ([#64](https://github.com/TheSevenPens/OpenTabletArtist/issues/64))
- **Tablet Detection UX** — A 'Detect Tablets' button with scanning feedback and auto-select of new tablets. ([#66](https://github.com/TheSevenPens/OpenTabletArtist/issues/66))

## Mid-Term (Deepen the Experience)

- **Plugin Browser** — A read-only Plugins page shipped; full browse/install/configure from a card grid remains. ([#69](https://github.com/TheSevenPens/OpenTabletArtist/issues/69))
- **Supported-tablets catalog** — show the full list of OTD-compatible tablets (read from the embedded configs); investigated and scoped, implementation deferred. ([#155](https://github.com/TheSevenPens/OpenTabletArtist/issues/155))
- **Per-application settings** — auto-switch tablet config (mapping/dynamics/bindings) by foreground app, the way manufacturer drivers do. Investigated ([design doc](../design/167-per-app-settings.md)): feasible by reusing our existing **Saved Settings** snapshots (applied via the daemon's `SetSettings`) + a Win32 foreground-window watcher + a live-apply-only switch path, but **gated by a latency / mid-stroke-safety spike** before any build. Tray/background mode (#72) is the prerequisite and is shipped. ([#167](https://github.com/TheSevenPens/OpenTabletArtist/issues/167))
- **Animations and Micro-Interactions** — Page transitions, hover effects, loading skeletons, and toast notifications. ([#70](https://github.com/TheSevenPens/OpenTabletArtist/issues/70))

## Long-Term (Distribution and Platform)

- **Packaging as a Standalone App** — Windows already ships a self-contained `win-x64` build (`release.yml`). Remaining: **macOS packaging** — a self-contained, ad-hoc-signed `.app` with the daemon bundled (Phase 6), then notarization + Developer-ID signing (Phase 7, V2); see [macOS handoff](../design/macos/HANDOFF.md). ([#71](https://github.com/TheSevenPens/OpenTabletArtist/issues/71))
- **Shrink the Windows download** — the zip carries two self-contained .NET runtimes (app net10 + daemon net8), the price of zero-install. Debug symbols are already stripped (~100 MB, mostly native Skia/HarfBuzz `.pdb`), bringing it to ~87 MB. Next: **single-file publishing** (`PublishSingleFile` + compression), verified to collapse the ~250 loose root files to one exe and shrink the payload — pending an end-to-end check of bundled-daemon auto-start from a packaged single-file build. ([#585](https://github.com/TheSevenPens/OpenTabletArtist/issues/585), [#586](https://github.com/TheSevenPens/OpenTabletArtist/issues/586))
- **Cross-Platform Verification** — **Done for macOS**: Phases 0–5 are merged and live-verified on Apple-Silicon macOS (build, connect, detect, map, calibrate; Windows-only surface gated off) — see [macOS handoff](../design/macos/HANDOFF.md). **Linux** is feasibility-assessed (builds + tests on the CI matrix; [192-linux-feasibility.md](../design/192-linux-feasibility.md)) pending live verification on hardware. ([#73](https://github.com/TheSevenPens/OpenTabletArtist/issues/73))
- **Accessibility** — Keyboard nav, screen-reader labels, high-contrast theme, and reduced-motion support. ([#74](https://github.com/TheSevenPens/OpenTabletArtist/issues/74))
- **Localization** — Extract user-facing strings into a translation system; add RTL support. ([#75](https://github.com/TheSevenPens/OpenTabletArtist/issues/75))

## Exploratory Ideas

- **AI-Assisted Configuration** — Suggest area/sensitivity settings from usage patterns. ([#76](https://github.com/TheSevenPens/OpenTabletArtist/issues/76))
- **Community Presets** — A shared, tagged repository of community presets to browse and apply. ([#77](https://github.com/TheSevenPens/OpenTabletArtist/issues/77))
- **Visual Tablet Skins** — Render the user's actual tablet model from per-model SVGs with the active area overlaid. ([#78](https://github.com/TheSevenPens/OpenTabletArtist/issues/78))
- **Split-Area Mapping** — Map different tablet regions to different monitors/apps. ([#79](https://github.com/TheSevenPens/OpenTabletArtist/issues/79))
- **Gesture Zones** — Define tablet-surface zones that trigger actions, via a visual zone editor. ([#80](https://github.com/TheSevenPens/OpenTabletArtist/issues/80))

## Community Suggestions

User-submitted feature ideas from GitHub issues (labeled `suggestion`):

- **Special handling for ultrawide** — better area-mapping for ultrawide displays. ([#3](https://github.com/TheSevenPens/OpenTabletArtist/issues/3))
- **Map active area to a specific application** — per-application active-area mappings. ([#4](https://github.com/TheSevenPens/OpenTabletArtist/issues/4))
- **Hotkey to switch between mappings** — global hotkey to cycle/switch saved mappings. ([#5](https://github.com/TheSevenPens/OpenTabletArtist/issues/5))
- **Implement smoothing** — an input smoothing filter. ([#6](https://github.com/TheSevenPens/OpenTabletArtist/issues/6))
- **On-Screen-Menu for shortcuts** — a Wacom-style on-screen radial/menu for shortcuts. ([#7](https://github.com/TheSevenPens/OpenTabletArtist/issues/7))
- **Per-application settings** — distinct settings/profiles auto-applied per foreground app. ([#8](https://github.com/TheSevenPens/OpenTabletArtist/issues/8))
- **Pressure curve calibrator** — auto-calibrate the curve from sampled presses (perceptual → linear feel); complements the manual editor in [#68](https://github.com/TheSevenPens/OpenTabletArtist/issues/68). ([#9](https://github.com/TheSevenPens/OpenTabletArtist/issues/9))
