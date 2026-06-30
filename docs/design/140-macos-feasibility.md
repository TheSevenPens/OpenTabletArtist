# Feasibility — a macOS version of the helper (#140)

> Status: **investigation only.** Per the #140 decision this is *aspirational backlog* — we want a
> grounded read on what porting would take, not a commitment. Seeking review of this assessment.

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
| ~~Icon font: **Segoe MDL2 Assets**~~ | — | **Resolved (#150):** the Windows-only icon font was removed entirely (text labels + colored status dots), so it's no longer a macOS blocker. |

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
2. **Icon font:** ✅ done — the Segoe MDL2 dependency was removed in #150 (text + colored dots).
3. **macOS spike:** confirm OTD daemon builds/runs + IPC transport on macOS; get the app to connect
   and read/show profiles.
4. **Feature gating:** hide VMulti / Windows-Ink UI on non-Windows; surface the macOS output path.
5. **Packaging:** `.app` + signing/notarization + a macOS CI lane.

## Effort & recommendation

Medium-large, and gated by external unknowns (OTD macOS maturity, Apple signing). The portable core
is reachable; full feature parity is not (and isn't the right goal). **Recommendation: keep #140 as
backlog.** The icon-font dependency has since been removed (#150); the remaining win is continuing to
keep OS integration behind seams.

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
