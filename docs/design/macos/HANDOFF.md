# macOS port — handoff for the next agent

> **Read this first if you're resuming the macOS port.** Phase 0 (Windows-safe prep) is **done and
> merged to `master`** (PR #511). Your job is to resume at **Phase 1** and work down the phased plan on a
> **macOS machine**, verify-first, landing each phase as its own small, Windows-safe PR.

## Orient yourself — read in this order

1. [140-macos-feasibility.md](../140-macos-feasibility.md) — the hub + document map + status snapshot.
2. [macos/implementation-plan.md](implementation-plan.md) — **the plan you're executing** (Phases 1–6). Note the revision block folding in the [#510](https://github.com/TheSevenPens/OpenTabletArtist/issues/510) review.
3. [macos/dev-environment.md](dev-environment.md) — macOS SDK/runtime setup + the verification tooling.
4. [macos/windows-specific-surface.md](windows-specific-surface.md) — the technical catalog (seams, P/Invoke, the two real blockers).
5. [macos/reference-changes.md](reference-changes.md) — what the reference branch changed, file-by-file.
6. [macos/porting-journey.md](porting-journey.md) — how the port was de-risked; context for the plan's shape.

## What's already done (Phase 0, on `master`)

Merged in PR #511 — a strictly-better, port-ready Windows codebase, no behaviour change except the one
documented output-mode generalisation:

- **0.1** CA1416 enabled and **enforced as an error** (repo-root `Directory.Build.props`, kept out of the OTD submodule). Self-guarding wrappers deliberately left un-annotated.
- **0.2** `DisplayEnumerator` extracted behind an **`IDisplayEnumerator`** seam + static facade (Windows impl only — **Phase 1 adds the macOS impl**). 11 call sites unchanged.
- **0.3** Output-mode detection **generalised beyond Windows Ink** (native OTD modes recognised); the one intentional Windows change (native-absolute click no longer force-swaps to WinInk) is locked by Windows regression tests.
- **0.4** Platform-aware daemon exe name (`.exe` only on Windows).
- **0.5** OS-portable test paths + a **`windows-latest` + `ubuntu-latest` CI matrix** (both must stay green).
- **0.6** The early-reachable Win32 seams (`Win32ForegroundAppWatcher.Start`, `GlobalHotkeyService` ctor, calibration-overlay `SetCursor`/`Win32Properties`) guarded off-Windows.

Build with `dotnet build OpenTabletArtist.slnx`; test with `dotnet test`.

## Ground rules (non-negotiable — from the plan)

- **Runtime guards** (`OperatingSystem.IsWindows()` / `.IsMacOS()`), **never `#if`**. One binary that runs everywhere.
- **Keep Windows behaviour unchanged.** Every change is Windows-safe or the documented exception.
- **Verify each phase live on real hardware** per its "Verification"/"Exit" criteria before moving on. For Phase 3, the **calibration report is the coordinate-space oracle** (a systematic offset = the overlay isn't covering the full display).
- **Keep CA1416 green** (warning-as-error) and **both CI lanes green.**

## The reference branch

`origin/macos` is the **reference implementation / answer key — NOT a merge candidate.** Re-implement each
phase as a fresh, reviewed, Windows-safe PR; use the branch to see exactly how each capability was done.
`tools/DaemonProbe` (the headless daemon round-trip smoke test) lives on that branch, **not on `master`** and
not in the solution — pull it from `origin/macos` when Phase 1 needs it.

## Where to resume — Phase 1

1. Confirm the OTD submodule daemon (`net8`) **and** the OTA app (`net10`) build on macOS.
2. Add `AvaloniaScreensDisplayEnumerator` (the `IDisplayEnumerator` macOS impl from the Phase 0 seam).
3. Prove the daemon round-trip (`GetTablets` / `GetSettings`) with `tools/DaemonProbe`.
4. Get the GUI to boot and show the detected tablet with zero exceptions.
5. Extend CI to a macOS build lane.

Then proceed: **Phase 2** (feature-gating) → **3** (output + calibration) → **4** (daemon lifecycle) →
**5** (seam safety + optional backends) → **6** (packaging).

## Prerequisites the agent can't provide

- A **Mac** (ideally Apple Silicon) with the **.NET 8 + .NET 10 SDKs** (see `dev-environment.md`).
- A **real supported tablet** for the live verification in Phases 1 / 3 / 4.
- **Phase 6 only:** an **Apple Developer Program** membership + a CI secrets story (signing identity +
  notarization credentials). Don't start Phase 6 until 1–5 are landed.
