# Per-application settings — implementation design (#167)

> Companion to the feasibility note [167-per-app-settings.md](167-per-app-settings.md). That doc
> establishes **why/what/risk**; this one specifies the concrete **how** — components, the switch
> policy, the live-apply path, the data model, UI, and tests.
>
> **Gate:** still contingent on the latency / mid-stroke spike (Risk 1 in the feasibility note). This
> design is written so the spike is literally *phase 2 of the switcher* (a throwaway wiring of the same
> `IForegroundAppWatcher` + apply path), not separate throwaway code.

## Goal

While OpenTabletArtist is running, automatically apply a per-app **profile** when the foreground
application changes, falling back to a default for unmapped apps — the way Wacom/XP-Pen/Huion drivers
do. A "profile" is an existing **Saved Settings snapshot** (whole-`Settings` JSON). Switches are
**live-apply only** (never written to disk) and are **ephemeral** — the user's on-disk default is
never overwritten and is restored when the feature is disabled or the app exits.

Non-goals (v1): per-tablet (rather than whole-`Settings`) profiles; arbitrary window-title matching;
editing a per-app profile in place (you edit the underlying Saved Settings snapshot).

## Components

```
 ┌─────────────────────────┐   ForegroundAppChanged(AppIdentity)
 │ IForegroundAppWatcher    │ ───────────────┐
 │  Win32ForegroundAppWatcher│               ▼
 └─────────────────────────┘        ┌──────────────────┐   ApplyLiveOnlyAsync(Settings)
 ┌─────────────────────────┐  pen   │  PerAppSwitcher   │ ──────────────────────────────► AppSession ─► daemon.SetSettings
 │ DaemonPenInputSource     │ up/dn  │  (debounce +      │        (no TrySave, no reload)
 │  .Sample (IsDown)        │ ─────► │   defer + policy) │
 └─────────────────────────┘        └──────────────────┘
                                        ▲          │ resolve name
                        Mappings/Default │          ▼
                                 ┌──────────────┐  load Settings by name
                                 │ PerAppProfile │ ◄── SavedSettings snapshots (PresetsViewModel/store)
                                 │ Store (JSON in│
                                 │  AppSettings) │
                                 └──────────────┘
```

| Component | New? | Responsibility |
|---|---|---|
| `IForegroundAppWatcher` + `Win32ForegroundAppWatcher` | **new** | Raise `Changed(AppIdentity)` on the UI thread when the foreground window's process changes. Win32 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`. Interface for testability + the macOS seam (#140). |
| `AppIdentity` | **new** | `record(string ExePath, string ExeName)`. Identity for matching. |
| `PerAppProfileStore` | **new** | Load/save the `{Enabled, DefaultSnapshot, Mappings[]}` config as JSON in `AppSettings` under one key. CRUD + `Resolve(AppIdentity) → snapshotName?`. |
| `PerAppSwitcher` | **new** | The brain: subscribe to the watcher + pen stream, apply the switch **policy**, call the live-apply path. Headless and fully unit-testable. |
| `AppSession.ApplyLiveOnlyAsync` / `RestoreDefaultAsync` | **new** | Live-apply a snapshot to the daemon **without** `TrySave` and **without** mutating `CurrentSettings`; restore the cached default on disable/exit. |
| `IPenStateProvider` (+ `DaemonPenInputSource`) | **new** | A **feature-scoped** pen-down stream for defer-until-pen-up. `DaemonPenInputSource.Sample` is *page-scoped* today (only runs while Test/Diagnostics/Calibration/dynamics pages hold it), so in tray/background mode — the primary use case — there is no pen stream. The switcher owns its own `DaemonPenInputSource`, started/stopped with the feature's enable toggle and **refcounted through `DaemonClient._debugRefCount`** so it coexists with Diagnostics/Test. |
| Snapshot loading | reuse | Read a snapshot's `Settings` by `Name` via the existing Saved Settings store (`PresetsViewModel`/`ISettingsFileStore`). |
| `PerAppProfilesView` / VM | **new** | The UI: enable toggle, default picker, mapping list, add-via-process-picker/browse. |

## Live-apply-only path (resolves feasibility Risk 3)

Today [`AppSession.ApplyAndSaveSettingsAsync`](../../OpenTabletArtist/Services/AppSession.cs) does
`SetSettingsAsync` **+** `TrySave` **+** (during load) reload — so reusing it per focus change would
overwrite the user's on-disk default. The switcher needs a path that touches **only** the daemon:

```csharp
// AppSession — illustrative
public async Task ApplyLiveOnlyAsync(Settings snapshot)   // per-app switch: ephemeral
{
    Dispatcher.UIThread.VerifyAccess();
    await _daemon.SetSettingsAsync(snapshot);
    // NOTE: deliberately does NOT set _settings, TrySave, or LoadDataAsync.
}

public Task RestoreDefaultAsync()                          // on disable / app exit
    => _settings is { } s ? _daemon.SetSettingsAsync(s) : Task.CompletedTask;
```

**Editor coherence (key decision):** the switcher never mutates `CurrentSettings`/`_settings`, so the
tablet-settings editor always reflects and persists the user's **default**, not the transient per-app
snapshot. Per-app applies are invisible to the editor by design. Live pen streams (Diagnostics,
gauges) keep working because they read daemon reports, not `_settings`.

**Stale-UI surfaces (must be handled, not just the editor).** Several places read
`CurrentSettings`/`_settings` rather than live daemon state and will show the *default* while a per-app
snapshot is applied. Enumerated and classified:

| Surface | While per-app active, show… |
|---|---|
| Tablet-settings editor (`MainViewModel`/dialog profile) | Default (editing target) — non-blocking banner explains the daemon may differ. |
| `PresetsViewModel.UpdatePreset` ("save current config to snapshot") | Default — **must not** capture a transient per-app snapshot; disable or warn while active. |
| Dashboard output-mode / Windows-Ink indicators | Default is misleading; mirror a one-line "Per-app profile **X** applied" readout. |
| Tray dynamics reveal line (`AppTray.cs`) | Default; same one-liner. |

So: keep `_settings` on the default everywhere, add a **non-blocking banner** on the editor, a
**live status one-liner** on the Per-App page (and mirrored on the Dashboard/tray while active), and
**guard `UpdatePreset`** so "save current" can't snapshot a transient per-app config.

### Lifecycle, restore & guards

- **Restore-on-exit must run on every shutdown path.** `RestoreDefaultAsync` has to fire before the
  daemon is disconnected/stopped on **all** of: the disable toggle, tray **Quit**, and
  `MainWindow.AllowCloseForQuit`. Wire it at the composition root (next to `AppTray` / the existing
  tray lifecycle), **not** only in the page VM — otherwise the last per-app snapshot stays applied to
  the daemon after we exit. (Feasibility Risk 2 says a lingering snapshot is *acceptable*, but if we
  advertise "ephemeral" switching, users will expect restore-on-exit; make it the default.)
- **Foreign-daemon guard.** Per-app switching calls `SetSettings` on whatever daemon is connected, but
  snapshot paths / filter stores won't match a foreign daemon. **Disable the feature (with an inline
  explanation) when `AppSession.IsForeignDaemon`** — same app-owned-only pattern as plugin install and
  the pressure-plugin copy.
- **Enable = start the pen provider; disable = stop it + restore default.** The feature-scoped
  `DaemonPenInputSource` lifetime is tied to the enable toggle (refcounted, so it composes with
  Diagnostics/Test rather than fighting them).

## Switch policy (the core, and the bulk of the tests)

State: `currentSnapshot` (what's applied to the daemon), `pending` (snapshot waiting on pen-up),
`penDown`, and a debounce token.

```
on ForegroundAppChanged(app):
    if app.ExeName == our own exe:  return           # editing our window must not thrash
    target = store.Resolve(app) ?? store.DefaultSnapshot ?? <user default>
    if target == currentSnapshot:   return           # dedupe by SNAPSHOT, not by app
    debounce(200ms):                                  # coalesce alt-tab storms
        if penDown and DeferUntilPenUp:  pending = target; return
        applySwitch(target)

on PenUp:
    if pending is set:  applySwitch(pending); pending = null

applySwitch(target):
    settings = (target is <user default>) ? RestoreDefaultAsync()
                                          : ApplyLiveOnlyAsync(load(target))
    currentSnapshot = target
```

Policy details:
- **Dedupe by snapshot, not app** — three apps mapped to the same snapshot cause no switching between
  them (avoids needless `SetSettings` churn).
- **Debounce** (~200 ms, tune from the spike) collapses rapid focus changes.
- **Defer-until-pen-up** — gated on the spike result; when a switch lands mid-stroke and deferral is
  on, hold it as `pending` until `IsDown` goes false, read from the switcher's **feature-scoped**
  `IPenStateProvider` (see Components — the page-scoped stream is off in tray/background mode, so the
  switcher must own its own).
- **Default fallback** — unmapped apps resolve to the configured default snapshot, or, if none, the
  user's on-disk default (via `RestoreDefaultAsync`).
- **Ignore our own window** — focusing OpenTabletArtist keeps the current profile so editing/testing
  isn't disrupted.
- **Dangling snapshot** — if a mapping references a deleted/renamed snapshot, resolve to default and
  surface a warning badge on that row (don't fail the switch).

## Data model + persistence

```csharp
record PerAppMapping(string ExePath, string ExeName, string SnapshotName, bool Enabled = true);
record PerAppConfig(bool Enabled, string? DefaultSnapshot, IReadOnlyList<PerAppMapping> Mappings);
```

Persisted as JSON under a single `AppSettings` key (e.g. `"PerAppProfiles"`) — **our** store, never
OTD's settings file. Match precedence in `Resolve`: exact `ExePath` → `ExeName` (portability across
install paths) → `DefaultSnapshot` → user default.

**Snapshot rename propagation.** `PresetsViewModel.RenamePreset` moves the file and knows nothing about
mappings. The dangling-snapshot → default fallback keeps this *safe*, but the intended UX is to
**update matching `PerAppProfileStore` mappings in place on rename** (and surface the warning badge only
for genuinely missing snapshots). Hook the store into the rename path rather than relying on the
fallback.

## Foreground watcher (only novel OS-integration piece)

`SetWinEventHook(EVENT_SYSTEM_FOREGROUND, …, WINEVENT_OUTOFCONTEXT)`, hook installed **on the UI
thread** (OUTOFCONTEXT callbacks fire on the installing thread, which must pump messages — Avalonia's
UI thread does). Callback → `GetForegroundWindow` → `GetWindowThreadProcessId` →
`QueryFullProcessImageName` for the exe path (fall back to `Process.GetProcessById(pid).ProcessName`
when access is denied). Marshal via `Dispatcher.UIThread.Post`, **pin the delegate** against GC,
unhook on dispose. References: PowerToys (FancyZones/Awake), pinvoke.net.

**Documented identity limitations:** elevated / admin foreground apps may deny the exe-path query (name
fallback still works), and **packaged / UWP (Store) apps** often report odd `ApplicationFrameHost`-style
paths from `QueryFullProcessImageName` — the `ExeName` match mitigates but doesn't fully solve it. List
both in the USERMANUAL caveats.

macOS seam (#140): a `MacForegroundAppWatcher` behind the same interface later; everything above the
interface is portable.

## UI

New **Advanced** page "Per-App Profiles":
- **Enable** master toggle.
- **Default profile** dropdown (unmapped apps) — options are Saved Settings snapshots + "Use my
  default settings".
- **Defer switching until the pen is lifted** checkbox (surfaced per the spike outcome).
- **Mappings** list — each row: app name + exe path, a snapshot dropdown, remove; a warning badge if
  the snapshot is missing.
- **Add mapping** → dialog: running-process picker (with icons) *or* browse-to-exe.
- **Live status line** — "Foreground: `krita.exe` → profile **Painting**" (confidence readout, akin to
  the wheel gauge).
- **Empty state** links to Saved Settings to create snapshots first (they're the prerequisite).

## Testing

- **`PerAppSwitcher` policy** (the bulk): fake `IForegroundAppWatcher`, fake pen stream, fake apply —
  assert debounce coalescing, dedupe-by-snapshot, defer-until-pen-up, default fallback, ignore-own-
  window, dangling-snapshot → default. Fully headless.
- **`PerAppProfileStore`** — JSON round-trip + `Resolve` precedence + rename-in-place propagation.
- **Pen-provider refcount** — enabling the feature while Diagnostics/Test is open, then closing one,
  must not tear down the other's stream (`DaemonClient._debugRefCount` stays ≥ 1). This is the #121
  ref-counting invariant applied to the new consumer.
- **Manual** — the latency spike (gating), multi-monitor alt-tab storms, elevated- and UWP-foreground
  apps (access-denied / odd-path), snapshot delete or rename while mapped, restore-on-exit across tray
  Quit and window close.

## Phasing

1. **Spike (gates all):** wire a `Win32ForegroundAppWatcher` → `ApplyLiveOnlyAsync` between two
   snapshots; time the switch and feel it mid-stroke against the feasibility success criteria
   (≲50 ms pen-up / ≲100 ms defer-until-pen-up). Run it in **both** configurations the shipped feature
   will see, and across the scenarios below:

   | Config | Why |
   |---|---|
   | Debug stream **off** (normal tray use) | Bare `SetSettings` latency — the number that matters when defer-until-pen-up is off or pen state is unavailable. |
   | Debug stream **on** (defer-until-pen-up wired through the feature-scoped pen provider) | Realistic overhead with pen tracking active; also confirms holding the debug stream on for the switcher doesn't itself cause mid-stroke glitches. |

   Scenarios: alt-tab storm (10+ switches in 2 s), a switch **during** an active stroke with defer
   on/off, and a snapshot that differs only in **pen dynamics** vs. one that changes **output mode /
   display mapping** (mapping changes likely cost more than a curve tweak). Go/no-go.
2. `ApplyLiveOnlyAsync` + `RestoreDefaultAsync` in `AppSession` (+ tests).
3. `IForegroundAppWatcher` + Win32 impl (promoted from the spike).
4. `PerAppSwitcher` + policy, headless, validated by logs and unit tests.
5. `PerAppProfileStore` + CRUD.
6. `PerAppProfilesView`/VM.
7. Docs (USERMANUAL) + the "app must be running" and "whole-Settings/all-tablets" caveats.

## Open questions

- **Default identity** — is "default" the user's on-disk `Settings` (restore path) or a chosen
  snapshot? Proposed: on-disk default, with an optional override snapshot in the config.
- **Debounce/defer constants** — set from the spike, not guessed.
- **Elevated foreground apps** — the hook may not resolve the exe; document the limitation and match by
  process name where possible.
- **Coexistence with OTD's own UI** — cosmetic only (a concurrent OTD UI would show the flips);
  documented, not solved.

## Review — 2026-06-30 (Cursor agent, verified & incorporated)

Reviewed against the pinned submodule (`external/OpenTabletDriver` @ v0.6.7), `AppSession`,
`PresetsViewModel`, `DaemonPenInputSource`, and `DaemonClient`. Verdict: **design sound and
well-scoped**; six gaps were raised, and **all were verified against the code and folded into the
sections above** —

1. Feature-scoped pen provider → *Components* + *Switch policy* (the page-scoped stream is off in tray mode).
2. Stale-UI surfaces + `UpdatePreset` guard → *Live-apply / Stale-UI surfaces* table.
3. Restore-on-exit on every shutdown path → *Lifecycle, restore & guards*.
4. Snapshot-rename propagation → *Data model*.
5. Foreign-daemon guard → *Lifecycle, restore & guards*.
6. UWP/elevated identity limits → *Foreground watcher*; two-config spike matrix → *Phasing*.

The original review notes are retained below for provenance.

### Affirmations

- **No OTD preset RPCs.** `IDriverDaemon` exposes only `GetSettings` / `SetSettings` (verified in
  `OpenTabletDriver.Desktop/Contracts/IDriverDaemon.cs`). The feasibility note's correction of the
  earlier issue comment (which mentioned `GetPresets` / `ApplyPreset`) is accurate — snapshots are
  file-based JSON in `PresetDirectory`, exactly as `PresetsViewModel` already handles.
- **Risk 3 is real and well-specified.** `ApplyAndSaveSettingsAsync` does `SetSettings` + `TrySave` +
  `LoadDataAsync` and mutates `_settings` — reusing it per focus change would corrupt the on-disk
  default. The proposed `ApplyLiveOnlyAsync` / `RestoreDefaultAsync` split is the right fix.
- **Editor coherence decision is correct.** Keeping `_settings` on the user's default while the daemon
  runs a transient snapshot avoids the worst failure mode (editing Krita's profile and accidentally
  persisting it as the global default). The non-blocking banner is necessary, not optional.
- **Dedupe-by-snapshot** is a smart optimization — three apps sharing one snapshot should not churn
  `SetSettings`.
- **Spike-as-phase-2** (promote the same watcher + apply path rather than throwaway code) will give
  honest numbers and avoids rework.

### Gaps to address in the design

1. **Pen-down source is not session-scoped today.** The switch policy depends on
   `DaemonPenInputSource.Sample` / `IsDown`, but that type is **page-scoped** — it only calls
   `SetTabletDebugAsync(true)` while Test, Diagnostics, Calibration, or the dynamics live-pressure
   dot are active. In tray/background mode (the primary use case for per-app switching) there is no
   pen stream unless the user happens to have one of those pages open. **Add an `IPenStateProvider`
   (or have `PerAppSwitcher` own a dedicated `DaemonPenInputSource` instance) whose lifetime is tied
   to the feature's enable toggle**, refcounted through `DaemonClient`'s existing debug ref-count so
   it coexists with Diagnostics/Test. Without this, defer-until-pen-up cannot work in production.

2. **Stale UI beyond the banner.** Several surfaces read `ISettingsCoordinator.CurrentSettings`
   (`_settings`), not live daemon state: Dashboard output-mode / WinInk indicators, tray dynamics
   reveal line, `PresetsViewModel.UpdatePreset` ("save current config to snapshot"), and the tablet
   dialog's loaded profile. The banner covers the editor mismatch, but **enumerate which of these
   should show default vs. live** and whether any need a small "daemon-applied profile" readout
   (the proposed live status line on the Per-App page is a good start — consider mirroring a one-liner
   on the Dashboard or tray while the feature is active).

3. **Lifecycle: restore on exit is easy to miss.** `RestoreDefaultAsync` must run on **all** shutdown
   paths: tray Quit, `MainWindow.AllowCloseForQuit`, and disable-toggle — before disconnect/stop.
   Wire this at the composition root (`AppSession` or `Program`) alongside the existing tray
   lifecycle, not only in the page VM. Otherwise the last per-app snapshot stays applied in the
   daemon after the app closes (Risk 2 in the feasibility note says this is acceptable, but users
   will expect restore-on-exit if we advertise ephemeral switching).

4. **Snapshot rename propagation.** `PresetsViewModel.RenamePreset` moves the file but does not know
   about per-app mappings. The dangling-snapshot → default fallback is safe, but **specify whether
   rename should update `PerAppProfileStore` mappings in place** (preferred UX) or only show the
   warning badge and require manual re-assign.

5. **Foreign-daemon guard.** Plugin install and pressure-plugin copy are gated on app-owned daemon.
   Per-app switching calls `SetSettings` on whatever is connected — it will work on a foreign daemon,
   but snapshot paths / filter stores may not match. **Recommend disabling the feature (with an
   explanation) when `IsForeignDaemon`**, same pattern as other app-owned-only features.

6. **Packaged / UWP foreground apps.** `QueryFullProcessImageName` often returns odd paths for Store
   apps and some elevated processes. The `ExeName` fallback helps; add to documented limitations
   alongside elevated apps.

### Spike additions

Run the spike in **both** configurations the production feature will see:

| Config | Why |
|---|---|
| Debug stream **off** (normal tray use) | Measures bare `SetSettings` latency — the number that matters if defer-until-pen-up is off or pen state is unavailable. |
| Debug stream **on** (defer-until-pen-up enabled) | Measures the realistic overhead when pen tracking is active; validates that keeping debug on for the switcher doesn't itself cause mid-stroke glitches. |

Also test: alt-tab storm (10+ switches in 2 s), switch **during** an active stroke with defer on/off,
and a snapshot that differs only in pen dynamics vs. one that changes output mode / display mapping
(mapping changes may be more expensive than curve tweaks).

### Verdict

**Proceed with the spike.** The architecture fits the existing `AppSession` role-interface shape
(`PerAppSwitcher` as a headless service composed next to `AppTray`), reuses proven Saved Settings
infrastructure, and keeps the blast radius contained. If the spike hits the ≲50 ms / no-dropped-report
bar with defer-until-pen-up wired through a session-scoped pen provider, the phased plan below is
ready to execute as written. If not, the finding belongs in the feasibility doc as a recorded no-go —
no further UI work.
