# macOS implementation plan — phased, Windows-first

> Sibling of the [feasibility hub](../140-macos-feasibility.md). This is the **actionable plan**: prepare the
> Windows codebase first (no-regret refactors), then land macOS support in small, reviewable, Windows-safe
> increments that converge on what the [`macos` reference branch](reference-changes.md) already proves works.

> **Revised 2026-07-10 to fold in the [#510 plan review](https://github.com/TheSevenPens/OpenTabletArtist/issues/510).**
> That review verified the plan's factual claims against `master` (they hold) and raised three blockers, all
> about plan accuracy rather than architecture: (1) Phase 0.3 is *not* zero-Windows-behaviour-change — it alters
> the uncommon native-absolute case; (2) the foreground-watcher / calibration-overlay guards were sequenced into
> Phase 5 but belong in the defensive 0.6 wave; (3) Phase 0.5's test-hygiene scope was under-counted. These are
> now reflected below.

## How to use this plan

- The `macos` branch is the **worked reference**, not the thing to merge. For each phase below, the
  corresponding branch commits/files (see [reference-changes.md](reference-changes.md)) show *exactly* one way
  to do it — but each phase should be re-implemented as its own small PR on a fresh branch, reviewed on its
  own merits.
- **Every Phase 0 and most later PRs are Windows-behaviour-preserving.** The one deliberate Windows change is
  the output-mode generalisation in **Phase 0.3** (the code lands in 0.3; Phase 3 only adds the macOS overlay +
  verification on top). It is **byte-identical for the common Windows-Ink case** and changes behaviour **only**
  for the uncommon config of a Windows tablet on OTD's *native* absolute mode — clicking *Absolute* no longer
  force-swaps it to Windows Ink (only *Fix output mode* does). That's an improvement, not a regression, and the
  0.3 PR must lock it with a Windows regression test (see [Phase 0.3](#phase-0--prepare-the-windows-codebase-no-macos-behaviour-yet)).
- **Verification is per-phase and mandatory.** The branch was built verify-first; keep that discipline. The
  tooling is in [dev-environment.md](dev-environment.md).

## Guiding principles

1. **Runtime guards, not `#if`.** Keep one binary that runs everywhere; guard with
   `OperatingSystem.IsWindows()` / `.IsMacOS()`. This is why the app compiles unmodified on macOS.
2. **Seam per concern.** One interface per OS integration; a thin static facade preserves call sites; the
   platform decision lives in one factory. (`DisplayEnumerator` is the template.)
3. **Keep pure logic pure.** Evaluators/domain take capability *inputs*, not platform *checks*. (`HealthEvaluator`
   takes an `IsWindows` flag.)
4. **No hidden Windows assumptions.** No hardcoded `.exe` / `\` / `C:\` / registry paths; no domain concept
   bound to a Windows implementation.
5. **Fail visible, degrade gracefully.** Prefer explicit guards over swallow-everything `try/catch`; give any
   unavailable feature a clear "unavailable on this platform" state, not a silent no-op that looks broken.

---

## Phase 0 — Prepare the Windows codebase (no macOS behaviour yet)

**Goal:** make `master` port-ready and cleaner, with **zero** macOS code and **Windows-default-preserving**
behaviour — every change keeps the existing Windows result **except** the one documented output-mode
generalisation in 0.3, which changes only the uncommon native-absolute case (see 0.3 below). Each item is an
independent, mergeable PR. After Phase 0, the later phases are small and low-risk.

| # | PR | What | Why it's a general win | Windows-safe? |
|---|---|---|---|---|
| 0.1 | **Enable CA1416 + annotate** | Turn on the platform-compatibility analyzer; add `[SupportedOSPlatform("windows")]` to the Windows-only classes (see the [surface catalog](windows-specific-surface.md)) | Unguarded Win32 calls become **compile-time** errors; documents intent | ✅ no runtime change — but expect call-site fallout ¹ |
| 0.2 | **Extract the `IDisplayEnumerator` seam (Windows impl only)** | Introduce **`IDisplayEnumerator`** — the one load-bearing seam Phase 1 needs — and move the Win32 code behind it; static facade + `.Use()` for tests. **Defer** the other seams (`IGlobalHotkeys` / `IStartupManager` / `ISingleInstance` / `IElevation`) until a second impl or a test needs them ² | Untestable static P/Invoke becomes unit-testable; one factory owns the platform choice | ✅ behaviour-preserving refactor |
| 0.3 | **Generalise domain conflations** | Output mode: detect Absolute/Relative by mode path (native + WinInk), platform-preferred selection; Health: pass an `IsWindows` input flag (default `true`) instead of inline checks | Cleaner domain modelling; pure, testable evaluator; fixes a mislabeled toggle for native-absolute tablets on Windows too | ⚠️ byte-identical for Windows-Ink; changes native-absolute ³ |
| 0.4 | **Kill hardcoded platform strings** | Platform-aware `DaemonExeName`; audit for `.exe` / `\` / `C:\` / registry / `explorer.exe` / `ms-settings:` literals | No magic strings encoding a platform; prevents the class of bug that broke Restart | ✅ `.exe` on Windows |
| 0.5 | **Test + CI hygiene** | Audit **all** test path literals — OS-root the portable ones (`Path.Combine` + per-OS root), mark/skip the genuinely Windows-only ones off-Windows; add a **Linux `dotnet build` + `dotnet test` CI lane** | Portable suite; catches "compiles/passes only on Windows" regressions immediately | ✅ Windows-identical paths ⁴ |
| 0.6 | **Defensive fallbacks + explicit guards** | Add graceful fallbacks to single-point-of-failure OS calls (daemon discovery/version); replace `GlobalHotkeyService`'s empty ctor `catch` with an explicit `IsWindows()` guard; **guard the early-reachable Win32 seams now** — `Win32ForegroundAppWatcher.Start` and the calibration-overlay `SetCursor` / `Win32Properties` hook ⁵ | Reliability on Windows too (elevated processes, AV, etc.); predictable failure; nothing throws before its phase lands | ✅ same result on Windows |

**Exit criteria:** `master` builds + all tests pass on Windows **and** on the new Linux CI lane; the
`IDisplayEnumerator` seam exists with a Windows impl; **CA1416 is green as warning-as-error on Windows CI**
(analyzer *on*, not just attributes added); no hardcoded platform strings remain.

> After Phase 0, `master` is a strictly better Windows codebase that happens to be macOS-ready.

**Notes (from the [#510 review](https://github.com/TheSevenPens/OpenTabletArtist/issues/510)):**

¹ **CA1416 fallout is call-site-wide, not just the annotated classes.** Enabling the analyzer flags every
  site that constructs a Windows-only type from unannotated code (e.g. `MainViewModel` constructing
  `GlobalHotkeyService` / `Win32ForegroundAppWatcher`, and `CalibrationOverlayWindow`'s `Win32Properties`
  use). Attributes on the Win32 classes alone will **not** make the build green — expect to add call-site
  `IsWindows()` guards or `[UnsupportedOSPlatformGuard]`. 0.1's verification is "solution builds with the
  analyzer on as warning-as-error on Windows CI," not "attributes added."

² **Scope 0.2 to `IDisplayEnumerator` only.** It's the sole seam Phase 1 depends on (11 call sites, all
  through the static `Enumerate()`). Extracting the other four in the same wave inflates review surface for
  no near-term gain; they land when a macOS impl or a test actually requires them (Phase 5).

³ **0.3 is the one intentional Windows behaviour change — it is not zero-change.** WinInk Absolute/Relative
  is byte-identical, but on `master` clicking *Absolute* always applies `WinInkAbsoluteMode`; after 0.3, if the
  profile is already on any Absolute path the click no-ops and only *Fix output mode* moves native → WinInk.
  **The 0.3 PR's acceptance gate is a Windows regression test** locking: (a) WinInk Absolute/Relative unchanged,
  (b) native-Absolute → Absolute card checked + `CanCalibrate` true + an *Absolute* click does not churn to
  WinInk, (c) `FixOutputMode` still forces WinInk.

⁴ **0.5's scope is wider than two files.** Beyond `ExecutablePathTests` / `ProfileSwitchServiceTests`, at
  least `DaemonExePathsTests` (also `C:` roots), `PerAppProfileStoreTests`, `PressurePluginInstallerTests`,
  `WindowsInkBundledInstallerTests`, `TabletConfigNamingTests`, `StartupServiceTests`, `PresetsViewModelTests`,
  and `ConflictingDriverParserTests` bake Windows path/`.exe` literals. The audit (and an explicit
  "portable vs Windows-only test" policy) **is** the exit criterion, alongside the lane. Also pin the Linux
  runner image and whether Avalonia/Skia tests need headless setup (`xvfb-run` / Avalonia headless) — a lane
  that goes red for display reasons is worse than none.

⁵ **These guards moved up from Phase 5 (blocker #2).** `Win32ForegroundAppWatcher.Start` calls
  `SetWinEventHook` with no guard on `master`; it's masked today only because `FeatureFlags.PerAppProfiles` is
  off, so it's future-proofing rather than a live crash — but it belongs in the defensive wave, not Phase 5.
  Phase 5 keeps the remaining guard *sweep* + the optional macOS backends.

---

## Phase 1 — Build + connectivity on macOS

**Goal:** OTA compiles, launches, and connects to the daemon on macOS.

- Add `AvaloniaScreensDisplayEnumerator` (the `IDisplayEnumerator` macOS impl from Phase 0's seam).
- Confirm the OTD submodule + OTA build on macOS (they do — the OTD graph is port-clean).
- Extend CI to a **macOS build lane**.
- Prove the daemon round-trip with `tools/DaemonProbe` (`GetTablets` / `GetSettings`).

**Verification:** `dotnet build` 0/0 on macOS; probe round-trips; the GUI boots and shows the detected tablet
with zero exceptions. **Exit:** OTA runs and connects live on macOS.

**Risk:** low. The branch proved every step. The only external dependency is the daemon (bundled or a running
OTD.app).

---

## Phase 2 — Feature-gating (a usable, un-nagging macOS UI)

**Goal:** the macOS UI shows only what applies — no VMulti/Windows-Ink nagging, no dead Windows-only pages.

- Gate the **health catalog** off-Windows (WinInk plugin, VMulti, driver-conflict), keeping the
  cross-platform checks (display mapping, pen dynamics, config override, external daemon). The
  "running as admin" check needs no explicit gate — `ProcessElevation.IsElevated` already returns `false`
  off-Windows, so it goes inert on its own (don't claim the `IsWindows` flag suppresses it; the reference
  branch leaves that check ungated for exactly this reason).
- Filter the **ADVANCED rail** off-Windows (hide Windows Ink Plugin / VMulti Driver / Driver Cleanup / Startup).

**Verification (live):** the macOS Home "Needs attention" panel is empty where it previously nagged; the
ADVANCED rail shows only Daemon / Configs / Diagnostics / Console / Plugins + Developer / Theme.
**Exit:** macOS launches to a clean, applicable UI.

**Risk:** low. Pure gating; Windows unaffected (defaults to showing everything).

---

## Phase 3 — macOS output story + calibration

**Goal:** the tablet's output mode reads correctly and **calibration runs and aligns** on macOS.

- With output-mode detection generalised (Phase 0.3), confirm a macOS tablet on OTD's native `AbsoluteMode`
  reads as **Absolute**, the movement toggle is correct, and **calibration is available**.
- Fix the **calibration overlay coverage** on macOS: `WindowState.FullScreen` only covers the working area and
  AppKit constrains the window below the menu bar (the ~30 px offset that broke alignment). Reach the
  `NSWindow` (descriptor-checked ObjC interop) to raise it above the menu-bar level and set its frame to the
  full `NSScreen` frame. The calibration overlay's user32 calls (`SetCursor`, `Win32Properties` hook) are
  already guarded in Phase 0.6 (moved up from Phase 5), so this phase only adds the macOS coverage path.

**Verification (live, with the pen):** menu bar covered; the calibration report shows small, centred deltas
(no systematic vertical offset); the cursor tracks the nib after applying. Use the **calibration report as the
coordinate-space oracle** — a systematic offset means the overlay isn't covering the full display.
**Exit:** a real calibration improves alignment on a macOS pen display.

**Risk:** medium — this is the one area with genuine macOS-specific complexity (window coverage + ObjC
interop). The branch's fix is the reference; keep the interop descriptor-checked and guarded so it can never
fire an unrecognised selector.

---

## Phase 4 — Daemon lifecycle on macOS

**Goal:** the daemon card's controls and status work on macOS.

- Locate/launch the daemon by its real (extension-less) name (Phase 0.4).
- Show the connected daemon's **version + source** on macOS: fall back from the Win32 pipe-PID lookup to the
  single running daemon process, and read the version stamp from the sibling `OpenTabletDriver.Daemon.dll`
  (the native apphost has no version resource).
- Confirm **Restart** stops the running daemon and launches the bundled one.

**Verification (live):** the card reads "Daemon running v0.6.7 · Bundled daemon" for our build (and an
external daemon's real version + "external" for a pre-existing one); Restart works. **Exit:** daemon
Start/Stop/Restart + version/source correct on macOS.

**Risk:** low–medium. In dev, a submodule-built daemon needs the .NET 8 runtime; a **self-contained** bundled
daemon (Phase 6) removes that dependency for shipped builds.

---

## Phase 5 — Seam runtime safety + optional macOS backends

**Goal:** nothing throws off-Windows; unavailable features degrade cleanly; optionally add real macOS backends.

- **Guarding (required):** ensure every OS seam no-ops/degrades off-Windows. The early-reachable ones
  (`Win32ForegroundAppWatcher.Start`, the calibration-overlay user32 calls) are already handled in Phase 0.6;
  this phase is the **sweep** of the rest — global hotkeys, tray (already cross-platform via Avalonia
  `TrayIcon` — a menu-bar item on macOS), single-instance, elevation, toast, shell-feedback, clipboard, and
  the `explorer.exe` / `ms-settings:` shell hooks (a small "reveal in file manager" seam).
- **Backends (optional, incremental):**
  - Global hotkeys → Carbon `RegisterEventHotKey` / `CGEventTap` (Accessibility grant).
  - Per-app switching → `NSWorkspace.didActivateApplicationNotification` (bundle-id identity).
  - Run-at-startup → `LaunchAgent` plist / `SMAppService`.
  - Single-instance → file-lock or Unix-domain-socket.
  - Clipboard → Avalonia `IClipboard`.

**Verification (live):** GUI boots and all seams exercise without exceptions; each unimplemented backend shows
a graceful "unavailable on macOS" state rather than a silent no-op. **Exit:** no seam throws; the app is
robust across every OS-integration path.

**Risk:** guarding is low-risk; each backend is an independent, optional add-on.

---

## Phase 6 — Packaging + release

**Goal:** a distributable macOS build.

- **`.app` bundle** with a proper name/icon (stop the menu bar reading "Avalonia Application").
- **Self-contained bundled daemon** (its own runtime) so it launches without a system .NET install.
- **Code-signing + notarization** (Apple Developer account) so Gatekeeper allows it.
- **Permissions UX** — guide the user through Accessibility / Input Monitoring grants (OTD ships a
  `PermissionHelper`).
- **macOS CI lane** — build + test + package (and ideally signed/notarized artifacts).

**Verification:** a clean-machine install runs without Gatekeeper blocks; permissions flow is clear; the daemon
launches self-contained. **Exit:** a shippable macOS release.

**Risk:** this is the **main remaining unknown** — Apple tooling (signing/notarization) and the release
workflow, independent of the app itself. Everything upstream is proven. Concretely, notarization needs an
**Apple Developer Program membership** (paid) and a **CI secrets story** (signing identity + notarization
credentials in the pipeline), not just "tooling" — treat both as external gates in the exit criteria.

---

## Dependency graph & sequencing

```
Phase 0 (prep, Windows-only) ─┬─> Phase 1 (build+connect)
                              │      └─> Phase 2 (gating)
                              │      └─> Phase 3 (output+calibration)
                              │      └─> Phase 4 (daemon lifecycle)
                              └─────────> Phase 5 (seam safety; some items overlap 0.6)
Phases 1–5 (a usable macOS app) ───────> Phase 6 (packaging/release)
```

- Phase 0 gates everything and is the highest-value work regardless of macOS.
- Phases 1–5 are largely independent once Phase 0 lands and can be parallelised across reviewers; Phase 2/3/4
  each stand alone behind the Phase 0 seams/generalisations.
- Phase 6 needs a usable app (1–5) and is the only phase blocked on external (Apple) factors.

## Suggested PR slicing (small, reviewable)

Roughly one PR per plan row / per phase-bullet. Concretely, mirroring the reference branch but re-implemented:

1. CA1416 + `[SupportedOSPlatform]` annotations (expect call-site-guard fallout — see note ¹).
2. Extract `IDisplayEnumerator` (+ facade) — Windows impl only.
3. *(Deferred)* Extract remaining OS seams (`IGlobalHotkeys`, `IStartupManager`, `ISingleInstance`,
   `IElevation`) — only when a second impl or test needs them; splitting **per interface** is safer than one
   PR if pursued.
4. Generalise output-mode detection (with the Windows regression test — note ³); make `HealthEvaluator` take `IsWindows`.
5. Platform-aware daemon exe name + message; audit hardcoded strings.
6. Test path hygiene + Linux CI lane.
7. Defensive daemon discovery/version fallbacks; replace swallow-`catch` with guards.
8. `AvaloniaScreensDisplayEnumerator` + macOS build/CI + probe (Phase 1).
9. Feature-gate health catalog + ADVANCED rail (Phase 2).
10. Calibration overlay macOS coverage + native output-mode wiring (Phase 3).
11. Daemon version/source/restart on macOS (Phase 4).
12. Seam guards sweep (+ optional backends) (Phase 5).
13. Packaging (Phase 6, likely several PRs).

## Definition of done (parity with the branch)

macOS build: `dotnet build` 0/0 · suite green on macOS **and** Windows · GUI boots + connects + shows the real
tablet · maps to the correct display · native output mode + working calibration · daemon version/source/restart
correct · Windows-only surface hidden · no seam throws · (Phase 6) a signed, notarized `.app`.
