# macOS port — status & handoff

> **Status (2026-07-10): the V1 macOS port is complete.** Phases 0–5 are implemented, verified live on
> Apple-Silicon macOS 26 (Darwin 25.5) with a Wacom Movink 13, and **merged to `master`**. **Phase 6
> (packaging + release) is deferred to a V2 milestone.** OTA on macOS now builds, connects, lists tablets,
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

## V2 — where the next milestone picks up (Phase 6: packaging + release)

Deferred, and gated on decisions this repo can't make. When V2 starts, see
[implementation-plan.md → Phase 6](implementation-plan.md#phase-6--packaging--release). It needs:

- A distributable **`.app`** (name/icon — stop the menu bar reading "Avalonia Application").
- A **self-contained bundled daemon** (its own runtime) so it launches without a system .NET 8.
- **Code-signing + notarization** — an **Apple Developer Program** membership + a **CI secrets story**
  (signing identity + notarization credentials).
- A **permissions UX** for the Accessibility / Input Monitoring grants (the catch-22 the port kept hitting: a
  freshly-built daemon binary has no TCC grants; OTD.app's signed daemon does).

## Document map

1. [140-macos-feasibility.md](../140-macos-feasibility.md) — the hub + status snapshot.
2. [implementation-plan.md](implementation-plan.md) — the phased plan (with per-phase status).
3. [dev-environment.md](dev-environment.md) — macOS SDK/runtime setup + verification tooling.
4. [windows-specific-surface.md](windows-specific-surface.md) — the seam / P-Invoke catalog.
5. [reference-changes.md](reference-changes.md) — what the reference branch changed, file-by-file.
6. [porting-journey.md](porting-journey.md) — how the port was de-risked.
