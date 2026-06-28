# Feasibility — per-application settings (#167)

> Status: **investigation only.** No commitment. The ask: typical manufacturer drivers (Wacom,
> XP-Pen, Huion) let you define a *global* config plus *per-application* overrides for mapping,
> dynamics, pen switches, and tablet buttons, and switch automatically as the foreground app
> changes. This doc assesses whether we can offer the same on top of OpenTabletDriver.

## TL;DR

**Feasible, and smaller than first expected — but it is a feature we own end-to-end, gated by one
unknown (switch latency / mid-stroke safety).** OTD has *no* per-application concept of its own, so
the auto-switching layer is entirely ours. The good news: OTD already ships a **Presets** primitive
(named settings files + `ApplyPreset(name)` on the daemon API), so we don't have to build profile
storage — each app maps to an OTD preset by name, and our job shrinks to **(1) a foreground-window
watcher** and **(2) an app→preset mapping + UI**. The make-or-break risk is that `ApplyPreset`
rebuilds the daemon pipeline on every focus change; a timed spike must confirm switching is fast and
glitch-free (especially mid-stroke) before we build the rest. The tray/background mode (#72) is a
hard prerequisite and is already shipped.

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

- **No application concept.** The daemon model is `Settings → Profiles[]` keyed by *tablet*
  (name + `PersistentId`), plus global `Tools[]`. Nothing keys on a process or foreground window.
  (`OpenTabletDriver.Daemon.Contracts/Persistence/Settings.cs`, `Profile.cs`.)
- OTD's own ["Windows App-specific FAQ"](https://opentabletdriver.net/Wiki/FAQ/WindowsAppSpecific) is
  **manual per-app workarounds** (osu! settings, enabling Windows Ink for Photoshop/Krita) — not
  auto-switching.
- The nearest upstream issue, [#4119 "Layered Settings"](https://github.com/OpenTabletDriver/OpenTabletDriver/issues/4119),
  is **multi-tablet** global/override profiles, not per-app, and sits in their backlog.
- **No community plugin** doing per-app switching was found.
- **Presets primitive exists** ([PR #1715](https://github.com/OpenTabletDriver/OpenTabletDriver/pull/1715)) —
  named settings JSON files, switchable from the tray menu or a hotkey, exposed on the daemon API:

  ```csharp
  Task<IEnumerable<string>> GetPresets();
  Task ApplyPreset(string name);
  Task SaveAsPreset(string name);
  ```

Everything the issue wants (mapping, dynamics, pen switches, tablet buttons) lives in a `Profile`
(`OutputMode`, `Filters`, `Bindings`), and a preset bundles a full settings snapshot — so a preset is
a complete, self-contained "this app's config."

## Recommended approach: build on OTD Presets (not raw profile snapshots)

| Layer | Who owns it |
|---|---|
| Per-app config **storage** (the snapshot) | **OTD** — each app's config is a named OTD preset |
| **Apply** a config live | **OTD** — `ApplyPreset(name)` |
| **app → preset name** mapping | **us** — a small JSON in our AppSettings |
| **Foreground-window watcher** | **us** — Win32 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` |
| **Switch policy** (debounce, fall back to a "default/global" preset for unmapped apps) | **us** |
| **UI** — assign an app to a preset, pick from running processes or browse to an exe | **us** |

This keeps our new surface minimal and composes with OTD's existing manual preset switching (a user
can still flip presets by hand or hotkey; we just automate it by foreground app).

### Reference code we *can* read (the only novel OS-integration piece)

- **Microsoft PowerToys** (MIT) — uses `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` in multiple modules
  (FancyZones, Awake). The most robust real-world reference.
- [fjl's C# gist](https://gist.github.com/fjl/4080259) — minimal "detect foreground window switch."
- [microsoft/CsWin32 discussion #1162](https://github.com/microsoft/CsWin32/discussions/1162) and
  [pinvoke.net SetWinEventHook](https://www.pinvoke.net/default.aspx/user32.setwineventhook) — P/Invoke
  signatures. The hook thread needs a message loop; pin the delegate against GC.

## Risks / open questions (in priority order)

1. **Switch latency & mid-stroke safety — the make-or-break.** `ApplyPreset` (like `SetTabletProfile`)
   rebuilds the daemon pipeline. Rapid alt-tabbing, or a switch *while the pen is down*, could cause a
   hitch, a dropped report, or a visible glitch mid-stroke. **Must be measured by a spike before
   anything else is built.** Likely mitigations: debounce focus changes; defer a switch until the pen
   lifts (we already stream pen-down state via `DaemonPenInputSource`).
2. **Our app must be running.** OTD won't switch on its own; this only works while OTDWindowsHelper is
   alive (tray/background mode #72 — done ✅). When the app is closed, the last-applied preset stays.
3. **Persistence collision.** Don't let auto-switching call `SaveSettings()` / overwrite the user's
   intended on-disk default. Keep our mapping in *our* store; on exit, restore the user's chosen
   default preset.
4. **Coexistence with OTD's own UI** is cosmetic — a user running OTD's UI would see the preset flip.
5. **"App" identity.** Map by process executable name/path (robust) vs. window title (fragile). Prefer
   exe path; expose a running-process picker + browse-to-exe.
6. **Multi-tablet** interaction with presets (a preset is whole-settings, all tablets) — out of scope
   for v1; document the limitation.

## Recommended phasing (if pursued)

1. **Spike (gates everything):** wire a throwaway `SetWinEventHook` foreground watcher to
   `ApplyPreset` between two real presets; **time the switch** and feel it mid-stroke. Decide go/no-go
   and whether defer-until-pen-up is required.
2. **Switch service:** `IForegroundAppWatcher` (Win32 impl behind an interface, for testability and
   the macOS seam per #140) + a debounced switcher with default-preset fallback.
3. **Mapping store:** `app exe → preset name` in AppSettings; restore-default-on-exit.
4. **UI:** a "Per-app profiles" view — list mappings, add via running-process picker / browse, assign
   an existing OTD preset, enable/disable the whole feature.
5. **Docs:** USERMANUAL section + note the "app must be running" caveat.

## Effort & recommendation

Medium. The storage/apply is free (OTD presets); the real work is the foreground watcher + UI, and
the real risk is switch performance. **Recommendation: greenlight the spike only.** Build nothing
user-facing until the latency/mid-stroke spike proves `ApplyPreset` switching is acceptable. If the
spike fails, this feature is likely impractical on the current OTD daemon and should go back to
backlog with that finding recorded.

## Sources

- [OTD Windows App-specific FAQ](https://opentabletdriver.net/Wiki/FAQ/WindowsAppSpecific)
- [OTD #4119 — Layered Settings](https://github.com/OpenTabletDriver/OpenTabletDriver/issues/4119)
- [OTD #1715 — Presets PR](https://github.com/OpenTabletDriver/OpenTabletDriver/pull/1715)
- [Settings & Profiles (DeepWiki)](https://deepwiki.com/OpenTabletDriver/OpenTabletDriver/2.3-settings-and-profiles)
- [fjl — foreground-switch C# gist](https://gist.github.com/fjl/4080259)
- [microsoft/CsWin32 — SetWinEventHook discussion](https://github.com/microsoft/CsWin32/discussions/1162)
- [pinvoke.net — SetWinEventHook](https://www.pinvoke.net/default.aspx/user32.setwineventhook)
