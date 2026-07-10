# Feasibility — a macOS version of the helper (#140)

> Status: **investigation only.** Per the #140 decision this is *aspirational backlog* — we want a
> grounded read on what porting would take, not a commitment.

> **Update (2026-07-09).** Re-baselined again against `master` (v0.36.0), ~170 commits past the previous
> refresh. The original assessment (design review #148) still holds on the portable core, but the cost has
> kept climbing: on top of the earlier wave — global hotkeys, the per-app foreground watcher, run-at-startup,
> the single-instance guard, the whole Windows Ink auto-setup layer, driver cleanup, and the health-check
> catalog — this pass turned up **three more Win32 seams the prior footprint missed**: `ShellPenFeedback`
> (suppresses the shell's pen/touch feedback rings app-wide), and the `ClipboardImage` / `ClipboardText`
> helpers (dev-screenshot + log/report copy), plus a P/Invoke on the calibration overlay (press-and-hold
> suppression). All three are **cosmetic/convenience, already `IsWindows()`-guarded to a graceful no-op**,
> so they degrade rather than block — but they move the numbers. Corrected P/Invoke footprint:
> **28 user32 · 10 kernel32 · 4 setupapi · 1 cfgmgr32** sites (was reported as 18 · 2 · 4 · 1). The
> recommendation is unchanged: **backlog.** The silver lining still holds — nearly every seam is isolated in
> its own service class and seven now self-gate with `OperatingSystem.IsWindows()`, so the architecture is
> port-friendly. See "Windows-specific layers" and "Feature layers that assume Windows" below.

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
| `Services/SingleInstance` | Single-instance guard via a **named `Mutex`** + `EventWaitHandle` (#191) | **Named mutexes are Windows-only in .NET** (throw `PlatformNotSupported` on Unix). Already `IsWindows()`-gated to skip the guard off-Windows — so it's degraded-but-safe today (every launch is "primary"; no crash), not a hard blocker. A real macOS impl still needs a file-lock or Unix-domain-socket approach. |
| `Services/ShellPenFeedback` · `Services/ClipboardImage` · `Services/ClipboardText` | Cosmetic/convenience Win32: suppress the shell's pen/touch feedback rings app-wide (`SetWindowFeedbackSetting`, user32) and put images/text on the clipboard (kernel32 `Global*` + user32 clipboard APIs) for the dev-screenshot and log/report-copy features. The calibration overlay also has a user32 P/Invoke to suppress press-and-hold. | All **already `IsWindows()`-guarded to a no-op / `false`**, so they degrade gracefully. A macOS backend would route clipboard through Avalonia's `IClipboard` (or `NSPasteboard`) and simply drop the shell-feedback suppression (no macOS analogue needed). Low priority — not on the critical path. |
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

## Spike log — 2026-07-09 (macos branch, no code changes)

A grounded pass on an **Apple-Silicon (arm64) macOS host** — which turned out to already have
**OpenTabletDriver.app installed and a real Wacom Movink 13 (DTH-135) pen display attached**, i.e. a genuine
test rig. Results, in order:

- **Toolchain bootstrapped.** The machine had only a .NET **9** SDK (off PATH) and no net10. Installed the
  **net10 SDK** (`10.0.301`) via the official `dotnet-install.sh` into `~/.dotnet` (coexists with 9; no sudo).
  Note: our submodule daemon is `net8.0` and needs the **.NET 8 *runtime*** to run standalone — the SDK bundles
  9 + 10 only — so the round-trip below was instead run against OTD.app's self-contained daemon (more
  representative anyway).
- **OTD v0.6.7 macOS targets verified** in the pinned submodule (not just claimed): `OpenTabletDriver.MacOS.slnf`,
  `OpenTabletDriver.UX.MacOS/`, `PermissionHelper.cs`, and `DaemonWatchdog` launching `OpenTabletDriver.Daemon`
  (no `.exe`) on non-Windows. Pipe name shared.
- **OTD daemon builds on macOS: ✅** `dotnet build OpenTabletDriver.Daemon` (net8, arm64) → **0 warnings, 0 errors**.
- **The OTD projects OTA compiles are port-clean.** OTA `<ProjectReference>`s four OTD projects
  (`OpenTabletDriver.Desktop`, `OpenTabletDriver`, `OpenTabletDriver.Plugin`, `OpenTabletDriver.Configurations`)
  and builds them as part of its own app. All four target **plain `net8.0` — no `-windows` TFM, no Windows-only
  conditionals, no Windows-native package deps** (Octokit / SharpZipLib / StreamJsonRpc / System.CommandLine /
  WaylandNET — the last is Linux-runtime-only but a harmless managed reference on macOS).
- **OTA itself builds on macOS: ✅** `dotnet build OpenTabletArtist.csproj` (net10) → **0 warnings, 0 errors**.
  Every Windows `DllImport` compiles (P/Invoke binds lazily at runtime) and the `IsWindows()` guards handle the
  rest — **there is no compile-time Windows blocker**. Build risk on macOS is effectively nil; the whole macOS
  problem is *runtime* behaviour of the seams, not compilation.
- **Daemon round-trip: ✅ (the decisive test) — against BOTH the system OTD.app daemon *and our own
  submodule-built v0.6.7 daemon*.** A throwaway net10 probe mirroring `DaemonClient`'s exact path
  — `NamedPipeClientStream("OpenTabletDriver.Daemon")` → `new JsonRpc(pipe)` → `InvokeAsync<T>(...)` — connected
  over the .NET named-pipe → Unix-domain-socket emulation and round-tripped: `GetTablets` → **1 tablet,
  "Wacom Movink 13 (DTH-135)"**; `GetSettings` → a live `Settings` object (`Revision, Profiles,
  LockUsableAreaDisplay, LockUsableAreaTablet, Tools`; **2 profiles**). First run was against OTD.app's bundled
  (older, self-contained) daemon; the **second was against the daemon we built from the pinned submodule** — the
  exact version OTA references — confirming the contract on *our* build, not just a released one. **DaemonClient's
  connect+RPC layer needs no changes to talk to the macOS daemon.**
- **net8 runs on macOS 26 — but only via its apphost, and the daemon binds the pipe lazily.** Our submodule
  daemon is `net8.0`; running it standalone needed the **.NET 8 *runtime*** installed. Two gotchas worth writing
  down for the eventual macOS setup guide: (1) the daemon creates its `CoreFxPipe_OpenTabletDriver.Daemon` socket
  only *after* startup/tablet-detection completes (several seconds), and .NET appears to unlink the socket's
  filesystem entry after bind — so poll by **connecting**, not by `test -S` on the path; a client that connects
  too early just needs a retry (which `DaemonClient`'s connect-loop already does). (2) On this macOS 26 host the
  **`dotnet` SDK muxer** was fragile after mixing runtime installs — launching apps by their **native apphost**
  (with `DOTNET_ROOT` set) was the reliable path. Neither affects OTA at runtime; both are dev-environment notes.

**What this moves:** the two biggest external unknowns — "does OTD build/run on macOS" and "can our client
connect + `GetSettings()`" — are now **confirmed**, on real hardware. The remaining macOS work is exactly the
integration/UI surface already catalogued above (display enumeration, daemon-path discovery + ownership,
feature-gating the Windows-only cards, permissions UX, packaging/signing) — *not* any foundational risk.

**Full OTA GUI booted on macOS: ✅ (2026-07-09).** Launched the real Avalonia app (`net10`, via apphost) on
the Apple-Silicon host. It **runs cleanly — zero exceptions across several runs** — and connects live to the
daemon: the HOME page renders the sidebar (HOME / PRESETS / HOTKEYS / SCRIBBLE / ABOUT / ADVANCED) and shows
the **real detected tablet** with correct specs pulled over RPC — *Wacom Movink 13 (DTH-135), Detected,
297.76 × 169.24 mm · 8191 pressure levels · 3 buttons* (and Wacom PTH-660 as "Not detected"). Notably, the
Windows-only startup services **did not crash** — global hotkeys, tray, the foreground watcher (already behind
`Win32ForegroundAppWatcher`), and the single-instance guard are either `IsWindows()`-gated or degrade to a
harmless no-op. So the app is *runnable* on macOS today, not just buildable. Two concrete observations from
the live run:

- **Feature-gating is the real next task, now seen firsthand.** The Home "NEEDS ATTENTION" panel nags about
  *"VMulti driver not installed"* and *"…not using Windows Ink"* — both Windows-only, exactly the health-check
  entries the doc flags for gating. On macOS these should be hidden, not surfaced as fixable problems.
- **Display geometry comes back in logical points, not physical pixels.** Avalonia reported the 4K ASUS as
  1920×1080 (scaling read as 1, though the panel is 3840×2160) and the Wacom as 960×540 — i.e. logical points,
  half the physical size, but **internally consistent** (both 2:1), so Display-Mapping *proportions* stay
  correct. Any code that assumes the Win32 physical-pixel model or reads `Scaling` should be validated on macOS.

**Still not exercised:** the remaining seam *runtime* behaviours (global-hotkey registration, tray actions,
calibration-overlay placement) actively on a Mac, and a click-through of the Display-Mapping tab in the live
GUI (the monitor read itself is unit-tested + harness-verified; automating the nested Avalonia a11y tree to
open that tab wasn't worth the effort this pass).

## Progress — 2026-07-09 (continued): first platform seam landed + probe promoted

Turned the spike findings into committed code on the `macos` branch:

- **`DisplayEnumerator` extracted behind a seam (the largest functional P/Invoke gap — 8 user32/GDI sites).**
  Introduced `IDisplayEnumerator`; the existing Win32 code moved verbatim into `WindowsDisplayEnumerator`
  (zero behaviour change on Windows); added `AvaloniaScreensDisplayEnumerator` (cross-platform, via Avalonia
  `Screens`); and `DisplayEnumerator` is now a thin static facade that dispatches by `OperatingSystem.IsWindows()`.
  **All 11 call sites are untouched** — same `DisplayEnumerator.Enumerate()` shape. Builds on macOS 0/0.
- **The macOS impl reads real monitors — with friendly names.** A throwaway Avalonia harness booting the real
  macOS backend enumerated this Mac's two displays correctly: `ASUS PA329CV` (1920×1080, primary) and
  `Wacom DTH135` (960×540 @ 0,1080). So on macOS we get geometry **and** `DisplayName` — better than this doc's
  earlier "lower fidelity, names may be blank" caveat. What Avalonia does *not* give (and the record leaves
  empty, which the UI already tolerates): refresh rate, connector/port, and driving-GPU.
- **Tests:** added `DisplayEnumeratorSeamTests` (facade dispatch; the Avalonia impl degrades to an empty list
  when no window/screens exist, never throws). Full suite: **558 pass**. The **4 pre-existing failures are
  macOS-environment issues, not this change** (verified by stashing) — `ExecutablePathTests` (Windows path
  separators) and three `ProfileSwitchServiceTests`; these are exactly the "Windows-assuming tests" the
  cross-platform-verification item (#73) will need to fix.
- **`tools/DaemonProbe` promoted.** The throwaway round-trip probe is now a committed, standalone dev smoke
  test (kept out of the solution/CI) — `dotnet run --project tools/DaemonProbe`. Handy for re-checking the
  daemon transport on any platform.
- **Windows-only surface feature-gated — the macOS UI is now clean.** Two gates, both verified live on the Mac:
  - *Health catalog:* added `HealthInputs.IsWindows` (defaults true; set from `OperatingSystem.IsWindows()` in
    `HealthService`). The evaluator now skips the Windows-Ink-plugin + per-tablet "not using Windows Ink"
    checks, the VMulti check, and the manufacturer-driver-conflict check off-Windows; the cross-platform checks
    (display mapping, pen dynamics, config override, external daemon) still fire. **The macOS Home page's
    "NEEDS ATTENTION" section is now empty** where it previously nagged about VMulti + Windows Ink.
  - *ADVANCED rail:* the `WINDOWS INK PLUGIN`, `VMULTI DRIVER`, `DRIVER CLEANUP`, and `STARTUP` subpages are
    filtered out off-Windows (data-driven; a stray deep-link to a hidden tab resolves to null content, no
    crash). **The macOS rail now shows only** Daemon / Configs / Diagnostics / Console / Plugins + Developer /
    Theme. Added `HealthEvaluator` gating tests; suite **560 pass** (+2), same 4 pre-existing macOS-env failures.
- **The test suite is now fully green on macOS.** The 4 remaining failures were **test-only Windows-path
  assumptions**, not product bugs (verified): `ExecutablePathTests` used `C:\…\..\…` literals that never
  normalize off-Windows (`\` isn't a separator), and `ProfileSwitchServiceTests` keyed its fake store with
  backslash paths while the service builds snapshot paths via `Path.Combine` (`/` on macOS), so lookups missed
  and switches no-oped. Both now build OS-appropriate paths; the product code (`ExecutablePath.SameFile`,
  `ProfileSwitchService.SnapshotPath`) was correct all along. **Suite: 561 passed, 0 failed on macOS** (#73).
- **Display-Mapping fidelity validated on macOS — the logical-points concern is resolved.** The open question
  was whether OTA's Avalonia-derived geometry (logical points) matches the coordinate space the macOS daemon
  uses. Answer: **yes, exactly.** Read the live daemon's stored area (configured by OTD.app's own macOS UX) —
  the ASUS mapping was `Display W=1920 H=1080, centre (960,540)`, precisely what `AvaloniaScreensDisplayEnumerator`
  reports and what `DisplayMappingApplier.MappedCenter` computes. OTD's macOS output uses the same CoreGraphics
  points space Avalonia reports, so there is **no physical-vs-logical scale mismatch**. Confirmed live: the
  Display-Mapping tab renders both real monitors (ASUS PA329CV · Primary, Wacom DTH135) with friendly names and
  **highlights the correct mapped display** via `CurrentlyMapped` against the real daemon area. Added a regression
  test (`MacOsLogicalPointsGeometry_AgreesWithDaemonStoredArea`) encoding the real geometry + daemon area. **Suite:
  562 passed, 0 failed.**
- **The remaining OS-integration seams are macOS-safe.** Audited each live on the Mac:
  - *Tray* (`AppTray`) — already cross-platform (Avalonia `TrayIcon` + `NativeMenu`); **verified live, its icon
    appears in the macOS menu bar**. No change.
  - *Global hotkeys* (`GlobalHotkeyService`) — already self-guards (ctor `try/catch` → `_hwnd = Zero` → every
    registration no-ops off-Windows). *Profile toast* (`ProfileToast`) — already catches `DllNotFoundException`
    on its `SetWindowPos` and falls back to Avalonia `Topmost`. No change.
  - *Calibration overlay* — **fixed two latent throws**: `SetCursor(IntPtr.Zero)` ran ~30×/sec from the pulse
    timer during a hold, and the `Win32Properties` press-and-hold WndProc hook ran on open/close — all user32/
    Win32-only. Now guarded with `IsWindows()`; Avalonia's `Cursor=None` handles hiding cross-platform and the
    press-and-hold gesture doesn't exist on macOS.
  - *Foreground watcher* (`Win32ForegroundAppWatcher.Start`) — `SetWinEventHook` now no-ops off-Windows, so
    per-app switching (default-off flag) degrades to "unavailable" instead of throwing.

  Verified live: GUI boots, tray shows, tablet tabs (incl. Calibration) reachable, **zero exceptions**. Suite 562.

## Recommended next step

The daemon foundation, the display seam, a clean live GUI boot, the Windows-only-surface feature-gating, a green
test suite, validated display-mapping fidelity, **and macOS-safe OS-integration seams** are done — the macOS
**portable core is functionally proven end-to-end and hardened**: connect → profiles → area mapping → dynamics →
calibration → test, mapping to the right monitor, no Windows-only nagging, and no seam throws on a Mac. What's
left is purely **shipping**:

1. **Packaging** — `.app` bundle, signing + notarization (Apple Developer account), a macOS CI lane. This is the
   main remaining unknown (Apple tooling), independent of the app itself. Cosmetic sub-task: set a proper bundle/
   app name so the macOS app menu stops reading "Avalonia Application".
2. **Optional polish** — a macOS output-story pass (guided setup surfacing OTD's native output), a real macOS
   foreground-watcher backend (`NSWorkspace`) if per-app switching is wanted there, and higher-fidelity display
   info (Avalonia gives geometry + name but not refresh/port/GPU).

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
