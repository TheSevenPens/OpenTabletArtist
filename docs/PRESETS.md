# Presets

*(Settings → Presets tab.)* At the top, a pinned **Current settings** card ("In use now") represents the configuration your tablet is using right now — the live `settings.json` you edit on the Tablet pages. It's what hotkey switches revert to, and every **preset** below is a *saved copy* of it.

A **preset** is a named backup of your entire OTD configuration (all tablets' settings). Cards show the preset name and file last-modified time, sorted newest first. Presets are what **Load** and preset hotkeys apply.

> Note: a **preset** is a whole-configuration backup saved by OpenTabletArtist — not OpenTabletDriver's own per-tablet `Profile`. Each preset file contains the complete OTD `Settings` (all tablets).

On the Current settings card:

- **Save as preset** — Saves the current settings as a copy with an auto-numbered name: `Preset`, `Preset 2`, `Preset 3`, ... (lowest available number is reused if you delete one). Rename freely after saving.
- **Browse** — Opens the presets folder in Explorer.

Each preset card has:

- **Load** — Applies the preset and makes it your **Current settings** (this is a permanent switch — see [Switching presets](#switching-presets)).
- **Update** — Overwrites the preset with the current settings.
- **Duplicate** — Saves a copy of the preset under a new auto-named `<name> copy` (numbered if that's taken, e.g. `<name> copy 2`). The copy is independent — it doesn't carry the original's hotkey.
- **Rename** — Prompts for a new name (simple text dialog, no file picker).
- **Delete** — Removes the preset file after a confirmation prompt.

The "No presets" empty state appears only when the presets folder is actually empty.

## Switching presets

There are several ways to change the active configuration, split by whether the change is **permanent** (rewrites your Current settings) or **temporary** (a live override that's restored later).

Whole-preset switches:

| How | Where | Effect |
|---|---|---|
| **Load** a preset | Presets page | **Permanent** — applies it and makes it your Current settings. |
| **Preset hotkey** | Settings → Hotkeys | **Temporary** — a global keyboard shortcut applies a preset as a live override; a "Preset override" chip shows while active. Your Current settings are untouched. |

Monitor-mapping switches (change only which monitor the tablet maps to — all **permanent**):

- **Cycle mapped monitor** hotkey (Settings → Hotkeys).
- **Switch Display** in the system-tray menu.
- The display picker on a tablet's page.

Notes:

- Only **Load** is a permanent whole-preset switch; the preset hotkey is temporary and leaves your Current settings intact.
- There's currently no one-click "back to Current settings" button for a hotkey-driven override — it clears when you switch again.
