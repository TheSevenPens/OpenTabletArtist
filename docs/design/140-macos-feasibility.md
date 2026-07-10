# macOS support for OpenTabletArtist (#140)

> **Status (2026-07-10): feasibility PROVEN, end-to-end, on real hardware.**
> The `macos` exploration branch takes OTA from "Windows-only" to a **running, connected, usable** macOS
> build: it compiles, connects to the OpenTabletDriver daemon, detects the real tablet, maps to the correct
> display, switches output mode, and **calibrates correctly** — with the Windows-only surface hidden and the
> full test suite green. What remains is **packaging** (`.app` + signing/notarization), not foundational risk.

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
| Calibration runs and **aligns correctly** (overlay covers the full display) | ✅ live-verified with the pen |
| Daemon lifecycle: correct exe name, Restart launches the bundled daemon, version + source shown | ✅ |
| OS-integration seams (hotkeys, tray, watcher, overlay) safe off-Windows | ✅ guarded; tray works as a menu-bar item |
| Test suite | ✅ **564 passing, 0 failing** on macOS |
| Packaging (`.app` bundle, code-signing, notarization, macOS CI lane) | ⛔ not started — the remaining work |

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
