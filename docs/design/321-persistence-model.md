# 321 — Settings persistence model + surfacing "saved"

**Status:** Decided — building the save indicator + failure surfacing
**Issue:** #321

## The model today (from the audit)

OpenTabletArtist has **no "unsaved changes" concept**. Every settings edit is applied *and* persisted
immediately, as one atomic operation:

```
edit → mutate the live profile → AppSession.ApplyAndSaveSettingsAsync:
        ├─ SetSettings  (RPC → daemon applies the change live, in memory)
        ├─ TrySave      (writes settings.json to disk — best-effort, returns bool)
        └─ LoadData     (reload from daemon → UI re-syncs)
```

- Sliders / the pressure curve debounce their persist (~350–400ms) purely for drag smoothness; every
  other control applies on change.
- There is **no Save button, no dirty flag, no discard/revert** anywhere for daemon settings.
- Unlike OTD's own UX (which has a manual **Save**), in OTA "applied live" and "saved to disk" happen
  together on every change — the daemon's `SetSettings` only applies in memory; OTA is what persists it.
- **Snapshots/presets** are a *separate*, explicitly-saved concept (named backups of the whole settings
  file); loading one applies it via the same `ApplyAndSaveSettingsAsync`.
- **Local app preferences** (theme, card opacity, petals, suspend/backup state) live in a separate
  `AppSettings` key/value store that writes to disk immediately, independent of daemon settings.

This auto-save model is *good* — nothing is ever lost. The issue is that it's **invisible**, plus one
real reliability gap.

## Problems worth fixing

1. **Silent save failures (the real bug).** `ApplyAndSaveSettingsAsync` ignores `TrySave`'s bool result
   — there's a literal `TODO(#21): surface TrySave failures`. If the disk write fails, the change is
   **live but not persisted**, the user is never told, and it silently vanishes on the next daemon
   restart. This must be surfaced.
2. **No feedback that auto-save happened.** A user used to "edit → Save" has no signal their change
   stuck.

Out of scope (noted for later): OTD's own UX editing settings concurrently isn't reflected until the
30s poll / a manual Refresh — relevant to #320, not an "unsaved" concern.

## Decision

**Not** a Save/dirty/discard model — that would fight the (good) auto-save design. Instead: a **subtle,
transient save-status indicator** in the shell, driven by `ApplyAndSaveSettingsAsync`, that also covers
the failure case (fixing #1).

### Design

- `AppSession` gains a `SaveState { None, Saving, Saved, Failed }` observable, surfaced on
  `IConnectionState` (already the session-status interface exposed to the shell, `INotifyPropertyChanged`):
  - `bool ShowSaveStatus`, `bool SaveFailed`, `string SaveStatusText`.
- `ApplyAndSaveSettingsAsync`:
  - set `Saving` before the daemon write,
  - capture `TrySave`'s bool → set `Saved` (true) or `Failed` (false),
  - `Saved` auto-clears to `None` after ~2.5s (a one-shot dispatcher timer); `Failed` persists until the
    next save attempt.
- Shell indicator (MainWindow): a small, unobtrusive line —
  - *Saving…* (muted), *Saved* (success, transient), or
  - *Couldn't save — your change is live but won't survive a restart* (warning, sticky).
- Guarded for headless/no-dispatcher (tests) the same way the connect timer is.

Snapshots language cleanup (the third item from the discussion) is included: the Saved Settings page now
states up front that settings save automatically (no Save button) and that snapshots are *named backups*
you save now and restore later — so "Save Snapshot" doesn't read as "the Save button."

## Not changing

- The auto-save behavior itself, the debounce, the reload round-trip, or the snapshots feature.
- `AppSettings` (local prefs) — those already write immediately and rarely fail; the indicator is for
  daemon settings.
