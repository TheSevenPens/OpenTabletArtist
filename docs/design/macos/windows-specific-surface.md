# Windows-specific surface — the technical catalog

> Sibling of the [feasibility hub](../140-macos-feasibility.md). This is the **reference map** of what is
> Windows-specific in OTA, how each piece should behave on macOS, the platform-seam pattern, and the domain
> conflations to untangle. The [plan](implementation-plan.md) turns this into phases; [reference-changes.md](reference-changes.md)
> records what the `macos` branch actually did to each item.

## P/Invoke footprint

Measured on the current codebase (`grep` for `DllImport`), by library and site:

| Library | Sites | Where |
|---|---:|---|
| `user32` | 28 | display enumeration (GDI), global hotkeys, foreground watcher, clipboard, shell pen-feedback, profile toast, calibration overlay |
| `kernel32` | 10 | clipboard (`Global*`), `GetModuleHandle`, `GetNamedPipeServerProcessId` |
| `setupapi` | 4 | VMulti device detection |
| `cfgmgr32` | 1 | VMulti device detection |

**None of these are a compile-time blocker** — `DllImport` binds lazily at runtime. The risk is *runtime*:
an unguarded call throws `DllNotFoundException` on macOS. The functional gaps that need a real macOS
implementation (not just a guard) are **display enumeration** (done) and **daemon-ownership/version**
(done via fallback). The rest either have a cross-platform Avalonia equivalent, degrade to a no-op, or belong
to a feature that's hidden on macOS.

> **Recommendation carried into the plan:** annotate these classes with `[SupportedOSPlatform("windows")]` and
> enable the platform-compatibility analyzer (CA1416), so unguarded calls become **compile-time** errors. This
> would have caught, at build time, the two runtime bugs the port hit (`SetCursor` in the calibration overlay,
> the `.exe` daemon name).
>
> **Caveat (from the [#510 review](https://github.com/TheSevenPens/OpenTabletArtist/issues/510)):** CA1416
> flags not just these classes but every **call site** that constructs them from unannotated code — e.g.
> `MainViewModel` `new`-ing `GlobalHotkeyService` / `Win32ForegroundAppWatcher`, and
> `CalibrationOverlayWindow`'s `Win32Properties` use. Annotating the Win32 classes alone will **not** make the
> build green; expect to add call-site `IsWindows()` guards or `[UnsupportedOSPlatformGuard]`. Phase 0.1's real
> exit criterion is "builds with the analyzer on as warning-as-error," not "attributes added."

## Windows shell hooks (not P/Invoke, but Windows-specific)

Beyond `DllImport`, several view-models shell out to Windows-only executables / URI schemes. These are
**best-effort** (wrapped in try/catch), so they don't crash on macOS, but they fail silently or loudly and are
easy to miss because they aren't in the P/Invoke count:

| Site | What | macOS |
|---|---|---|
| `PluginsViewModel` · `PresetsViewModel` · `CustomTabletConfigsViewModel` (open-folder) | `Process.Start("explorer.exe", dir)` | `Process.Start` with `UseShellExecute` on a directory, or `open` — a small "reveal in file manager" seam |
| `TabletDetailViewModel` (display settings) | `ms-settings:display` | `x-apple.systempreferences:` / no-op |
| `MainViewModel` (own-exe match for per-app switching) | forces `ProcessName + ".exe"` | bundle-id identity (per-app switching is Windows-only for now anyway) |

These land in the Phase 5 guard sweep (a reveal-in-file-manager seam), not Phase 0 — recorded here so the
catalog is complete.

## Service-by-service catalog

Status legend: **seamed** (behind an interface with a macOS impl) · **gated** (Windows-only UI hidden) ·
**guarded** (no-ops/degrades off-Windows) · **blocker** (Windows-only by nature; hidden on macOS) ·
**todo** (still needs a macOS backend or seam).

> **The "Status in branch" column was the `macos` branch's *end-state* — and it is now `master`'s too.** The
> guarding it referenced (`Win32ForegroundAppWatcher.Start` around `SetWinEventHook`, the calibration-overlay
> `SetCursor` / `Win32Properties` calls, and `GlobalHotkeyService`'s ctor) landed in **Phase 0.6 (#511)** and
> **Phase 5 (#519)**, both merged. So the "guarded" labels below now describe `master`.

| Component | What it does on Windows | macOS approach | Status in branch |
|---|---|---|---|
| `Services/DisplayEnumerator` | Monitor geometry + friendly names + connector/GPU via GDI / DisplayConfig | Avalonia `Screens` (geometry + friendly name; no refresh/port/GPU) | **seamed** — `IDisplayEnumerator` + Win32/Avalonia impls + facade |
| `Services/DaemonClient` | Connects over the named pipe `OpenTabletDriver.Daemon`; `GetNamedPipeServerProcessId` for ownership | .NET named pipe → Unix-domain-socket emulation connects as-is; pipe-PID is Win32-only | **works unchanged** for transport; ownership handled below |
| `Services/DaemonLifecycleService` + `Domain/DaemonExePaths` | Finds/launches the daemon **exe**; scans by process name | Apphost has no `.exe` extension; launch/discovery differ | **fixed** — platform-aware exe name; single-running-process fallback |
| `Services/AppSession` (daemon source/version) | PID via `GetNamedPipeServerProcessId`, version via `FileVersionInfo` on the exe | Both Win32-only; native apphost has no version stamp | **fixed** — process-list fallback + sibling-`.dll` version read |
| `Services/GlobalHotkeyService` | System-wide hotkeys via `RegisterHotKey` + message-only window (user32); backs profile-switch/monitor-cycle chords | macOS needs Carbon `RegisterEventHotKey` or a `CGEventTap` (Accessibility grant) | **guarded** — ctor try/catch → no-op; **todo:** macOS backend |
| `Services/ForegroundAppWatcher` | Per-app profile switching via `SetWinEventHook` (user32) | macOS `NSWorkspace.didActivateApplicationNotification` (bundle-id vs exe-name) | **guarded** — already behind `IForegroundAppWatcher`; `Start()` no-ops off-Windows; **todo:** macOS backend |
| `Services/StartupService` + `Services/StartMenuShortcut` | Run-at-startup via HKCU `Run`; Start-menu shortcut | macOS `LaunchAgent` plist / `SMAppService` | **gated** — `IsSupported` hides the card; tab hidden on macOS; **todo:** macOS impl if wanted |
| `Services/SingleInstance` | Single-instance guard via named `Mutex` + `EventWaitHandle` | Named mutexes are Win32-only in .NET | **guarded** — gated to skip off-Windows (every launch "primary"); **todo:** file-lock/UDS impl |
| `Services/ProcessElevation` | "Running as admin" via `WindowsIdentity` | Concept differs (root) | **guarded** — returns `false` off-Windows → health check inert |
| `Helpers/ProfileToast` | Topmost non-activating toast; `SetWindowPos` HWND_TOPMOST | Avalonia `Topmost` | **guarded** — catches `DllNotFoundException` → Avalonia topmost |
| `Services/ShellPenFeedback` | Suppresses shell pen/touch feedback rings (`SetWindowFeedbackSetting`, user32) | No macOS analogue; not needed | **guarded** — `IsWindows()` no-op |
| `Services/ClipboardImage` / `ClipboardText` | Win32 clipboard (kernel32 `Global*` + user32) for dev-screenshot / log-copy | Avalonia `IClipboard` / `NSPasteboard` | **guarded** — `IsWindows()` → returns `false`; **todo (low):** Avalonia clipboard backend |
| `Views/CalibrationOverlayWindow` | Full-display overlay; `SetCursor` + `Win32Properties` press-and-hold hook; `WindowState.FullScreen` | Avalonia `Cursor=None`; borderless full-frame via ObjC (`NSWindow` level + frame) | **fixed** — guards + full-display coverage |
| Health catalog (`Domain/Health`, `Services/HealthService`) | Checks VMulti, Windows Ink, driver conflicts, "running as admin" | Most don't apply on macOS | **gated** — `IsWindows` input flag skips Windows-only checks |
| `Services/DriverConflictMonitor` / `TabletDriverCleanupRunner` | Parses OTD's Windows manufacturer-driver warnings; runs the Windows cleanup tool | N/A on macOS | **gated** — page hidden; health check skipped |

## The two real blockers (Windows-only *by nature*)

These are not seams to fill — they're concepts that don't exist on macOS, and the daemon delivers pen input
another way there. Both are **hidden** on macOS.

- **VMulti** (`VMultiDetector` / `VMultiInstaller`, `install_hiddriver.bat`) — a Windows HID driver that the
  Windows Ink plugin injects pressure/tilt through. No macOS equivalent and not needed: on macOS OTD emits
  input through its own output stage.
- **Windows Ink output mode** (VoiD's `WinInkAbsoluteMode`/`WinInkRelativeMode` plugin) — a Windows concept.
  macOS pressure reaches apps through OTD's **native** absolute/relative output. The output-mode UI needs a
  macOS-appropriate story (see below), and the Windows-Ink plugin management is hidden.

Net: a chunk of the app's *Windows* value (VMulti + Windows Ink management, creative-focused recommendations)
is Windows-specific. The portable core — connect, profiles, area mapping, dynamics, calibration, test — is a
worthwhile app on macOS, just a different product surface.

## The platform-seam pattern (worked example: DisplayEnumerator)

The pattern the branch established, and that the plan extends to the other OS services:

```
IDisplayEnumerator                 // the seam (one interface per concern)
├── WindowsDisplayEnumerator       // existing Win32 code, moved verbatim
└── AvaloniaScreensDisplayEnumerator  // cross-platform impl (Avalonia Screens)

DisplayEnumerator (static facade)  // dispatches by OperatingSystem.IsWindows();
                                   // .Use(impl) for tests; all call sites unchanged
```

Why this shape:
- **Interface-per-concern** (not one god-interface) — interface segregation; each seam is independently
  testable and swappable.
- **A thin static facade** preserves the existing `DisplayEnumerator.Enumerate()` call sites (there were 11),
  so the refactor is behaviour-preserving on Windows and diff-small.
- **The platform decision lives in one factory**, not scattered `IsWindows()` at call sites.
- **`.Use(impl)`** makes it unit-testable without a DI container (the app has none today).

The same treatment is recommended for `IGlobalHotkeys`, `IStartupManager`, `ISingleInstance`, `IElevation`,
and daemon launch/discovery. `IForegroundAppWatcher` and `IDaemonLifecycleService` already exist as interfaces
— good precedent.

## Domain conflations to untangle

These are code-quality issues independent of macOS; untangling them is what makes the platform behaviour
correct *and* the domain cleaner.

- **Output mode ≡ Windows Ink.** `IsAbsoluteMode`/`CanCalibrate` were defined as "is on the VoiD WinInk
  *absolute* plugin path". That conflates the domain concept (absolute vs. relative movement) with a specific
  Windows implementation. The branch generalises detection to "the mode path carries the word `Absolute`/
  `Relative`" (covering OTD's native modes *and* WinInk) and selects a **platform-preferred** mode (Ink on
  Windows, native elsewhere). "Fix output mode → Windows Ink" becomes correctly Windows-only.
- **Health evaluation ≡ platform checks inline.** The branch keeps `HealthEvaluator` a **pure function** by
  passing an `IsWindows` flag in its input snapshot (defaulting `true`), rather than calling
  `OperatingSystem.IsWindows()` inside it. Capability decisions belong at the edges; the core stays
  deterministic and unit-testable.

## macOS output story (native output)

On macOS the output-mode UI simplifies to OTD's **native Absolute / Relative** — no VMulti, no Windows Ink.
The branch does the minimum to make this correct (native modes recognised → toggle + calibration work). A
fuller "macOS output story" (guided first-run surfacing native output, mac-specific copy that doesn't lean on
Windows-Ink terminology) is optional polish, tracked in the plan's later phases.

## Other macOS realities (packaging-era)

- **Permissions.** macOS needs **Accessibility** and/or **Input Monitoring** grants for tablet input + cursor
  control; the app/daemon must guide the user through System Settings. (OTD's own `PermissionHelper` exists.)
- **Packaging.** `.app` bundle + **code-signing + notarization** (Apple Developer account) for distribution;
  Gatekeeper blocks unsigned apps. The daemon should be bundled **self-contained** (its own runtime) so it
  launches without a system .NET install. The release workflow is Windows-only today → needs a macOS CI lane.
- **App identity.** The macOS app menu currently reads "Avalonia Application" — set a proper bundle/app name
  during packaging.
