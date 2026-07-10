# Settings synchronization: daemon ↔ clients

How OpenTabletDriver (OTD) settings stay in sync between the daemon and its clients — OpenTabletArtist
(OTA), OTD's own UX, and the OTDWindowsHelper — and what happens when more than one of them changes
settings at once. This documents the concern in **#162** (OTDWindowsHelper + OTD UX both editing
settings) and #204 (the daemon's `Resynchronize` event).

**TL;DR:** settings changes are **pull-only** (no change event), and writes are **last-writer-wins on the
whole `Settings` object** with no merge or version check. OTA notices external edits by re-pulling on
focus (~1 s) or a 30 s fallback poll — not by a push.

## The daemon's model

The daemon holds a single in-memory `Settings` and drives the tablet from it
(`OpenTabletDriver.Daemon/DriverDaemon.cs`):

- **`GetSettings()`** — returns the in-memory `Settings` verbatim. Pure read.
- **`SetSettings(settings)`** — replaces the in-memory `Settings` **wholesale** and reconfigures the live
  driver (rebuilds output modes, bindings, filters, tools). It does **not** write `settings.json`, and it
  raises **no client notification** on success.
- **`ResetSettings()`** — `SetSettings(Settings.GetDefaults())`; in-memory only.
- **Disk:** the daemon reads `settings.json` **once at startup** (`LoadUserSettings()`), and only *writes*
  it on a fresh install where no file exists yet. There is **no file watcher and no timer** — it never
  re-reads the file on its own. Disk persistence during normal operation is entirely the **client's** job.

So whoever calls `SetSettings` owns the live state; the daemon is a pure in-memory holder + driver.

## Events a client can subscribe to (push)

Over StreamJsonRpc on the `OpenTabletDriver.Daemon` pipe (multiple clients may connect and all receive the
same broadcasts). Declared on `OpenTabletDriver.Desktop/Contracts/IDriverDaemon.cs`:

| Event | Raised when | OTA reaction |
|---|---|---|
| `TabletsChanged` | tablet add/remove, sleep/wake | full reload (`LoadDataAsync`) |
| `Message` | every log line | Console page only |
| `DeviceReport` | per raw report, **only while debug enabled** | pen-test / debug streams |
| `Resynchronize` | **only** on a failed `SetSettings`, or explicit `ForceResynchronize()` | **not subscribed** |

**There is no settings-changed event.** A successful settings edit by any client is invisible to the
others until they pull. `Resynchronize` is a failure/forced-recovery signal, not a normal change notice.

## How OTA stays in sync (pull)

OTA (`Services/AppSession.cs`, `Services/DaemonClient.cs`, `ViewModels/MainViewModel.cs`) re-pulls the
whole state — tablets + settings + app-info — on four triggers, always from the **daemon** (`GetSettings`),
never from `settings.json`:

1. **On connect** — the `Connected` handler runs `LoadDataAsync`.
2. **`TabletsChanged`** — `AppSession` subscribes and reloads. (Fires on plug/unplug/sleep — usually *not*
   on a pure settings edit, so it's an incidental trigger for settings.)
3. **Window focus / activation** — `MainWindow.Activated → MainViewModel.OnWindowActivated →
   AppSession.ReloadAsync`, throttled to once per 750 ms. This is the **fast path**: alt-tab back to OTA
   after editing in the OTD UX and it refreshes within ~1 s.
4. **Fallback poll** — `AppSession.PollDataAsync` loops every **`FallbackPollInterval` = 30 s** and reloads
   if connected. Its own comment: "a safety net in case an event is missed, not the primary detection
   path." No diff — it unconditionally re-pulls and rebuilds.

A reload flows through `LoadDataCoreAsync` → `DataLoaded`, and `MainViewModel.ReconcileOpenTabletDetails`
updates any open tablet page so an external edit replaces stale values on screen.

## The write path (who persists what)

An OTA edit (`AppSession.ApplyAndSaveSettingsAsync`) does two independent writes:

1. **Daemon** — `SetSettingsAsync(settings)` sends the **entire `Settings` object** → the daemon replaces
   its in-memory state and reconfigures the driver (live effect, not persisted).
2. **Disk** — `SettingsFileStore.TrySave(settings, SettingsFilePath)` serializes to `settings.json`.
   `SettingsFilePath` is the daemon's own `AppInfo.SettingsFile` — the same file the daemon reads at
   startup and **the same file OTD's own UX writes on Save**.

Variants: `ApplyLiveOnlyAsync` = daemon + reload, no disk (temporary override); `ApplyEphemeralAsync` =
daemon only, no disk, no reload (per-app switching).

The only client-side gate is a **no-op guard**: OTA skips the write if the serialized settings are
byte-identical to what it last loaded (`_lastLoadedSettingsJson`), plus an apply-loop circuit-breaker.
Neither is concurrency control — they only suppress redundant *self*-writes.

## Conflict semantics: last-writer-wins

There is **no merge, no version/ETag, no optimistic-concurrency check** anywhere:

- **Daemon:** `SetSettings` overwrites the whole `Settings`. The last caller wins completely; any field a
  concurrent editor changed is lost.
- **Disk:** both OTA and OTD's UX serialize the *same* `settings.json` with no locking. Two saves close
  together interleave and the last serialize wins the file — independent of, and possibly inconsistent
  with, whichever `SetSettings` reached the daemon last (nothing coordinates the RPC write with the disk
  write).

## The concurrent-editor scenario (#162)

If OTD's UX (or the OTDWindowsHelper) changes settings while OTA is open:

- **(a) Does OTA find out?** Not by a push — the daemon has no settings-changed event and OTA ignores
  `Resynchronize`. OTA learns only by pulling: incidentally on `TabletsChanged`, on **window focus**
  (~1 s), or on the **30 s fallback poll**.
- **(b) Does OTA's next apply clobber it?** **Yes, if OTA applies before it reloads.** OTA sends its whole
  in-memory `Settings` (based on its last load), overwriting the external edit in both the daemon and
  `settings.json`. The no-op guard doesn't help — it only compares against OTA's own last-loaded copy, not
  the daemon's current state.
- **(c) What does a reload reconcile?** It discards OTA's stale view and adopts the daemon's current
  state (updates the display via `ReconcileOpenTabletDetails`). It reconciles what's *shown*; it does not
  merge unsaved OTA edits — a reload overwrites OTA's view, and a later apply re-pushes that reconciled
  view.

**Net:** the window of risk is between OTA's last reload and its next apply. In practice it's small because
the focus-reload fires whenever OTA regains focus (you almost always alt-tab to OTA before editing in it),
and the 30 s poll bounds the staleness otherwise. But there is no hard guarantee — a settings edit made in
another tool and an OTA apply that races it will resolve last-writer-wins with no warning.

## Implications / possible improvements (not implemented)

- **Detect-before-clobber:** before an apply, OTA could re-pull `GetSettings` and, if the daemon's state no
  longer matches OTA's last-loaded copy, warn / reconcile instead of overwriting. This closes the
  #162 window without needing a daemon change.
- **Push on change:** a daemon-side settings-changed event would make external edits reflect instantly
  rather than on focus/poll — but that's an upstream OTD change, out of OTA's control.
- **Subscribe to `Resynchronize`** (#204): currently ignored; reacting to it (re-pull) would at least catch
  the forced-resync path.
