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
snapshot. Per-app applies are invisible to the editor by design. While the feature is enabled we show
a non-blocking banner ("Per-app profiles are switching your settings automatically") so the user isn't
confused that the daemon state can differ from what the editor shows. Live pen streams (Diagnostics,
gauges) keep working because they read daemon reports, not `_settings`.

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
  on, hold it as `pending` until `Sample.IsDown` goes false. (`DaemonPenInputSource.Sample` already
  streams pen-down state.)
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

## Foreground watcher (only novel OS-integration piece)

`SetWinEventHook(EVENT_SYSTEM_FOREGROUND, …, WINEVENT_OUTOFCONTEXT)`, hook installed **on the UI
thread** (OUTOFCONTEXT callbacks fire on the installing thread, which must pump messages — Avalonia's
UI thread does). Callback → `GetForegroundWindow` → `GetWindowThreadProcessId` →
`QueryFullProcessImageName` for the exe path (fall back to `Process.GetProcessById(pid).ProcessName`
when access is denied for elevated processes). Marshal via `Dispatcher.UIThread.Post`, **pin the
delegate** against GC, unhook on dispose. References: PowerToys (FancyZones/Awake), pinvoke.net.

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
- **`PerAppProfileStore`** — JSON round-trip + `Resolve` precedence.
- **Manual** — the latency spike (gating), multi-monitor alt-tab storms, elevated-foreground-app
  (access-denied path), snapshot delete while mapped.

## Phasing

1. **Spike (gates all):** wire a throwaway `Win32ForegroundAppWatcher` → `ApplyLiveOnlyAsync` between
   two snapshots; time the switch and feel it mid-stroke against the feasibility success criteria
   (≲50 ms pen-up / ≲100 ms defer-until-pen-up). Go/no-go.
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
