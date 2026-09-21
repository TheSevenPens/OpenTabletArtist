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
   path." No diff — it re-pulls and rebuilds. One exception since #737: while a per-app override is
   live (`ISettingsCoordinator.HasEphemeralOverride`), the reload deliberately **skips** the settings
   read, because the daemon is holding a transient snapshot and adopting it would make that snapshot the
   editor's baseline. Device data still refreshes.

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
daemon only, no disk, no reload (per-app switching), and it sets the override flag above;
`ClearEphemeralOverrideAsync` ends that override by putting the daemon back on `CurrentSettings`.

Since #740 all of this lives in `Services/SettingsCoordinator.cs` rather than `AppSession` — the session
keeps the `ISettingsCoordinator` contract and the UI-thread guards, and orchestrates apply-then-reload, so
the coordinator holds no reference back to it.

**A disk write that fails is retried on the next reload** (#743). The daemon and the disk fail
independently, so an apply can leave a change live but unpersisted; the reload — window focus or the 30 s
poll — calls `RetryPendingPersistAsync`, which rewrites it and returns the save chip to "Saved". The
common cause is the file being briefly locked while OTD's own UX writes the same `settings.json`, and
that clears itself. Bounded (10 attempts per pending change, reset by any new edit) so a permission
problem, which won't clear, stops retrying instead of warning on every poll for as long as the app is open.

The only client-side gate is a **no-op guard**: OTA skips the write if the serialized settings are
byte-identical to what it last loaded (`_lastLoadedSettingsJson`), plus an apply-loop circuit-breaker.
Neither is concurrency control — they only suppress redundant *self*-writes.

## Conflict semantics: last-writer-wins

There is **no merge and no version/ETag** anywhere. Since #491 there is one client-side check, described
under the scenario below; it is a read immediately before the write, not a guarantee:

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
- **(b) Does OTA's next apply clobber it?** **No, since #491 — it holds the change instead.** Immediately
  before sending, OTA re-reads `GetSettings` and compares it against what it last read from *that same
  daemon*. If they differ, nothing is sent: the apply returns `ChangedElsewhere`, the save chip says so,
  and the editor keeps the held edit rather than letting the next reload adopt the daemon's version over
  it. Taking the daemon's version is the banner's **Reload**, which is a decision the artist makes.

  If the read fails, times out (2 s) or answers nothing, the change is held on the same terms and
  reported as `CouldNotCheck` instead. Proceeding there would abandon the protection exactly where the
  state is least certain: a daemon that will not say what it holds is not evidence that it holds what we
  last saw. The cost is deliberate — a daemon answering writes but not reads now refuses edits it used
  to take.

  **Resolving it is the artist's, and it is per draft (#906).** A held change comes back with a
  `SettingsHold`: what that draft was weighed against. Every later submission of the same draft — the
  banner's **Try again**, and equally just carrying on editing, which submits by another route —
  presents that hold, so the draft is compared against the state it was held against rather than against
  whatever a reload has learned since. Learning somebody's settings is not consent to replace them.

  Two ways out, both decisions the artist makes. **Reload** takes the daemon's version, and the editor
  then drops its hold. **Keep my change** sends the draft back with the `SettingsConflict` it was shown;
  the session re-reads the daemon and writes only if what is there is still what the artist looked at,
  refusing with a fresh conflict otherwise. An authorised write that does not land resolves nothing, and
  neither does a refusal: the hold stands until something the artist did actually settles it.

  A hold names the session that issued it and the connection it was taken on. One that matches neither is
  **refused** (`HoldNotApplicable`) rather than ignored: falling back to this session's own baseline reads
  as the careful choice, but that baseline can match the daemon exactly while the draft belongs somewhere
  else — so the check passes and a foreign draft is written.

  The reachable case is a **reconnect to the same daemon**, which moves the connection without moving the
  daemon's identity, so it does not go through the #905 daemon-switch path. The editor keeps the draft and
  **stops submitting anything** until the artist has seen the current settings and taken them; the banner
  says so. Neither of the other two ways out applies — trying again would be the ordinary apply that must
  not run, and the conflict the editor was holding belonged to the connection that has gone. Clearing the
  protection instead would let the next edit through against a baseline the reconnect's reload has already
  brought level with the daemon, which is the original hole; discarding the draft would lose the artist's
  work for a reason #905 does not supply, since that is a *different* daemon and this is the same one.

  What a hold guarantees is narrower than it first looks. It is **not** that a hold only ever makes a
  write harder: if the daemon moves away from the expected state and back again, the held draft is taken
  while an ordinary submission of the same edit is held, because a reload advanced the ordinary baseline
  in between. The invariant is *compare this draft against its own unchanged expectation*. A hold belongs
  to one draft, is dropped when that draft is resolved or replaced, and is never attached to a later one.

  **Snapshot and stamp are one published value.** An editor quotes its stamp back when the artist accepts
  what it is showing, and the session honours an acceptance whose stamp is the one it is publishing — so
  reading the settings and the stamp separately, at either end, hands out an older snapshot under a newer
  stamp and makes that acceptance an agreement to something nobody was shown. The coordinator replaces one
  field whole (including on a daemon switch, since the session generation is half of every stamp), and
  both adoption routes — reconciliation and an editor's own Refresh — read it once.

  The hold lives on the draft rather than on the session because editors are cached and two can be live
  at once. With one shared expectation, whichever artist resolved first cleared it, and the other's
  draft — still built on the older state — was then found to agree with the daemon and written. There is
  now nothing shared for a decision to clear.

  **Which writes are covered:** the ordinary apply-and-save path. `ApplyLiveOnlyAsync` and the
  restore-default path deliberately do not check — restoring a saved default is an intentional
  replacement of whatever is there, not an edit that could be built on a stale read.

  It is a **narrowing, not a fix**. Two writes can still interleave inside the gap between that read and
  the write, and the check does not apply before the first read or against a daemon the baseline did not
  come from.

- **(c) What does a reload reconcile?** It discards OTA's stale view and adopts the daemon's current
  state (updates the display via `ReconcileOpenTabletDetails`). It reconciles what's *shown*; it does not
  merge unsaved OTA edits — a reload overwrites OTA's view, and a later apply re-pushes that reconciled
  view.

**Net:** the window of risk is between OTA's last reload and its next apply. In practice it's small because
the focus-reload fires whenever OTA regains focus (you almost always alt-tab to OTA before editing in it),
and the 30 s poll bounds the staleness otherwise. But there is no hard guarantee — a settings edit made in
another tool and an OTA apply that races it will resolve last-writer-wins with no warning.

## Implications / possible improvements (not implemented)

- ~~**Detect-before-clobber**~~ — done in #491; see the scenario above. What remains unimplemented is
  the *reconciling* half: OTA holds the change and asks, rather than merging an edit that touched a
  different profile or tablet than the external one. A merge needs a field-level model this layer does
  not have.
- **Push on change:** a daemon-side settings-changed event would make external edits reflect instantly
  rather than on focus/poll — but that's an upstream OTD change, out of OTA's control.
- **Subscribe to `Resynchronize`** (#204): currently ignored; reacting to it (re-pull) would at least catch
  the forced-resync path.
