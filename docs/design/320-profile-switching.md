# 320 — Switching profiles (hotkeys + per-app auto-switch)

**Status:** Reviewed & approved (Cursor) — ready to implement Part 1
**Issue:** #320
**Related:** #167 (per-app settings — Part 2 *is* this), #321 (persistence model), #72 (tray/background mode, shipped)

## Goal

From #320, two features that ship independently:

- **Part 1 — Hotkeys + toast.** Global keyboard shortcuts to switch between saved profiles quickly,
  with an on-screen toast confirming the switch.
- **Part 2 — Per-application auto-switch.** Assign a profile to an application (pick a running process
  or browse to an exe path); when that app comes to the foreground, its profile is applied
  automatically. A profile maps to *all apps* (default) or a *specific app*.

Plus the open user-model question from the issue: while you're editing in the UI, *which* profile are
you in?

## Terminology

In #320, "profiles" = our **Saved Settings snapshots** (whole-`Settings` JSON, managed by
`PresetsViewModel` / the Saved Settings page) — **not** OTD's per-tablet `Profile` objects. A snapshot
is a complete, self-contained config (mapping, dynamics, pen switches, tablet buttons, output mode), so
"switch profile" = apply a snapshot.

## Prior art (what's already been discussed)

**OpenTabletDriver will not build native per-app switching soon.**
- [OTD #2233 "[Feature Suggestion] App specific Presets"](https://github.com/OpenTabletDriver/OpenTabletDriver/issues/2233) (open) — maintainer **X9VoiD**: *"If somebody knows how to retrieve the current focused window on Linux and macOS then we'll gladly add it."* The blocker is **cross-platform focus detection**. **vedxyz**: earliest possible is 0.7 (a rewrite), and *"unlikely to be implemented … any time soon."* Also: loading a preset **doesn't overwrite** `settings.json`, and you can already switch presets from the **tray right-click menu** (and, per the wiki/web, via **hotkey and tablet-button bindings**).
- [OTD #4119 "Layered Settings"](https://github.com/OpenTabletDriver/OpenTabletDriver/issues/4119) (open) — multi-tablet global/override profiles; related mental model, not per-app.
- Web / [DeepWiki: Settings and Profiles](https://deepwiki.com/OpenTabletDriver/OpenTabletDriver/2.3-settings-and-profiles) — confirms manual preset switching (UI/hotkey/tablet-button); **no** automatic per-app switching.

**We already did the design homework for the auto-switch half:**
- [OTA #167 "Support per-app settings"](https://github.com/TheSevenPens/OpenTabletArtist/issues/167) with
  [167-per-app-settings.md](167-per-app-settings.md) (feasibility) and
  [167-per-app-settings-design.md](167-per-app-settings-design.md) (concrete design: `IForegroundAppWatcher`,
  `PerAppSwitcher`, `PerAppProfileStore`, switch-policy state machine, live-apply-only path, data model, UI, tests).

**Key insight:** the thing blocking OTD (cross-platform focus detection) **doesn't block us** —
OpenTabletArtist is **Windows-only**, so a `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` foreground watcher
is trivial. OTA is the right layer to add this on top of OTD.

## Persistence angle (from #321)

#321 established that OTA **auto-saves every settings change to disk** (`ApplyAndSaveSettingsAsync` =
apply to daemon **+ persist to `settings.json`** + reload). For profile switching this matters:

- An **auto-switch** (Part 2) must **not** persist — otherwise every app change would overwrite
  `settings.json` with that app's config, and your "real" settings would be lost. It needs a
  **live-apply-only** path (apply to the daemon, do **not** write `settings.json`). We don't have one yet.
- A **hotkey switch** (Part 1) is also **live-apply-only** (decided in review) — a temporary override
  that reverts on restart, with a distinct "Profile override" cue.

This also answers the issue's "which profile am I in while editing" question: **you always edit the
live/active settings.** Under live-apply-only switching, edits made while an app-profile is active are
temporary unless you explicitly Save (a snapshot) — which we should make legible in the UI.

## Part 1 — Hotkeys + toast (build first)

### Behavior
- **Bind a global hotkey to a specific snapshot** (e.g. `Ctrl+Alt+1` → "osu!", `Ctrl+Alt+2` → "Draw").
  Optionally also a **cycle** hotkey (next/previous snapshot). Start with per-snapshot bindings; cycle
  is a cheap add.
- On activation: apply that snapshot and show a **toast** ("Switched to 'Draw'").

### Components
- **`GlobalHotkeyService`** (Win32). Preferred mechanism: a **message-only window** owning a dedicated
  wndproc, with `RegisterHotKey`/`UnregisterHotKey` and `WM_HOTKEY` dispatch. (Alternative:
  `SetWindowsHookEx(WH_KEYBOARD_LL)`.) Must work while the app is minimized to the tray (#72). The exact
  mechanism is a small spike/reviewer question — see Risks.
- **Hotkey → snapshot mapping**: small JSON in `AppSettings` (`Hotkey:<id>` → snapshot name + chord).
- **Apply path**: reuse the snapshot apply. Persist-vs-live decision below.
- **Toast**: in-app transient overlay when the window is visible; a **tray balloon** (we already own a
  tray icon, #72) when backgrounded, so the confirmation is visible either way. Reuse the save-chip
  styling for the in-app case.

### UI
- On the **Saved Settings** page, per snapshot: an "Assign hotkey…" affordance that captures a chord and
  stores the mapping; show the assigned chord on the snapshot card.

### Why first
Lower risk than auto-switch: switches are **user-initiated** (no per-focus-change churn, far less
mid-stroke exposure), and it delivers standalone value. It also exercises the apply path + toast that
Part 2 reuses.

## Part 2 — Per-app auto-switch (later; gated on a spike)

This *is* #167 — build to [167-per-app-settings-design.md](167-per-app-settings-design.md):
`IForegroundAppWatcher` (`SetWinEventHook`) → `PerAppSwitcher` (debounce, dedupe-by-snapshot,
**defer-until-pen-up**, default fallback) → **live-apply-only** apply. Mapping: exe/process → snapshot,
plus a Global/Default. UI to assign an app (running-process picker or browse-to-exe).

**Make-or-break risk (unchanged from #167):** `SetSettings` rebuilds the daemon pipeline on every
switch — rapid alt-tabbing or a switch *mid-stroke* could glitch. **Do a latency/mid-stroke spike
before building the user-facing feature** (throwaway watcher → apply between two snapshots; measure;
feel it mid-stroke). Mitigations: debounce + defer-until-pen-up.

## Risks / unknowns

1. **Global hotkey mechanism** — `RegisterHotKey` (message-only window) vs `WH_KEYBOARD_LL`. RegisterHotKey
   is cleaner and less antivirus-alarming; confirm it fires while minimized to tray.
2. **Switch latency / mid-stroke glitch** (Part 2) — the gating spike above.
3. **Live-apply-only path** — new variant of `ApplyAndSaveSettingsAsync` that applies without persisting;
   both parts likely want it. Needs care so the reload round-trip + save-indicator (#321) behave.

## Recommended sequencing

1. This doc → Cursor review.
2. **Part 1** (hotkeys + toast) — including the live-apply-only apply path if we choose live-only.
3. **Latency/mid-stroke spike** for Part 2.
4. **Part 2** (per-app auto-switch, per #167) if the spike is green.

## Resolved by design review (Cursor, approved)

| Question | Decision |
|---|---|
| Persist or live-apply on a hotkey switch? | **Live-apply-only** (same as Part 2). A hotkey switch is a temporary "I'm drawing now" override, not a new default. Persisting would fight #321's auto-save (edits would land in the wrong on-disk file) and surprise users on restart. |
| Per-snapshot vs cycle hotkeys? | **Per-snapshot bindings first** (map to Saved Settings cards). Cycle is a cheap later add once the switch service exists. |
| Global hotkey mechanism? | **Message-only HWND + `RegisterHotKey`.** Standard, low-privilege, fires while minimized to the tray. Avoid `WH_KEYBOARD_LL` unless forced. |
| Toast surface? | **In-app transient chip + tray balloon** for v1. Reuse the save-chip styling but with distinct copy/icon so it isn't confused with #321 "Saved". Skip native Windows toasts unless asked. |

### The "profile override" cue
While a live-only override is active (daemon ≠ what's on disk), show a **distinct, persistent
"Profile override: <name>"** cue — separate from the #321 save chip — so it's legible that the active
config isn't the saved default. Clears on RestoreDefault / explicit save.

## Part 1 implementation requirements (from review)

1. **Shared `ProfileSwitchService`** (name TBD) that both hotkeys and (later) `PerAppSwitcher` use:
   load snapshot by name → `ApplyLiveOnlyAsync` / `RestoreDefaultAsync` → raise `ActiveSnapshotChanged`
   for the toast + override cue. Hotkeys must **not** wire directly to `AppSession` from the Saved
   Settings page.
2. **Build `ApplyLiveOnlyAsync` + `RestoreDefaultAsync` now, with tests** — a variant of
   `ApplyAndSaveSettingsAsync` that applies to the daemon + reloads but does **not** write
   `settings.json`. Part 2's spike reuses this (not a throwaway). Include **restore-on-exit** on tray
   Quit / window close.
3. **Guard `PresetsViewModel.LoadPreset` / `UpdatePreset` while an override is active** — loading today
   goes through the persist+reload path, and "Update snapshot" could capture the wrong (overridden)
   config. Disable or warn, mirroring #167's stale-UI handling.
4. **Hotkey mapping lifecycle** — snapshot rename/delete must update/invalidate `Hotkey:*` mappings
   (mirror the dangling-snapshot handling in #167's `PerAppProfileStore`).
5. **Chord conflicts** — `RegisterHotKey` fails if another app owns the chord; surface a clear error in
   the capture UI, never fail silently.

Small spike within Part 1: confirm hotkeys fire with **only the tray icon** visible (main window hidden).

## Part 2 note (from review)
No doc changes; keep the #167 pointer + the latency/mid-stroke spike as the gate. Run the spike with the
debug stream **on and off** (the pen-defer path), per the #167 review.
