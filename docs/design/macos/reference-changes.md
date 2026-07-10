# Reference changes — what the `macos` branch actually did

> Sibling of the [feasibility hub](../140-macos-feasibility.md). This is the **concrete inventory** of the
> `macos` exploration branch: the commit history grouped by theme, and a file-by-file summary mapped to the
> [plan](implementation-plan.md) phases. Use it to see *exactly* how each capability was implemented — it's the
> worked example for the real PRs.

> ⚠️ **This branch is a reference, not a merge candidate.** It's exploratory (verify-first, doc-heavy, with
> some throwaway tooling). Re-implement each phase as its own reviewed PR on a fresh branch; use the code here
> as the answer key, not the source of truth.

## Change footprint (vs. `master`)

- **21 code/test files** changed: **+891 / −398** lines (much of the churn is a verbatim move —
  `DisplayEnumerator` → `WindowsDisplayEnumerator`).
- **7 new files**: 3 display-enumeration seam files, 1 seam test, and `tools/DaemonProbe/*`.
- **`tools/DaemonProbe`** is **not in the solution** (`.slnx`) → never built by CI or the solution; zero app
  impact.
- Plus the documentation set (this `macos/` directory + the hub).

## Commit history (oldest → newest)

Themes in brackets map to plan phases.

```
f4a2049  docs: re-baseline macOS feasibility against v0.36.0            [journey]
59a27e6  docs: add spike log
844ddfb  docs: spike CONFIRMS the foundation
98d0ebd  docs: round-trip confirmed against our OWN submodule daemon    [Phase 1]
db9d1b7  refactor: extract DisplayEnumerator behind a platform seam     [Phase 0.2 / 1]
81721d8  tools: promote DaemonProbe daemon-connection smoke test        [Phase 1 tooling]
da48e3b  docs: record DisplayEnumerator seam + probe
7d164ee  docs: full OTA GUI boots + connects on macOS                   [Phase 1]
eb7023c  feat: gate the Windows-only surface off-Windows                [Phase 2]
89a372a  docs: record Windows-only feature-gating
21a9ba5  test: make path-based tests OS-appropriate (green on macOS)    [Phase 0.5]
6743139  docs: macOS test suite now green
46e459a  test: lock in macOS display-mapping coordinate fidelity        [Phase 1/3]
ab48340  docs: display-mapping fidelity validated
f8b7533  fix: guard the last Windows-only runtime seams                 [Phase 5 / 0.6]
4b182cf  docs: OS-integration seams are macOS-safe
a408b76  fix: recognise OTD's native output modes (calibration works)   [Phase 0.3 / 3]
ce7aa7f  docs: native output modes recognised → calibration works
7ced920  fix: locate the daemon by its real name (Restart works)        [Phase 0.4 / 4]
eb09483  fix: show the connected daemon's version + source on macOS     [Phase 4]
fcb46ae  docs: macOS daemon restart + version display fixed
a8929d8  fix: cover the whole mapped display for calibration            [Phase 3]
484191d  fix: calibration overlay covers the full display (menu bar)    [Phase 3]
1e19ecf  docs: calibration works end-to-end on macOS
```

## File-by-file

### Display enumeration seam — [Phase 0.2 / 1]

| File | Change |
|---|---|
| `Services/IDisplayEnumerator.cs` | **new** — the seam interface (`IReadOnlyList<DisplayInfo> Enumerate()`). |
| `Services/WindowsDisplayEnumerator.cs` | **new** — the existing Win32 GDI / DisplayConfig code, **moved verbatim** from `DisplayEnumerator`. Windows behaviour unchanged. |
| `Services/AvaloniaScreensDisplayEnumerator.cs` | **new** — cross-platform impl over Avalonia `Screens` (geometry + `DisplayName`; refresh/port/GPU left empty, which the UI tolerates). Degrades to an empty list when no window/screens exist. |
| `Services/DisplayEnumerator.cs` | reduced to a thin **static facade**: `Use(impl)` for tests; `Enumerate()` lazily picks the OS impl by `OperatingSystem.IsWindows()`. All 11 call sites unchanged. |
| `tests/…/DisplayEnumeratorSeamTests.cs` | **new** — facade dispatch + the Avalonia impl's null/throw safety. |
| `tests/…/DisplayMappingApplierTests.cs` | +`MacOsLogicalPointsGeometry_AgreesWithDaemonStoredArea` — encodes the real on-device geometry + the daemon's stored area, asserting they agree (the coordinate-fidelity guard). |

### Feature-gating — [Phase 2]

| File | Change |
|---|---|
| `Domain/Health/Health.cs` | Added `HealthInputs.IsWindows` (**default `true`**). `HealthEvaluator` skips the WinInk-plugin, per-tablet not-WinInk, VMulti, and driver-conflict checks when `!IsWindows`; keeps mapping/dynamics/config/foreign-daemon. Evaluator stays a **pure function**. |
| `Services/HealthService.cs` | Sets `IsWindows = OperatingSystem.IsWindows()` in the gathered inputs (1 line). |
| `ViewModels/AdvancedViewModel.cs` | `AppliesToCurrentOs(tab)` filters the ADVANCED rail: hides Windows Ink Plugin / VMulti / Driver Cleanup / Startup off-Windows. Returns `true` for all tabs on Windows. |
| `tests/…/HealthEvaluatorTests.cs` | +gating tests (Windows-only checks suppressed off-Windows; cross-platform issues still raised). |

### Domain generalisation (output mode) — [Phase 0.3 / 3]

| File | Change |
|---|---|
| `ViewModels/TabletDetailViewModel.cs` | Renamed `IsWinInkAbsolute/Relative` → `IsAbsoluteOutputMode/IsRelativeOutputMode`; detection changed from exact WinInk-path match to **substring `Absolute`/`Relative`** (covers OTD native modes *and* WinInk). Added `NativeAbsoluteModePath`/`NativeRelativeModePath` and `PreferredAbsolute/RelativePath` (Ink on Windows, native elsewhere). `CanCalibrate`/`IsAbsoluteMode` follow. `SelectMovement` no-ops when already in that direction (avoids churning WinInk↔native). `CanFixOutputMode` made explicitly `IsWindows()`-only. **Common Windows (WinInk) case is byte-identical.** |
| `tests/…/TabletDetailViewModelTests.cs` | +native-mode detection tests (native Absolute → calibratable; native Relative → not). |

### Daemon lifecycle — [Phase 0.4 / 4]

| File | Change |
|---|---|
| `Domain/DaemonExePaths.cs` | `DaemonExeName` is now platform-aware: `OpenTabletDriver.Daemon.exe` on Windows, extension-less elsewhere. Fixes Restart on macOS. |
| `Services/AppSession.cs` | `GetConnectedDaemonPath()` falls back to the single running daemon process when the Win32 pipe-PID is unavailable. `ReadExecutableVersion()` reads the version stamp from the sibling `…Daemon.dll` when the (native apphost) file has none. `DaemonExeMissingMessage` uses the platform-aware name. Fallbacks only trigger off-Windows. |
| `Services/DaemonLifecycleService.cs` | +`GetSingleRunningDaemonPath()` (the one running daemon's path, or null if ambiguous). Additive. |
| `tests/…/DaemonExePathsTests.cs` | Reference the platform-aware name instead of hardcoding `.exe`. |
| `tests/…/AppSessionLifecycleTests.cs` | Fake lifecycle implements the new interface member. |

### Seam runtime safety — [Phase 5 / 0.6]

| File | Change |
|---|---|
| `Services/ForegroundAppWatcher.cs` | `Win32ForegroundAppWatcher.Start()` no-ops off-Windows (`SetWinEventHook` is user32); already behind `IForegroundAppWatcher`. |
| `Views/CalibrationOverlayWindow.axaml.cs` | Guarded the user32 `SetCursor` (pulse-timer cursor re-hide) and the `Win32Properties` press-and-hold WndProc hook with `IsWindows()`. |

### Calibration overlay coverage (macOS) — [Phase 3]

| File | Change |
|---|---|
| `Views/CalibrationOverlayWindow.axaml.cs` | `PlaceOnDisplay()` split by OS: Windows keeps `WindowState.FullScreen`; macOS sizes the borderless window to the display's full bounds **and** calls `CoverFullDisplayOnMac()` — descriptor-checked ObjC interop that reaches the `NSWindow` to (a) raise it past `kCGMainMenuWindowLevel` and (b) set its frame to the full `NSScreen` frame, so the overlay truly covers the display (fixes the ~30 px menu-bar offset that misaligned calibration). Guarded so it only ever sends selectors the object responds to. |

### Test hygiene — [Phase 0.5]

| File | Change |
|---|---|
| `tests/…/ExecutablePathTests.cs` | Build absolute paths rooted for the current OS (`C:\` vs `/`) instead of Windows literals; the product `SameFile` was already correct. |
| `tests/…/ProfileSwitchServiceTests.cs` | Key the fake store via `Path.Combine` + a per-OS root (matching how the service builds snapshot paths), instead of backslash literals. |

### Tooling & docs (not app code)

| Path | Change |
|---|---|
| `tools/DaemonProbe/*` | **new**, standalone (not in `.slnx`) — mirrors `DaemonClient`'s transport to smoke-test the daemon connection headlessly. See [dev-environment.md](dev-environment.md). |
| `docs/design/140-macos-feasibility.md` + `docs/design/macos/*` | This documentation set. |

## Windows-safety summary (for reviewers)

Every code change is one of: **guarded** by `IsWindows()`/`IsMacOS()` · **defaults to Windows behaviour**
(`Health.IsWindows = true`) · **verbatim-moved** (`DisplayEnumerator`) · **additive** (new files/methods that
only run off-Windows) · **not in the solution** (`DaemonProbe`) · **test-only**. The **single** intentional
Windows behaviour change is the output-mode generalisation, and it's byte-identical for the common Windows-Ink
case — see the [hub](../140-macos-feasibility.md) merge-safety analysis for the full trace.
