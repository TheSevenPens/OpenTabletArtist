# Feasibility — a macOS version of the helper (#140)

> Status: **investigation only.** Per the #140 decision this is *aspirational backlog* — we want a
> grounded read on what porting would take, not a commitment. Seeking review of this assessment.

## TL;DR

Avalonia makes the **UI** portable and the **Domain** math + the **OTD plugins** are already
platform-neutral. The cost is concentrated in the **OS-integration layer** (daemon discovery/IPC,
display enumeration, the icon font) and in two features that are **Windows-only by nature** — VMulti
and Windows Ink. macOS has its own OTD output path, so the realistic target is "the same app with a
macOS-appropriate output story," not a line-for-line port. Recommendation: **keep as backlog**; if
pursued, do it in phases behind a platform-abstraction seam, starting with the daemon/IPC layer.

## What's already portable

- **Avalonia UI** (`net10`) — windows, controls, theming, the calibration overlay. Cross-platform by
  design.
- **`Domain/`** — pure math/logic (pressure curve, smoothing, area mapping, calibration). No OS deps;
  already unit-tested on the build host.
- **OTD plugins** (`net8`, `OtdWindowsHelper.Dynamics` incl. the calibration filter) — run inside the
  daemon, which is the same managed assembly on any OS.
- **`AppSettings`** — JSON under `LocalApplicationData`; the .NET path resolves on macOS.

## Windows-specific layers (need a macOS implementation behind a seam)

| Component | What it does (Windows) | macOS status / approach |
|---|---|---|
| `Services/DaemonClient` | Connects to the daemon over a **named pipe** (`OpenTabletDriver.Daemon`) + `GetNamedPipeServerProcessId` P/Invoke for ownership | .NET named pipes on Unix are emulated over Unix domain sockets, but **must match how the OTD daemon listens on macOS** — verify OTD's macOS IPC transport. Ownership-by-pipe-PID P/Invoke is Win32-only; need a macOS way (or drop the ownership indicator there). |
| `Services/DisplayEnumerator` | Monitor geometry via **Win32 GDI** (`EnumDisplayMonitors`, `DisplayConfig`) | Reimplement via Avalonia `Screens` (cross-platform, lower fidelity for friendly names) or macOS `CGGetActiveDisplayList`. The calibration overlay placement also relies on this. |
| `Services/DaemonLifecycleService` | Finds/launches `OpenTabletDriver.Daemon` **exe**, scans by process name, reads `MainModule.FileName` | The macOS daemon isn't a Windows `.exe`; launch path/args differ, and `MainModule.FileName` for elevated/other-user processes behaves differently. Need a macOS launch + discovery path. |
| Icon font: **Segoe MDL2 Assets** (12 views) | All glyphs (nav, status marks, buttons) | Windows-only font → **glyphs render as tofu on macOS.** Switch to a bundled cross-platform icon font (e.g. Fluent System Icons / a bundled set), as we already bundle Inter. Non-trivial but mechanical. |

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

1. **Platform seam:** extract `IDaemonLifecycleService`, display enumeration, and daemon-ownership
   behind interfaces (the first two already are partly), with Windows impls today and macOS impls
   later. De-risks without committing to macOS.
2. **Icon font:** move off Segoe MDL2 to a bundled cross-platform set (benefits Windows too — no
   reliance on an OS font).
3. **macOS spike:** confirm OTD daemon builds/runs + IPC transport on macOS; get the app to connect
   and read/show profiles.
4. **Feature gating:** hide VMulti / Windows-Ink UI on non-Windows; surface the macOS output path.
5. **Packaging:** `.app` + signing/notarization + a macOS CI lane.

## Effort & recommendation

Medium-large, and gated by external unknowns (OTD macOS maturity, Apple signing). The portable core
is reachable; full feature parity is not (and isn't the right goal). **Recommendation: keep #140 as
backlog.** The one piece worth doing *regardless* is the **icon-font swap** (removes a Windows-font
dependency that's mildly fragile even on Windows) and continuing to keep OS integration behind seams.

## Open questions for review

1. Is the **portable-core** framing right (ship config/mapping/dynamics/calibration; drop VMulti +
   Windows-Ink on macOS), or is macOS only worth it with a full output story?
2. Is the **icon-font swap** worth scheduling now as a Windows-side improvement that also unblocks
   macOS, or deferred with the rest?
3. Should we invest in the **platform-abstraction seam** incrementally now (low cost, keeps options
   open), or not touch it until macOS is actually greenlit?
4. Does the pinned **OTD submodule** support macOS well enough to be the foundation, or would this
   wait on an OTD upgrade?
