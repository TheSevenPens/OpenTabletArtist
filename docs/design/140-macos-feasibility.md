# macOS support for OpenTabletArtist (#140)

> **Status (2026-07-10): V1 port COMPLETE and merged to `master`.**
> Phases 0–5 shipped as reviewed, Windows-safe PRs (#511, #513, #514, #515/#516/#517, #518, #519) and are
> verified live on Apple-Silicon macOS with a Wacom Movink 13: OTA compiles, connects to the OpenTabletDriver
> daemon, detects the real tablet, maps to the correct display, switches output mode, **calibrates**, reports
> daemon version/source, and boots clean with every OS seam guarded — the Windows-only surface hidden and the
> full suite green on all three CI lanes. **Packaging is split — Phase 6 (an OTD-compatible self-contained
> `.app`, no Apple account) is the unblocked next step; Phase 7 (notarization + full signing) is deferred to
> V2**; a ~1% calibration drift is parked. See [macos/HANDOFF.md](macos/HANDOFF.md) for the full status. This
> document is the historical feasibility record.

This document is the **hub**. The detail lives in focused sub-documents under [`macos/`](macos/):

| Document | What it's for |
|---|---|
| [macos/porting-journey.md](macos/porting-journey.md) | **How we got here** — the process, what we started from, what had to change, the gotchas, and the lessons. Read this to understand *why* the plan is shaped the way it is. |
| [macos/windows-specific-surface.md](macos/windows-specific-surface.md) | **What is Windows-specific** — the P/Invoke footprint, the service-by-service catalog, the two real blockers, the platform-seam pattern, and the domain conflations to untangle. The technical reference. |
| [macos/implementation-plan.md](macos/implementation-plan.md) | **The phased plan** — starts by preparing the Windows codebase (no-regret refactors), then incremental phases that bring `master` to parity with this branch. Each phase has scope, verification, and exit criteria. |
| [macos/reference-changes.md](macos/reference-changes.md) | **What this branch actually changed** — the commit history and a file-by-file inventory, mapped to the plan phases. The concrete reference for PR authors. |
| [macos/dev-environment.md](macos/dev-environment.md) | **macOS dev setup + verification tooling** — SDK/runtime bootstrap, the environment quirks, and how to prove each capability (daemon probe, screens harness, live GUI, calibration report). |

> **This branch is an exploratory *reference implementation*, not a merge candidate.** It was built
> verify-first on real hardware to de-risk the port and discover the true shape of the work. The intended
> path forward (see the plan) is to prepare `master` and then land the changes as small, reviewed,
> Windows-safe PRs on a fresh branch, using this branch as the worked example.

---

## The one-paragraph story

OTA is an Avalonia (`net10`) desktop app that drives the OpenTabletDriver daemon (pinned submodule, `net8`,
**v0.6.7**). Avalonia already makes the UI cross-platform, the `Domain/` layer is pure math, and the OTD
plugins are platform-neutral. The cost of a macOS port is concentrated in the **OS-integration layer**
(display enumeration, daemon lifecycle, hotkeys, tray, single-instance) and in features that are
**Windows-only by nature** (VMulti, the Windows Ink output mode). Crucially, the app uses **runtime**
`OperatingSystem.IsWindows()` guards rather than `#if WINDOWS`, and nearly every OS seam already lives in its
own service class — so it **compiled on macOS with zero source changes**, and the port became a matter of
adding platform implementations behind seams, gating Windows-only UI, and fixing a handful of runtime
assumptions. The `macos` branch does exactly that and proves it works.

## What "macOS support" means here

The realistic target is **"the same app with a macOS-appropriate output story"**, not a line-for-line port:

- **Ships on macOS:** connect to the daemon · view/detect tablets · area mapping to the right display ·
  pen dynamics (pressure/smoothing) · calibration · test/diagnostics · profiles/presets · tray.
- **Hidden on macOS (Windows-only by nature):** VMulti driver management · Windows Ink plugin management ·
  Windows manufacturer-driver cleanup · run-at-startup (registry). The daemon delivers pen input through
  OTD's **native** output on macOS, so these concepts don't apply.
- **Deferred / graceful-unavailable:** global hotkeys and per-app profile switching (need macOS backends —
  Carbon/`CGEventTap`, `NSWorkspace` — but degrade to no-ops until then).

## Status snapshot (what the branch proves)

| Capability | Status on macOS |
|---|---|
| OTD daemon builds from the pinned submodule (`net8`, arm64) | ✅ 0 warnings / 0 errors |
| OTA app builds (`net10`) | ✅ 0 warnings / 0 errors — no compile-time Windows blocker |
| Daemon round-trip (`GetTablets` / `GetSettings` over the named-pipe → Unix-socket transport) | ✅ against both OTD.app's daemon and our own submodule build |
| Full GUI boots + connects live, shows the real tablet | ✅ zero exceptions; Wacom Movink 13 detected with correct specs |
| Display mapping fidelity (Avalonia points vs. daemon coordinates) | ✅ exact match — no scaling mismatch |
| Windows-only surface hidden (VMulti / Ink / Driver-cleanup / Startup + health nags) | ✅ gated off |
| Output mode: native Absolute/Relative recognised → calibration available | ✅ |
| Calibration runs and improves alignment (overlay covers the full display; capture handles negative-origin layouts) | ✅ live-verified with the pen; a ~1% residual drift is parked (see [HANDOFF](macos/HANDOFF.md)) |
| Daemon lifecycle: correct exe name, Restart launches the bundled daemon, version + source shown | ✅ live-verified (external v0.6.6.2 / bundled v0.6.7) |
| OS-integration seams (hotkeys, tray, watcher, overlay, shell hooks) safe off-Windows | ✅ guarded; tray works as a menu-bar item; reveal-in-file-manager opens Finder |
| Test suite | ✅ **587 passing, 0 failing** on the Windows/Linux/macOS lanes |
| Packaging — Phase 6 (OTD-compatible `.app` + bundled daemon, ad-hoc/`rcodesign` signing, tarball) | ▶️ **unblocked / planned** — OTD's shipping level, no Apple account needed |
| Packaging — Phase 7 (Developer-ID signing + notarization) | ⏭️ **deferred to V2** (paid Apple Developer membership) |

## Verdict

**Feasibility is no longer the question — it's proven.** The decision is *whether and when to productize*, and
the answer is gated by **packaging/Apple tooling** (an Apple Developer account for signing + notarization),
not by the app. The [implementation plan](macos/implementation-plan.md) lays out how to get `master` there
incrementally and safely.

## Related issues

- **#140** — Investigate a macOS version (this work).
- **#73** — Cross-platform verification / guard Windows-specific paths.
- **#148** — The original design review that scoped the portable core.
- **#296** — Daemon version display (extended to macOS here).
- **#167 / #320 / #89** — Per-app switching / global hotkeys (need macOS backends).
