# Feasibility — a macOS version of the helper (#140)

> Status: **investigation only.** Per the #140 decision this is *aspirational backlog* — we want a
> grounded read on what porting would take, not a commitment.

> **Update (2026-07).** Re-baselined against the current codebase. The original assessment (design
> review #148) still holds on the portable core, but it **understated the cost**: it predated a wave of
> deeper Windows integration — global hotkeys, the per-app foreground watcher, run-at-startup, the
> single-instance guard, the whole Windows Ink auto-setup layer, driver cleanup, and the health-check
> catalog. The Windows-specific surface has roughly doubled. See "Windows-specific layers" and "Feature
> layers that assume Windows" below. The recommendation is unchanged: **backlog.** The silver lining is
> that nearly every seam is already isolated in its own service class, so the architecture is port-friendly.
> Current P/Invoke footprint: **18 user32 · 4 setupapi · 2 kernel32 · 1 cfgmgr32** sites.

## TL;DR

Avalonia makes the **UI** portable and the **Domain** math + the **OTD plugins** are already
platform-neutral. The cost is concentrated in the **OS-integration layer** (daemon discovery/IPC,
display enumeration) and in two features that are **Windows-only by nature** — VMulti
and Windows Ink. macOS has its own OTD output path, so the realistic target is "the same app with a
macOS-appropriate output story," not a line-for-line port. Recommendation: **keep as backlog**; if
pursued, do it in phases behind a platform-abstraction seam, starting with the daemon/IPC layer.

## What's already portable

- **Avalonia UI** (`net10`) — windows, controls, theming, the calibration overlay. Cross-platform by
  design.
- **`Domain/`** — pure math/logic (pressure curve, smoothing, area mapping, calibration). No OS deps;
  already unit-tested on the build host.
- **OTD plugins** (`net8`, `OpenTabletArtist.Dynamics` incl. the calibration filter) — run inside the
  daemon, which is the same managed assembly on any OS.
- **`AppSettings`** — JSON under `LocalApplicationData`; the .NET path resolves on macOS.

## Windows-specific layers (need a macOS implementation behind a seam)

| Component | What it does (Windows) | macOS status / approach |
|---|---|---|
| `Services/DaemonClient` | Connects to the daemon over a **named pipe** (`OpenTabletDriver.Daemon`) + `GetNamedPipeServerProcessId` P/Invoke for ownership | .NET named pipes on Unix are emulated over Unix domain sockets, but **must match how the OTD daemon listens on macOS** — verify OTD's macOS IPC transport. Ownership-by-pipe-PID P/Invoke is Win32-only; need a macOS way (or drop the ownership indicator there). |
| `Services/DisplayEnumerator` | Monitor geometry via **Win32 GDI** (`EnumDisplayMonitors`, `DisplayConfig`) | Reimplement via Avalonia `Screens` (cross-platform, lower fidelity for friendly names) or macOS `CGGetActiveDisplayList`. The calibration overlay placement also relies on this. |
| `Services/DaemonLifecycleService` | Finds/launches `OpenTabletDriver.Daemon` **exe**, scans by process name, reads `MainModule.FileName` | The macOS daemon isn't a Windows `.exe`; launch path/args differ, and `MainModule.FileName` for elevated/other-user processes behaves differently. Need a macOS launch + discovery path. |
| `Services/GlobalHotkeyService` | Global hotkeys via `RegisterHotKey` + a message-only window (user32); backs profile-switch (#320) and monitor-cycle (#89) chords | Windows-only. macOS needs Carbon `RegisterEventHotKey` or a `CGEventTap` (the latter requires an **Accessibility** grant). Behind `IGlobalHotkeys`, macOS could no-op until implemented. |
| `Services/ForegroundAppWatcher` | Per-app profile switching (#167) via `SetWinEventHook` foreground events (user32) | Windows-only. macOS equivalent is `NSWorkspace.didActivateApplicationNotification` — a different event model + bundle-id vs exe-name identity. |
| `Services/StartupService` | Run-at-startup via the HKCU `Run` key + a `--background` tray launch (#360/#381) | Windows-only (registry). macOS uses a `LaunchAgent` plist (or `SMAppService`). The card already hides via `IsSupported`. |
| `Services/SingleInstance` | Single-instance guard via a **named `Mutex`** + `EventWaitHandle` (#191) | **Named mutexes are Windows-only in .NET** (throw `PlatformNotSupported` on Unix). Needs a file-lock or Unix-domain-socket approach on macOS. |
| `Services/ProcessElevation` / `Helpers/ProfileToast` | "Running as admin" detection (`WindowsIdentity`) and a Win32 toast/flash (user32) | `ProcessElevation` is already guarded (returns `false` off-Windows → the health check goes inert). `ProfileToast` needs a macOS notification path (or degrade to the in-app cue). |
| ~~Icon font: **Segoe MDL2 Assets**~~ | — | **Resolved (#150):** the Windows-only icon font was removed entirely (text labels + colored status dots). The only remaining `Segoe` refs are `"Segoe UI, Inter"` **text** fonts (graceful fallback) and the Skia controls' `Typeface("Segoe UI")`, now routed through `AppFonts` (#392) — cosmetic, not a blocker. |

## Feature layers that assume Windows (need gating or a macOS backend)

Beyond the low-level seams, several *features* added since #148 bake in Windows assumptions and would
need to be platform-gated or given a macOS backend:

- **Health-check catalog (#317).** `HealthEvaluator` + the Home "Needs attention" list check for
  VMulti, Windows Ink, conflicting manufacturer drivers, and "running as admin" — most of which don't
  apply on macOS. Needs platform gating so it doesn't nag about things that can't be fixed there.
- **Driver cleanup** (`DriverConflictMonitor` / `TabletDriverCleanupRunner`) — parses OTD's Windows
  manufacturer-driver conflict warnings (Wacom/Huion/XP-Pen) and runs the Windows-only
  TabletDriverCleanup tool. Whole page is Windows-only.
- **Windows Ink auto-setup** (`WindowsInkAutoSetup` / `WindowsInkBundledInstaller` / `WinInkAutoOptOut`
  / `WindowsInkPluginService`) — auto-installs + opt-out for the Windows Ink output mode (#361/#364/#380).
  Windows-only by nature (see blockers below); hidden on macOS.
- **Per-app switching (#167)** and **global hotkeys (#320/#89)** — functional features that each need a
  macOS backend (see the watcher/hotkey rows above) or a graceful "unavailable on this platform" state.

## The two real blockers (Windows-only by nature)

- **VMulti** (`VMultiDetector` / `VMultiInstaller`, `install_hiddriver.bat`) — a Windows HID driver.
  No macOS equivalent and not needed there: on macOS OTD emits input through its own output stage.
  The entire VMulti card/flow would be **hidden/removed** on macOS.
- **Windows Ink** output mode (and Kuuuube's Windows Ink plugin) — a Windows concept. macOS pressure
  reaches apps through OTD's macOS absolute/relative output, not Windows Ink. The output-mode UI and
  the Windows-Ink plugin management would need a **macOS-appropriate output story** (or be hidden).

Net: a chunk of the app's *value* (VMulti + Windows Ink management, the creative-focused
recommendations) is Windows-specific. The portable core is "connect to the daemon, configure
profiles, area mapping, dynamics, calibration, test." That's still a worthwhile app, but it's a
different product surface on macOS.

## Other macOS realities

- **Permissions:** macOS requires **Accessibility** and/or **Input Monitoring** grants for tablet
  input + cursor control; the app/daemon must guide the user through System Settings.
- **Packaging:** `.app` bundle, **code signing + notarization** (Apple Developer account) for
  distribution; Gatekeeper otherwise blocks it. Our release workflow is Windows-only today.
- **OTD daemon on macOS:** confirm the pinned submodule (OTD 0.6.x) actually supports macOS and how
  it's built/run there — this gates everything.

## Recommended phasing (if pursued)

1. **Platform seam:** extract the OS-integration services behind interfaces — display enumeration,
   daemon-ownership, `IDaemonLifecycleService` (already an interface), plus the seams added since #148:
   `IGlobalHotkeys`, `IForegroundWatcher`, `IStartupManager`, `ISingleInstance`, elevation, tray/toast.
   Windows impls exist today; add macOS impls or no-ops later. De-risks without committing to macOS.
2. **Icon font:** ✅ done — the Segoe MDL2 dependency was removed in #150 (text + colored dots).
3. **macOS spike:** confirm OTD daemon builds/runs + IPC transport on macOS; get the app to connect
   and read/show profiles.
4. **Feature gating:** hide the Windows-only UI on non-Windows (VMulti, Windows Ink, Driver Cleanup,
   run-at-startup) and platform-gate the **health-check catalog** so it doesn't nag about them; surface
   the macOS output path. Per-app switching + global hotkeys either get a macOS backend or a graceful
   "unavailable" state.
5. **Packaging:** `.app` + signing/notarization + a macOS CI lane.

## Effort & recommendation

Medium-large, and **larger than the #148 read** — the Windows-specific surface has roughly doubled
since (global hotkeys, foreground watcher, run-at-startup, single-instance, Windows Ink auto-setup,
driver cleanup, health catalog). Still gated by external unknowns (OTD macOS maturity, Apple signing).
The portable core is reachable; full feature parity is not (and isn't the right goal). **Recommendation:
keep #140 as backlog.** The icon-font dependency was already removed (#150); the ongoing win is keeping
each new OS-integration feature behind its own service class (as they already are), so a future port is
interface-swaps rather than surgery.

## macOS toolchain facts (confirmed in the pinned submodule)

- OTD ships `OpenTabletDriver.MacOS.slnf`, an `OpenTabletDriver.UX.MacOS`, and a `PermissionHelper`;
  `DaemonWatchdog` has a macOS launch path (`OpenTabletDriver.Daemon`, no `.exe`). So **OTD itself is
  a first-class macOS target at 0.6.x** — and the daemon uses the **same pipe name**, so our
  `DaemonClient` may connect as-is (`NamedPipeClientStream` over Unix-domain-socket emulation).
- The gap is **our** integration, not OTD: daemon-path discovery, the ownership P/Invoke, display
  enumeration, permissions UX, and packaging. *OTD macOS maturity ≠ this helper being macOS-ready.*
- The biggest P/Invoke gaps are **`DisplayEnumerator` (GDI)** and **daemon-ownership**
  (`GetNamedPipeServerProcessId`) — not VMulti/HidSharp (that whole feature is hidden on macOS anyway).
- `IDaemonLifecycleService` is already an interface — the platform seam is **partially in place**.
- The **calibration overlay** hardcodes dark styling (fine cross-platform) but its display-matching /
  placement logic needs separate validation on a macOS pen display.

## Recommended first step if greenlit

A **macOS spike, in this order**, before any UI port work: build the daemon from the submodule →
connect with the existing `DaemonClient` → call `GetSettings()`. If that round-trips, the foundation
is sound and the rest is the integration/UI work above.

## Resolved (design review #148)

1. **Portable-core is the goal.** Ship connect + profiles + area mapping + dynamics + calibration +
   test; hide the VMulti / Windows-Ink dashboard cards on macOS and surface OTD's native output path.
   A full macOS "output story" (guided setup, mac-specific recommendations) can follow later.
2. **Icon-font swap: schedule now**, independent of any macOS greenlight — it removes a Windows-only
   font dependency (fragile even on clean Windows installs) and is a small standalone PR. Tracked
   separately.
3. **Platform seam: yes, incrementally and lightly** — extract `DisplayEnumerator` behind an
   interface when next touched, gate VMulti/Windows-Ink views with `OperatingSystem.IsWindows()` when
   next edited (zero cost, documents intent), and defer macOS impls until the spike passes.
4. **OTD submodule is likely a sufficient foundation** (see toolchain facts above); the unknown is our
   integration, settled by the spike — not an OTD upgrade.

**Outcome:** #140 stays **backlog**. (The icon-font dependency was since removed entirely in #150.)
