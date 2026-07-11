# macOS port — status & handoff

> **Status (2026-07-10): the V1 macOS port is complete.** Phases 0–5 are implemented, verified live on
> Apple-Silicon macOS 26 (Darwin 25.5) with a Wacom Movink 13, and **merged to `master`**. **Packaging is
> split: Phase 6 (an OTD-compatible self-contained `.app`, no Apple account) is the unblocked next step;
> Phase 7 (notarization + full Developer-ID signing) is deferred to V2.** OTA on macOS now builds, connects, lists tablets,
> maps to the correct display, hides the Windows-only surface, runs pointer calibration, reports daemon
> version/source, and boots clean with every OS seam guarded.

## What's merged (Phases 0–5 on `master`)

| Phase | What it delivered | PR |
|---|---|---|
| **0** | Windows-safe prep — CA1416 enforced, `IDisplayEnumerator` seam, output-mode detection generalised, platform-aware daemon exe name, OS-portable tests + Windows/Linux CI lanes, early Win32-seam guards | #511 |
| **1** | Build + connectivity on macOS — `AvaloniaScreensDisplayEnumerator`, daemon round-trip, GUI boots, macOS CI lane | #513 |
| **2** | Feature-gating — Health checks + ADVANCED rail hide the Windows-only surface (WinInk/VMulti/DriverCleanup/Startup) off-Windows | #514 |
| **3** | Output + calibration — overlay covers the full display (NSWindow ObjC interop); display-match + capture handle negative-origin multi-monitor layouts | #515, #516, #517 |
| **4** | Daemon lifecycle — version + source on the daemon card (single-daemon-path fallback + sibling-`.dll` version read; `Domain.DaemonVersion`) | #518 |
| **5** | Seam runtime safety — `Services.PlatformShell` (reveal-in-file-manager / display-settings) replaced the unguarded `explorer.exe` launches; all other seams were already guarded; boots clean | #519 |

Plus: **Copy button on message/error dialogs** (#520) — cross-platform clipboard.

Build with `dotnet build OpenTabletArtist.slnx`; test with `dotnet test` (587 tests, green on the Windows/Linux/macOS lanes).

## Parked / follow-ups (not blockers)

- **~1% calibration pointer drift** after the affine correction — a small residual that survives on the
  Movink (worse toward the corners). Ruled out HiDPI/resolution (it's resolution-independent); the leading
  suspect is digitizer nonlinearity that an affine can't capture. **Next lever:** the grid/homography solver
  (the code currently forces affine per #486). Diagnostics (F1 pen readout, alignment-check overlay, cursor
  logging) are on branch **`diagnostics/calibration-macos`** (not for merge as-is; the CSV logging is
  scaffolding).

## Ground rules (keep these for any further macOS work)

- **Runtime guards** (`OperatingSystem.IsWindows()` / `.IsMacOS()`), **never `#if`.** One binary everywhere.
- **Keep Windows behaviour unchanged.** Every change is Windows-safe.
- **Verify live on real hardware** per each item's exit criteria. For calibration, the **calibration report is
  the coordinate-space oracle**.
- **Keep CA1416 green** (warning-as-error) and **all CI lanes green.**

## The reference branch

`origin/macos` is the **reference implementation / answer key — NOT a merge candidate** (and it predates the
Phase 3 negative-origin fixes, so its calibration would fail to capture on a display at a negative origin).
Use it to see how a capability was done; re-implement as a fresh, reviewed, Windows-safe PR.

## Packaging — Phase 6 (unblocked) then Phase 7 (deferred)

Split so the part that needs no Apple account can proceed. See
[implementation-plan.md → Phase 6](implementation-plan.md#phase-6--packaging-otd-compatible-no-apple-account).

**Phase 6 — OTD-compatible packaging (no Apple account, doable now).** Bring OTA to the level OTD itself ships
at: a self-contained `.app` with the **daemon bundled**, **ad-hoc / `rcodesign` signed** (OTD's
`eng/bash/macos/package.sh` is the reference — `rcodesign` even signs from Linux/CI), packaged as a **tarball**.
Same first-launch caveat as OTD (right-click → Open). Groundwork exists (`Application.Name`,
`scripts/bundle-macos-app.sh`, the `BundleMacApp` publish target from #520/#522/#523). Key insight: a **stable
signing identity is what makes the Input Monitoring / Accessibility grant persist across rebuilds** — the
catch-22 the port kept hitting (a fresh `dotnet build` daemon has a new cdhash → no TCC grant; OTD.app's signed
daemon keeps its). Plus a **permissions UX** for the grants (OTD ships a `PermissionHelper`).

**Phase 7 — full signing + notarization (deferred to V2).** The only part gated on external, paid decisions:
a **Developer ID Application** cert (**Apple Developer Program** membership), `notarytool` + `stapler`, and a
**CI secrets story**. Result: downloads open by double-click with no Gatekeeper prompt. (OTD does *not*
notarize today, so this is beyond OTD-parity.)

## Document map

1. [140-macos-feasibility.md](../140-macos-feasibility.md) — the hub + status snapshot.
2. [implementation-plan.md](implementation-plan.md) — the phased plan (with per-phase status).
3. [dev-environment.md](dev-environment.md) — macOS SDK/runtime setup + verification tooling.
4. [windows-specific-surface.md](windows-specific-surface.md) — the seam / P-Invoke catalog.
5. [reference-changes.md](reference-changes.md) — what the reference branch changed, file-by-file.
6. [porting-journey.md](porting-journey.md) — how the port was de-risked.
