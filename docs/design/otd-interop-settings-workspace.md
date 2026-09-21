# OTD settings workspace and explicit Save

Implemented on `codex/simplify-otd-interop`, starting from `6f56723`. This supersedes the autosave, temporary preset override, per-app switching, and per-editor conflict protocols described in earlier design notes.

## User behavior

- An edit applies to the driver immediately (sliders retain their short debounce). The footer says **Applied — not saved** until the live settings match the saved file.
- **Save**, Ctrl+S, or Cmd+S flushes pending input, waits for admitted applies, verifies the driver's current values and saved-file fingerprint, and atomically writes the confirmed settings. Failures stay visible; nothing retries a disk write automatically.
- **Reload driver settings** accepts the driver's current live document. It discards pending local input and clears an external-change pause. It neither restores the saved file nor saves anything.
- **Revert to saved** confirms replacing the live document with the driver's saved file. It applies without saving. A missing or damaged primary may be recovered from the last good backup; the recovered document remains unsaved if it differs from the primary.
- Loading a preset, including a hotkey load, replaces the normal editable document. Edits afterward use the same rules as every other edit. There is no temporary override document or override indicator.
- Loading a preset, quitting, stopping, or restarting the driver prompts about unsaved work. Save finishes the pending edits and persistence first; Cancel stops the action; Continue without saving does not undo values already live on the driver. Revert has its own discard confirmation.
- Closing the window to the tray keeps OTA running and does not prompt.
- Choosing another driver location changes the next OTA launch. It does not reconnect or restart the current driver. If another copy occupies the OTD pipe, it must be stopped before the next launch can use the selection.
- Restart relaunches the executable first used by this OTA session. A changed preference cannot silently redirect it.
- Unexpected disconnects still reconnect automatically. Each connection gets a fresh settings session and reads what the driver now holds. Old queued writes and local input are discarded, never replayed. A pipe interruption may retain live unsaved settings; a daemon restart usually loses them.
- A different executable or settings-file location on reconnect is read-only until OTA restarts. Unidentifiable connections are also read-only.
- Automatic per-app profile switching and its UI have been removed. Existing per-app mapping files are left on disk.

## Outside edits and their cost

OTA expects to be the active settings editor. Focus refresh, tablet notifications, the fallback poll, and preflight reads detect changes to the live document. A mismatch pauses settings changes for the whole app. File-only changes are checked before Save.

The artist sees the pause in the persistent footer, uses Reload, and reviews the driver's current values. Unsaved edits already overwritten by another client cannot be recovered automatically; pending local input is discarded on reload. Opening the OTD UX merely to inspect settings does not trigger a pause. Actually changing settings there can require an extra reload after returning to OTA.

There is deliberately no merge, force-overwrite choice, retained conflict draft, or per-editor conflict banner. OTD has no atomic compare-and-set operation: another client can still write between a preflight read and OTA's write. This is a single-editor workflow with best-effort conflict detection, not a multi-client transaction protocol.

OTD may normalize bindings or recover from a failed apply while reporting successful RPC completion. OTA reads back every apply. If values differ from those requested, editing pauses for reload and review. This conservatively treats legitimate normalization like rejection; it avoids silently claiming the request succeeded.

An unconfirmed write timeout closes that settings session. Reload cannot race the potentially late write. The footer asks for a driver restart before editing or saving. Same-driver reconnect remains automatic when the connection is actually replaced.

## Implementation boundary

```text
UI input and debounce
  -> SettingsWorkspace (one shared draft, latest apply, app policy)
  -> IOtdSettingsSession
       Apply / Save / Refresh / Reload / RestoreSaved
       one operation gate, one captured connection, one file path
  -> OTD RPC + atomic settings-file store
```

**OtdSession** owns transport, identifies the executable and settings path, pins that identity, and opens a new settings session for each connection. Events are delivered on the transport thread; the app dispatches them and rejects obsolete connection notifications.

**SettingsCoordinator** contains the ordered settings operations. It owns the last confirmed live document, the saved-file snapshot, and a pause flag. Callers receive copies. It has no UI scheduler, host policy callback, draft hold, conflict acceptance stamp, save retry, or cross-connection state migration.

**SettingsWorkspace** belongs to OTA. All cached tablet editors submit into this shared document. A tablet edit replaces only that tablet's profile, so a stale second tablet editor cannot overwrite the first tablet's changes. Whole-document operations are explicit preset loads. Artist filter policy runs here on intentional edits; read-only loads never clean filters or write settings.

**AppSession and MainViewModel** own display state, prompts, flushing/discarding UI input, and process lifecycle. The library does not depend on Avalonia or the application. Its raw settings transport and file writer remain internal.

Retained compatibility protections include OTD serialization, detached inputs, channel-bound writes, null-area repair, apply readback, atomic file replacement and backup recovery. Save repairs invalid null areas live and verifies that repair before persisting, if such a document was loaded directly from the driver.

## Size and validation

Compared with `6f56723`, counting all C# source and helpers while excluding build output:

| Measure | Before | After |
| --- | ---: | ---: |
| OtdInterop C# files | 31 | 23 |
| OtdInterop source lines, including comments | 6,289 | 1,944 |
| OtdInterop nonblank lines excluding line comments | 2,021 | 1,050 |
| Interop test Fact/Theory methods | 243 | 46 |
| Interop test C# source lines, including helpers/comments | 9,716 | 1,720 |

The tests for retired protocols were replaced with scenario tests for the smaller contract. Coverage includes apply without persistence, explicit-save failure/retry, external live and file edits, readback rejection, timeout/late completion, connection replacement, identity pinning, shutdown, backup integrity, two cached tablet editors, policy ownership, input flush/discard, and restart targeting. Existing named-pipe transport, boundary dependency, codec/file, and UI suppression tests remain.

The full solution build completed with zero warnings and zero errors. All 1,092 test cases passed: 49 interop, 976 application logic, and 67 headless UI cases. Run `dotnet test OpenTabletArtist.slnx` for all suites. The read-only check is `dotnet run --project tools/OtdDaemonSwitchCheck -- --read-only`. During implementation it timed out waiting for a ready daemon, so physical-tablet behavior and real daemon apply/save/restart still need an interactive smoke check. No live driver settings were changed by that diagnostic.
