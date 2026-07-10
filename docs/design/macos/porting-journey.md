# macOS porting journey — how we got to a compiling, working build

> Sibling of the [feasibility hub](../140-macos-feasibility.md). This is the **narrative + retrospective**:
> where we started, the method we used, the sequence that de-risked the port, what actually had to change,
> the environment gotchas, and the lessons. It explains the *why* behind the [phased plan](implementation-plan.md).

## Where we started

- A **Windows-only** Avalonia app (`OpenTabletArtist`, `net10`) driving the OpenTabletDriver daemon (pinned
  git submodule at **v0.6.7**, `net8`).
- A **stale** feasibility doc: it predated a wave of deeper Windows integration and understated the cost. The
  first move was to **re-baseline it against the current codebase** rather than trust it.
- A set of assumptions that turned out to be mostly *pessimistic*: "the icon font is a blocker" (already
  removed), "names may be blank on macOS" (they're not), "the Windows surface is huge" (it is, but almost all
  of it is already isolated behind service classes).

The single most important pre-existing property: **the app guards platform code with runtime
`OperatingSystem.IsWindows()` checks, not `#if WINDOWS`.** That is why it compiles and runs unmodified on
macOS — one binary, the JIT trims the guarded branches. Everything downstream depended on this.

## The method: verify-first, on real hardware

Every claim was **proven by running something on an actual Mac**, never by reasoning alone. The machine
happened to be an Apple-Silicon Mac (macOS 26 / Darwin 25.5) with **OpenTabletDriver.app installed and a real
Wacom Movink 13 (DTH-135) pen display attached** — i.e. a genuine test rig, plus a second display (ASUS
PA329CV). Each step produced a concrete artifact (a build result, an RPC round-trip, a screenshot, a
calibration report) before moving on.

## The de-risking sequence (what each step proved)

This order matters — it front-loads the biggest unknowns so that if something is fatal, you find out cheaply.

1. **Re-baseline the docs.** Measured the real P/Invoke footprint (**28 user32 · 10 kernel32 · 4 setupapi ·
   1 cfgmgr32**) and confirmed the earlier count was stale. Verified OTD v0.6.7 genuinely ships macOS targets
   (`OpenTabletDriver.MacOS.slnf`, `UX.MacOS`, `PermissionHelper`, a `DaemonWatchdog` macOS launch path).
2. **Bootstrap the toolchain.** No .NET SDK was on PATH. Installed the **.NET 10 SDK** (OTA is `net10`), later
   the **.NET 8 runtime** (the daemon is `net8`). See [dev-environment.md](dev-environment.md) for the exact
   steps and the gotchas that bit us here.
3. **Build the daemon** from the submodule (`net8`, arm64) → **0/0**. Proves the OTD graph is port-clean.
4. **Build the OTA app** (`net10`) → **0/0**. This was the pivotal result: **every Windows `DllImport`
   compiles** (P/Invoke binds lazily at runtime) and the `IsWindows()` guards handle the rest, so **there is
   no compile-time Windows blocker.** The whole macOS problem is *runtime* behaviour, not compilation.
5. **Prove the daemon round-trip.** A throwaway probe mirroring `DaemonClient`'s exact transport
   (`NamedPipeClientStream("OpenTabletDriver.Daemon")` → `JsonRpc` → `InvokeAsync`) connected over the .NET
   named-pipe → Unix-domain-socket emulation and round-tripped `GetTablets` (1 tablet) + `GetSettings`
   (2 profiles) — first against OTD.app's daemon, then against **our own submodule-built daemon**. This is the
   foundation the whole plan hinges on, and it needed **no changes** to `DaemonClient`.
6. **Boot the full GUI.** The real Avalonia app launched, ran with **zero exceptions**, and showed the
   detected tablet with correct specs. The Windows-only startup services (hotkeys, tray, foreground watcher,
   single-instance) **did not crash** — they were already guarded or degrade gracefully.

At that point feasibility was proven. Everything after was making it *correct and usable*.

## What actually had to change

Grouped by theme (see [reference-changes.md](reference-changes.md) for the file-by-file detail):

- **One platform seam extracted.** `DisplayEnumerator` (the largest functional P/Invoke gap — 8 GDI sites)
  was split into `IDisplayEnumerator` + `WindowsDisplayEnumerator` (existing code, moved verbatim) +
  `AvaloniaScreensDisplayEnumerator` (cross-platform via Avalonia `Screens`), behind a thin static facade
  that dispatches by OS. All 11 call sites were untouched.
- **Feature-gating.** The Windows-only surface was hidden off-Windows: the **health catalog** (via an
  `IsWindows` input flag that keeps the evaluator pure) stopped nagging about VMulti / Windows Ink /
  driver-conflicts, and the **ADVANCED rail** filtered out the Windows Ink / VMulti / Driver-cleanup / Startup
  subpages.
- **Domain de-Windows-ification.** The output-mode model equated "Absolute mode" with *the Windows Ink
  plugin*. Generalised it to recognise OTD's **native** Absolute/Relative modes (detect by the mode path
  carrying the word), which is what made the movement toggle read correctly and **calibration become
  available** on macOS.
- **Daemon lifecycle.** Fixed a hardcoded `OpenTabletDriver.Daemon.exe` (the apphost has no extension
  off-Windows) so **Restart** launches the bundled daemon; added a fallback so the **version + source** show
  on macOS (the Win32 pipe-PID lookup and executable version-stamp don't work there).
- **Seam runtime safety.** Guarded the last unguarded user32 calls (the calibration overlay's `SetCursor` and
  `Win32Properties` press-and-hold hook; the foreground watcher's `SetWinEventHook`) so nothing throws
  off-Windows.
- **Calibration overlay coverage.** The overlay used `WindowState.FullScreen`, which on macOS covers only the
  working area and lets AppKit push the window ~30 px *below the menu bar* — which threw off calibration.
  Fixed with targeted ObjC interop (raise the `NSWindow` above the menu-bar level + set its frame to the full
  `NSScreen` frame).
- **Test hygiene.** Four tests baked in `C:\…` path literals and failed off-Windows — not product bugs, just
  Windows-assuming tests. Rebuilt them to use OS-appropriate paths.

## Environment gotchas we hit (so the next person doesn't)

- **The `dotnet` muxer is fragile when mixing runtime installs.** Installing the net8 runtime over an
  existing net10 SDK left the muxer in a bad state on this macOS 26 host; restoring a clean host fixed it.
  Launching apps by their **native apphost** (with `DOTNET_ROOT` set) was the reliable path throughout.
- **The daemon is `net8`** — running it standalone needs the .NET 8 *runtime*. In a shipped release the daemon
  should be **self-contained** (bundle its runtime) so it launches regardless.
- **The daemon binds its pipe lazily** — the `CoreFxPipe_OpenTabletDriver.Daemon` socket appears only *after*
  startup/tablet-detection (several seconds), and .NET unlinks the socket path after bind. Poll by
  **connecting**, not by `test -S`. `DaemonClient`'s connect-loop already retries for this reason.
- **macOS AppKit constrains normal windows below the menu bar.** This is the root cause of the calibration
  offset — see the overlay fix above and in [reference-changes.md](reference-changes.md).

## Things we learned / were pleasantly surprised by

- **`DllImport` is not a compile-time obstacle.** P/Invoke declarations bind lazily; the app compiled on macOS
  with every user32/kernel32/setupapi/cfgmgr32 import present.
- **The OTD dependency graph is port-clean.** The four OTD projects OTA compiles are plain `net8` with no
  `-windows` TFM and no Windows-native package deps, so the build risk was confined to OTA's *own* code.
- **macOS gives us more, not less, than expected.** Avalonia `Screens` reports **friendly display names**
  (`ASUS PA329CV`, `Wacom DTH135`) on macOS — better than the doc's "names may be blank" caveat.
- **Coordinate spaces already agree.** OTA's Avalonia-derived geometry is in **logical points**, and OTD's
  macOS output uses the **same** CoreGraphics points space — the live daemon's stored display area matched our
  computed area exactly. No physical-vs-logical scaling mismatch.
- **The calibration bug was diagnosable from data, not guesswork.** The on-device calibration report
  (target/measured/delta per point) showed a systematic ~30 px vertical offset, worst at the top — the exact
  fingerprint of the menu-bar window-constraint. The fix followed directly from the numbers.
- **The codebase was already remarkably port-friendly.** Runtime `IsWindows()` guards, per-concern service
  classes, an existing `IForegroundAppWatcher` seam, an existing `IDaemonLifecycleService` interface — the
  architecture did most of the work.

## How we verified (and how to re-verify)

The tooling and exact checks live in [dev-environment.md](dev-environment.md): the **`tools/DaemonProbe`**
smoke test, a throwaway Avalonia **screens harness**, the **live GUI** boots + navigations, and the
**calibration report** as a coordinate-space oracle. Every capability in the status snapshot was confirmed
with one of these on the real Mac.
