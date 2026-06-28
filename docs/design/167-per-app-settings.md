# Feasibility — per-application settings (#167)

> Status: **investigation only.** No commitment. The ask: typical manufacturer drivers (Wacom,
> XP-Pen, Huion) let you define a *global* config plus *per-application* overrides for mapping,
> dynamics, pen switches, and tablet buttons, and switch automatically as the foreground app
> changes. This doc assesses whether we can offer the same on top of OpenTabletDriver.

## TL;DR

**Feasible, and smaller than first expected — but it is a feature we own end-to-end, gated by one
unknown (switch latency / mid-stroke safety).** OTD has *no* per-application concept of its own, so
the auto-switching layer is entirely ours. The good news: a "config" is just a saved `Settings`
snapshot applied via the daemon's `SetSettings`, and **we already have that UX** (the Saved Settings
page). So our job shrinks to **(1) a foreground-window watcher**, **(2) an app→snapshot mapping + UI**,
and **(3) a live-apply-only switch path** (apply without persisting to disk). The make-or-break risk
is that `SetSettings` rebuilds the daemon pipeline on every focus change; a timed spike must confirm
switching is fast and glitch-free (especially mid-stroke) before we build the rest. The
tray/background mode (#72) is a hard prerequisite and is already shipped.

## How manufacturer drivers do this (and why there's no code to read)

Wacom / XP-Pen / Huion are all **closed-source** — there is no reference implementation to copy. But
the architecture is well understood and universal across them:

1. A **user-mode background service** runs continuously (e.g. Wacom's `WTabletServicePro`).
2. It **hooks the foreground window** (Win32) and maps the active process to a stored config.
3. On a switch it **reconfigures the active mapping/bindings inside its own driver** — which is why
   theirs feels instant and seamless.

The "secret sauce" (steps 1–2) is mundane. The part that's genuinely hard (step 3, a cheap in-driver
reconfigure) is the part we *cannot* copy, because for us the reconfigure goes through the OTD daemon
API. That gap is the entire risk of this feature (see Risks).

## What OTD does and does not provide

> **API note (verified against our *pinned submodule*, `external/OpenTabletDriver`).** The daemon
> interface is whole-settings only — `Task<Settings> GetSettings()` / `Task SetSettings(Settings)`
> (`OpenTabletDriver.Desktop/Contracts/IDriverDaemon.cs`). There are **no preset RPCs** and no
> per-tablet apply RPC in the version we ship against. (Newer OTD branches expose a different
> `Profiles[]` + preset RPC surface — do **not** design against those; we build on what's pinned.)

- **No application concept.** `Settings` is one global object: a per-tablet `ProfileCollection Profiles`
  (each `Profile` = `OutputMode` + `Filters` + `Bindings`), plus `Tools` and the lock flags
  (`OpenTabletDriver.Desktop/Settings.cs`). Nothing keys on a process or foreground window.
- **"Presets" are a client-side convention, not a daemon feature.** A preset is just a saved `Settings`
  JSON snapshot; "applying" one means `SetSettings(thatSnapshot)`. Everything the issue wants
  (mapping, dynamics, pen switches, tablet buttons) is inside `Settings`, so a snapshot is a complete,
  self-contained "this app's config."
- **We already implement the snapshot UX.** Our **Saved Settings** page (`PresetsViewModel`) saves,
  loads, renames, and deletes snapshot JSON and applies via
  `AppSession.ApplyAndSaveSettingsAsync(Settings)` ([AppSession.cs:427](../../OTDWindowsHelper/Services/AppSession.cs))
  → `SetSettingsAsync` + best-effort `TrySave`. The per-app feature should **reuse these snapshots**,
  not invent a new format or borrow OTD's console/tray code.
- OTD's own ["Windows App-specific FAQ"](https://opentabletdriver.net/Wiki/FAQ/WindowsAppSpecific) is
  **manual per-app workarounds** (osu! settings, enabling Windows Ink for Photoshop/Krita) — not
  auto-switching.
- The nearest upstream issue, [#4119 "Layered Settings"](https://github.com/OpenTabletDriver/OpenTabletDriver/issues/4119),
  is **multi-tablet** global/override profiles, not per-app, and sits in their backlog.
- **No community plugin** doing per-app switching was found.

## Recommended approach: reuse our Saved Settings snapshots

| Layer | Who owns it | Status |
|---|---|---|
| Per-app config **storage** (the snapshot) | **us** — a Saved Settings JSON snapshot | **exists** (`PresetsViewModel`) |
| **Apply** a config live | **OTD** — `SetSettings(snapshot)` via our `_daemon.SetSettingsAsync` | **exists** |
| **Live-apply-only path** (apply without persisting to disk) | **us** | **new** — see Risk 3 |
| **app → snapshot name** mapping | **us** — small JSON in AppSettings | new |
| **Foreground-window watcher** | **us** — Win32 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` | new |
| **Switch policy** (debounce, fall back to a default snapshot for unmapped apps) | **us** | new |
| **UI** — assign an app to a snapshot; pick from running processes or browse to an exe | **us** | new |

Most of the building blocks already exist. The genuinely new code is the **foreground watcher**, the
**exe→snapshot mapping + UI**, and a **live-apply-only** variant of the apply path (today's
`ApplyAndSaveSettingsAsync` always persists — see Risk 3).

### Reference code we *can* read (the only novel OS-integration piece)

- **Microsoft PowerToys** (MIT) — uses `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` in multiple modules
  (FancyZones, Awake). The most robust real-world reference.
- [fjl's C# gist](https://gist.github.com/fjl/4080259) — minimal "detect foreground window switch."
- [microsoft/CsWin32 discussion #1162](https://github.com/microsoft/CsWin32/discussions/1162) and
  [pinvoke.net SetWinEventHook](https://www.pinvoke.net/default.aspx/user32.setwineventhook) — P/Invoke
  signatures. The hook thread needs a message loop; pin the delegate against GC.

## Risks / open questions (in priority order)

1. **Switch latency & mid-stroke safety — the make-or-break.** `SetSettings` disposes and
   reconstructs the daemon's output pipeline. Rapid alt-tabbing, or a switch *while the pen is down*,
   could cause a hitch, a dropped report, or a visible glitch mid-stroke. **Must be measured by a
   spike before anything else is built.** Likely mitigations: debounce focus changes; defer a switch
   until the pen lifts (we already stream pen-down state via `DaemonPenInputSource`).
   **Success criteria for the spike (proposed):** a switch completes in **≲50 ms** with **no dropped
   report** when the pen is up, *or* defer-until-pen-up keeps perceived lag **≲100 ms**. Below that =
   go; above, or visible glitches that can't be hidden = no-go.
2. **Our app must be running.** OTD won't switch on its own; this only works while OTDWindowsHelper is
   alive (tray/background mode #72 — done ✅). When the app is closed, the last-applied snapshot stays.
3. **Persistence collision (concrete).** `ApplyAndSaveSettingsAsync` **always** persists via `TrySave`
   ([AppSession.cs:435](../../OTDWindowsHelper/Services/AppSession.cs)), so reusing it for every focus
   change would overwrite the user's on-disk default with whatever app is in front. The switcher needs
   a **live-apply-only** path (`SetSettingsAsync` *without* `TrySave`), plus restore-the-default on
   exit. Keep the exe→snapshot mapping in *our* AppSettings, never in OTD's settings file.
4. **Coexistence with OTD's own UI** is cosmetic — a user running OTD's UI would see the preset flip.
5. **"App" identity.** Map by process executable name/path (robust) vs. window title (fragile). Prefer
   exe path; expose a running-process picker + browse-to-exe.
6. **Multi-tablet** interaction (a snapshot is whole-`Settings`, all tablets) — out of scope for v1;
   document the limitation.

## Recommended phasing (if pursued)

1. **Spike (gates everything):** wire a throwaway `SetWinEventHook` foreground watcher to apply two
   existing Saved Settings snapshots via `SetSettingsAsync` *only* (no disk write); **time the switch**
   and feel it mid-stroke against the success criteria above. Decide go/no-go and whether
   defer-until-pen-up is required.
2. **Switch service:** `IForegroundAppWatcher` (Win32 impl behind an interface, for testability and
   the macOS seam per #140) + a debounced switcher with default-snapshot fallback.
3. **Mapping store:** `app exe → snapshot name` in AppSettings; restore-default-on-exit.
4. **UI:** a "Per-app profiles" view — list mappings, add via running-process picker / browse, assign
   an existing Saved Settings snapshot, enable/disable the whole feature.
5. **Docs:** USERMANUAL section + note the "app must be running" caveat.

## Effort & recommendation

Medium. Storage + apply already exist (Saved Settings + `SetSettingsAsync`); the real work is the
foreground watcher, the mapping UI, and a live-apply-only path, and the real risk is switch
performance. **Recommendation: greenlight the spike only.** Build nothing user-facing until the
latency/mid-stroke spike proves `SetSettings` switching meets the success criteria. If the spike
fails, this feature is likely impractical on the current OTD daemon and should go back to backlog
with that finding recorded.

## Sources

- [OTD Windows App-specific FAQ](https://opentabletdriver.net/Wiki/FAQ/WindowsAppSpecific)
- [OTD #4119 — Layered Settings](https://github.com/OpenTabletDriver/OpenTabletDriver/issues/4119)
- [OTD #1715 — Presets PR](https://github.com/OpenTabletDriver/OpenTabletDriver/pull/1715)
- [Settings & Profiles (DeepWiki)](https://deepwiki.com/OpenTabletDriver/OpenTabletDriver/2.3-settings-and-profiles)
- [fjl — foreground-switch C# gist](https://gist.github.com/fjl/4080259)
- [microsoft/CsWin32 — SetWinEventHook discussion](https://github.com/microsoft/CsWin32/discussions/1162)
- [pinvoke.net — SetWinEventHook](https://www.pinvoke.net/default.aspx/user32.setwineventhook)
