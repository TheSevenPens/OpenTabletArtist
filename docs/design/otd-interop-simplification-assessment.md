# OTD Interop simplification assessment

Assessment date: September 21, 2026. Source baseline: `6f56723`.

This records the original assessment. The user subsequently requested implementation; see [the implemented settings model](otd-interop-settings-workspace.md) for current behavior and validation. It incorporates the discussion's preferences: automatic reconnect to the same daemon should remain; removing automatic per-app profile switching is acceptable; a simpler external-edit recovery flow is possible, provided its user cost is clear.

## Recommendation

Keep OTD Interop and its headless boundary. Reduce the behaviors it must coordinate:

1. Apply edits immediately, but save the daemon's persistent settings only on an explicit Save.
2. Select the daemon at application startup. A different daemon selection takes effect after restarting OTA.
3. Reconnect automatically to the same daemon configuration, using a fresh connection session and fresh settings. Do not replay old queued writes or saves.
4. Remove automatic per-app profile switching.
5. Use one settings workspace shared by all OTA editors. Replace per-editor conflict resolution with one application-wide pause and explicit reload when an outside edit is detected.
6. Serialize settings reads, applies, saves, and explicit reloads through one operation path. Keep UI callbacks and artist-specific policy out of that path.

These changes should materially simplify the library and the app together. Manual Save by itself offers a smaller improvement. Hiding the daemon-switch button while retaining all current reconnection and state-preservation semantics offers a smaller improvement too.

The objective is fewer independent states and fewer possible interactions. Fewer tests should follow from removed behaviors; deleting tests first would leave the complexity and its defects intact.

## What the code and history show

The original extraction really did move existing code. Commit `5bdd6a8` moved `SettingsCoordinator` with very little implementation change; `39759f3` moved the daemon client. The split itself was reasonable. Later work expanded the integration contract considerably: connection identity, destination discovery, retained capabilities, orderly shutdown, conflict detection, per-draft holds, and publication identity.

At the assessed revision:

| Measurement | Observed value |
|---|---:|
| Library C# files | 31 |
| Library source lines, including comments and blank lines | 6,289 |
| Nonblank library lines excluding lines beginning with `//` | 2,021 |
| `SettingsCoordinator.cs` | 2,247 lines; 769 under the same filtered count |
| `OtdSession.cs` | 1,402 lines; 433 under the same filtered count |
| Library test classes/files named `*Tests.cs` | 20 files |
| Source-declared `[Fact]` / `[Theory]` methods in those files | 243 |
| Lines in those test files | 9,106 |

These are source counts, not executed test-case counts; theories can produce several cases. They exclude additional OTA unit/UI coverage and test support files. Comment filtering is a rough size measure, not a language parser.

Immediately before the first library commit, the app's coordinator was 614 lines, or 299 under that filtered count. Its concurrency test file had 18 test methods; the current counterpart has 56. These comparisons illustrate growth, not a claim that all new code was invented rather than moved from elsewhere.

The large files are partly unusually extensive contract documentation. The deeper problem is the number of simultaneously relevant facts: a UI draft, published settings, the last observed daemon settings, the last persisted settings, a pending save, a temporary override, a save destination still being discovered, connection identity, publication identity, and shutdown progress.

For example, a failed save can be waiting when a reconnect occurs, metadata can arrive after that reconnect, and a cached editor can still hold a conflict token from the old connection. Every individual safeguard is understandable. Their combinations explain why apparently small changes require several review rounds.

## Improvements without deliberate feature reductions

**Make one component own the editable settings.** Tablet editors currently retain whole `Settings` snapshots even though each edits one tablet. The per-draft hold design explicitly accommodates several cached editors. Instead, keep one headless application workspace; editors submit targeted changes to that workspace, and it builds the whole-object payload OTD requires. A mapping change for tablet A then does not submit a stale copy of tablet B. Local targeted updates do not require an upstream patch API. Keep short-lived input state where needed, but stop treating every cached view as an independent settings document.

**Use one sequencing rule.** The current coordinator serializes mutations, while reloads use an observation epoch to avoid overtaking them. The app also has a reload gate; per-app switching has another apply lock. A common settings operation path can serialize reads and writes and remove many overlapping-read cases. Coalesce slider changes before that path so ordinary editing stays responsive. A serialized path still needs time limits and a terminal state for an uncertain or broken connection.

**Remove host callouts from the middle of settings mutations.** Today the library runs supplied policy and reports save progress through callbacks, while also requiring a host execution context and suitable synchronization-context continuations. Prepare artist-specific policy in the application workspace. Return detached results from library operations; let the app update its UI after completion. Transport events should queue work or publish immutable notifications, not synchronously re-enter the settings state machine. This reduces the need to defend every intermediate step against host reentrancy.

**Separate settings refresh from device refresh.** `AppSession.LoadDataCoreAsync` currently reads tablets, settings, app metadata, updates profile data, may apply/persist filter cleanup, retries persistence, raises `DataLoaded`, and initiates plugin work. A successful user edit also invokes this broad reload. Keep necessary settings readback, but avoid rebuilding unrelated state after every settings operation. Tablet hotplug can refresh devices without silently replacing a draft; if OTD also changes settings during detection, handle that observation explicitly.

**Keep abstractions proportional to the actual consumer.** The project already says this is an application split, not a broadly published SDK. Maintain useful seams for RPC and file I/O. Simplify general publication, lifetime, and extension contracts where OTA does not need them. This is not a reason to expose raw settings writes throughout the application.

These are worthwhile even if every feature stays. They should improve comprehensibility, but keeping every current behavioral guarantee still requires substantial tests.

## Explicit Save: worthwhile, with precise semantics

OTD's `SetSettings` changes live state; it does not normally persist the settings file. The official UX writes the file on Save. OTA currently applies and writes on each edit, then retains and retries a failed disk write.

The proposed UX is:

| Action or event | Result |
|---|---|
| Change a setting | Apply promptly; show `Applied — not saved` after confirmation |
| Click Save | Finish pending editor input and apply, then persist the confirmed settings |
| Save fails | Keep `Applied — not saved`; show the error and leave Save available |
| Choose Reload from daemon | Read the daemon's current live settings; do not imply they are saved |
| Choose Revert to saved | Read the saved file and apply it; report failure if either step fails |
| Load a named preset | Apply it as the current workspace; require Save to make it persistent |
| Exit with unsaved settings | Offer Save, Exit without saving, or Cancel |

Save should wait for pending debounced input, and briefly prevent another edit from overtaking the saved snapshot. The simplest rule is to disable relevant settings controls during that short save operation. Saving a stale snapshot while labeling a newer edit as saved would recreate the same complexity under a different name.

No automatic persistence retries, no save on focus, and no silent save on exit. Local OTA preferences such as theme and window settings can retain their current persistence behavior; they are a different document.

Benefits include removing `_pendingPersistSettings`, `_pendingDestination`, automatic retry counts, and the distinction between a manual persistence retry and a reload-driven persistence retry. The user directly controls a failed save's next attempt.

Costs include an extra action and the possibility of losing unsaved changes when the daemon restarts. Exiting OTA does not necessarily undo a live change: an external daemon may keep running it until that daemon exits. Closing the window to the tray is not exiting. The UI must explain persistence in terms of a daemon restart.

Dirty state must compare against the saved document or a verified saved snapshot. Starting OTA or reconnecting must not assume that what the daemon currently runs matches disk: another client could have applied without saving. An unreadable saved file means persistence is unknown, not clean. Backup recovery is recovery, not proof that the main file is good.

All existing settings-writing routes must follow this model: tablet editing, preset loading, tray mapping, monitor-cycle hotkeys, calibration/setup operations, developer tools, and automatic filter cleanup. A few hidden autosave exceptions would undermine both the UX and the simplification. Startup cleanup should become an explicit repair or a visible applied-but-unsaved change, not an incidental write during a refresh.

**Manual Save does not solve competing live edits.** OTD still replaces its entire live settings object on each apply. External conflict detection belongs before live apply, not only at Save.

## Startup daemon selection and automatic reconnect

Preserve the requested automatic reconnect, but separate it from changing daemon configuration.

- At startup, resolve the actual connection and its application/settings paths before enabling settings writes. Selection means the startup target, not permission to assume that the shared named pipe belongs to the requested executable.
- A different daemon selection is recorded for the next OTA launch.
- After a connection breaks, disable settings writes, end the old session, discard queued operations, and attempt reconnect to the same daemon configuration.
- On reconnect, construct a fresh session and load live settings and saved-state information before enabling edits. Do not replay a stale whole-settings payload or a failed save.
- Retire old editors' pending work. Old completions must not update the new workspace. A small lifetime check remains necessary even when the sessions are separate objects.

“Same daemon” should mean the same installation/configuration, not the same PID: an intentional restart changes PID. Executable and settings paths help identify it, but this repository's identity discovery is imperfect, particularly outside Windows or when process inspection is unavailable. If OTA cannot establish the expected target, remain read-only with an explanation rather than silently adopting a different one. This is a remaining platform validation task, not a promise the current RPC API supplies.

If only the connection broke and the daemon kept running, live unsaved settings may still be there. If the daemon process restarted, it normally reloads saved settings and those changes may be gone. Reload and describe what was observed; do not automatically overwrite it to reconstruct the earlier state. A read-only/exportable recovery copy is a possible later convenience, but automatically reapplying it would add back the recovery contract we are removing.

An intentional daemon restart, including one required by a plugin update, should resolve unsaved changes before stopping the daemon. The current plugin-update path does restart it. An unexpected restart cannot give that opportunity.

This removes *migration of settings work between connections*, not all reconnection complexity. Transport retry, identity checks, debug-stream resubscription, event cleanup, and refusal of late results remain. An initial connection retry also remains useful when the daemon is still starting.

## External edits: what pause and reload feels like

Recommend a single application-wide pause for settings writes. Keep the rest of the UI usable. Do not create separate conflict banners or decisions for every cached tablet editor.

Example: OTA shows pressure 50. The user changes it to 60 in OTD's UI, then returns to OTA and tries 55. OTA notices the outside change before applying 55. It stops writes and says:

> Settings changed outside OpenTabletArtist. Your latest change was not applied. Reload current driver settings to continue.

The primary action is **Reload current driver settings**. Its explanation states that pending local edits will be discarded. Reload reads again at the moment of the action, clears pending editor input, and rebuilds the shared workspace. It does not save and does not apply anything.

The user costs are concrete:

| Situation | User impact | Keep the cost contained |
|---|---|---|
| Alternating between OTD UX and OTA | Extra reload and possibly repeating the last adjustment | Recommend one settings editor at a time; one notice per incident |
| A debounced slider edit had not been sent | That local adjustment is discarded on reload | Never discard while merely showing the notice; explain what reload replaces |
| Earlier OTA edits were applied but unsaved | An outside whole-object write may already have overwritten them | Do not claim reload caused or can undo that loss |
| The outside change affected a different tablet | The whole settings workspace still pauses | State that this is a deliberate tradeoff for avoiding merge logic |
| A helper repeatedly changes settings | Repeated pauses make editing irritating | Explain that the other settings writer needs to be stopped |
| The daemon cannot answer a settings read | Cannot tell whether anything changed | Show a connection/read failure, not an invented conflict |
| Reloaded live settings differ from disk | Reload still leaves an unsaved state | Keep Reload, Save, and Revert to saved distinct |

If OTA has no pending local input, it can adopt a newly observed daemon snapshot automatically and announce the refresh unobtrusively. The consequential case is a local edit built on older settings: then stop and ask the user to reload. After a pause begins, polling must not silently clear it and resubmit the old draft.

The strongest simplification removes **Keep my change**, opaque conflict/hold tokens, per-draft resubmission rules, and acceptance of a particular stamped publication. It also removes the promise that a rejected draft survives reconnect and can later be safely resubmitted. The user can repeat their adjustment after reloading.

Still retain a basic pre-apply comparison against the last confirmed live state. This is best-effort detection: OTD has no atomic compare-and-set, so another writer can change settings between OTA's read and write. “One editor at a time” is a supported-use rule, not an exclusive lock OTA can enforce on other clients. Perfect simultaneous editing needs daemon-side protocol support.

## Remove automatic per-app switching; simplify manual presets too

Removing automatic per-app profile switching removes an independent writer driven by foreground-app events. It also removes the need for OTD Interop to keep a published default while the daemon runs something else, skip settings reload during that override, and restore the default on exit.

Retain named presets and manual switching if useful. Treat selecting one, whether from a menu or a hotkey, as loading a new current workspace and applying it. It remains unsaved until Save. If the existing workspace differs from disk, require an explicit replacement decision before loading another preset, just as opening a different unsaved document would.

The current manual preset service also distinguishes a live override and a saved default. With universal explicit Save, that special mode can become ordinary load/apply/revert behavior. Otherwise some override complexity remains even after per-app switching is removed.

This is an additional recommended UX change, not something already approved in the discussion. It may make frequent preset hotkeys less convenient when unsaved changes exist. Keep named preset files and existing per-app mappings on disk during migration so removal of the active feature does not unnecessarily delete user data.

## A smaller division of responsibilities

**OTA's headless settings workspace** owns the editable document, unsaved state, debounce/coalescing, artist policy, and the global editing/reload decision. All OTA settings writers use it. UI views translate actions into workspace changes and display state.

**OTD Interop** owns one connection at a time, typed daemon operations and events, settings serialization/format repairs, and reliable file I/O. Its settings operations have one serialization rule and return detached snapshots or clear failures. A save targets the path established for that connection and the confirmed snapshot it is meant to persist. Closing stops new work; a broken session never changes into a different daemon beneath an admitted operation.

**The application connection owner** retries opening a fresh session for the startup-selected target and activates a new workspace only after initialization succeeds. It does not carry a pending-operation recovery system of its own.

Exact class boundaries are secondary. A small controller can remain in OTD Interop if that keeps ownership clearer. Moving today's coordinator verbatim into OTA, or reproducing it in a connection manager, would fail the objective. Measure source and tests across both projects.

Do not build a general actor framework or generic transaction engine for this. One operation gate or a small single-consumer queue, one connection lifetime, and one settings workspace should be sufficient for the proposed contract. Async events must not interrupt an operation by mutating its state directly.

## Protections worth retaining

- Detached snapshots: later UI edits must not change an in-flight payload or the file being saved.
- Safe settings-file replacement and backup recovery; do not revert to OTD's delete-before-write serializer.
- OTD-compatible decoding and preservation of unknown plugin/settings data.
- The small null Absolute-mode area repair on outgoing settings.
- Truthful apply and save results, including disconnected and uncertain outcomes.
- Bounded connection/operation waits, terminal handling after uncertain writes, and late-result suppression.
- Serialized saves and explicit handling of a file changed elsewhere. A simple comparison before replacing the saved file can pause the operation, but cannot make competing uncoordinated writers atomic.
- Debug subscription ownership, event cleanup, and appropriate cross-platform transport coverage.

An important protocol limitation: in this checkout, `DriverDaemon.SetSettings` catches failures, can restore/recover settings, raises `Resynchronize`, and still returns a completed task. `DaemonClient` currently reports success after that RPC returns. Therefore retain a settings readback after apply, and compare/adopt the daemon's result before saying the requested edit succeeded or saving it. Account for legitimate daemon normalization instead of assuming byte-for-byte input identity. A readback verifies the observed settings state, not whether every plugin or hardware behavior works correctly. A failure to obtain that readback is uncertain, not evidence the change had no effect.

Do not remove every post-apply read in pursuit of simplicity. Remove the broad UI/device reload around it. A small real-daemon integration test should exercise recovery behavior that the transport fake's success flag cannot represent.

## What happens to tests

The current tests are largely understandable consequences of the current contract. Review uncovered real ordering and ownership problems; the answer is to retire requirements and implementations together.

| Current area | Expected treatment under the proposal |
|---|---|
| Persistence retry and destination readiness | Remove automatic retry and late-destination migration scenarios; replace with startup readiness and explicit save failure cases |
| `SettingsHold`, `SettingsConflict`, overwrite, resubmit, acceptance stamps | Remove those APIs and their multi-editor/reconnect combinations; test a global pause and fresh reload |
| Ephemeral override apply/clear/restore | Remove with per-app switching; retain straightforward preset load and revert tests |
| Reconnect ordering and session identity | Reduce to fresh-session lifecycle, target validation, and no old-work replay; do not delete reconnect coverage |
| Reentrancy and execution-context pumps | Much can disappear if no host code runs inside mutation operations and library state owns its synchronization |
| Publication epochs/stamps | Replace overlapping read/write combinations with tests of the single ordering rule; retain protection against stale UI completions |
| Shutdown | Fewer outstanding work types; still test shutdown during an operation, bounded exit, and late replies |
| Codec, file safety, format guard, snapshot ownership | Retain focused behavioral tests |
| Real transport / daemon contract | Retain or add a small integration layer for behavior fakes cannot prove |

For scale, 50 current library test methods sit in reconnect/identity files, 15 in destination readiness, 29 in shutdown, and 18 in context/reentrancy files. Those 112 methods are not all removable. They show how much coverage is attached to lifecycle machinery rather than basic settings conversion. Another 56 live in the broad coordinator concurrency file, covering several overlapping features.

Do not promise a percentage reduction before a vertical implementation proves it. The credible prediction is removal of whole scenario families, followed by a smaller suite organized around startup, apply, save, reload, reconnect, and close. Parameterizing an unchanged set of scenarios is cosmetic reduction; deleting unsupported states is substantive reduction.

Keep a small number of UI tests for Save state, pending slider input, the pause notice, reconnect, and exit decisions. Headless workspace tests should cover most workflows. Keep a boundary check ensuring the library does not acquire a UI dependency. A fully snapshotted public API is lower priority for this private application library than behavioral guarantees, and can be reconsidered rather than maintained as a published-SDK promise.

## Implementation sequence and proof of simplification

1. Agree the small product contract: explicit Save, startup target, fresh-session reconnect, no per-app switching, and the external-edit/preset replacement decisions above. Update older design documents that still prescribe universal autosave.
2. Create one end-to-end path for a representative setting: edit, confirmed live apply, unsaved indicator, explicit Save, safe file replacement. Use a shared workspace and one sequencing rule. Validate pending slider input and OTD recovery/readback.
3. Route the remaining settings writers through that workspace. Remove automatic per-app activity and normalize manual preset behavior. No hidden writers should remain.
4. Replace connection migration with fresh-session reconnect. Test same-target restart, mismatched/unknown targets, late completions, and plugin-update restart with unsaved changes.
5. Replace held-draft resolution with the app-wide pause/reload path. Retire corresponding APIs and tests only as the replacement becomes active.
6. Delete dead retry, override, publication, and lifecycle machinery. Measure the combined app/library code and test surface. Keep the old regression tests that still describe supported behavior.

Use small coherent changes rather than a wholesale rewrite or permanent parallel old/new coordinators. The acceptance test for the design is that a developer can explain each operation's ordering without consulting a collection of hold tokens, generations, destination lookup states, and retry exceptions. If the new code recreates those mechanisms in another project, reconsider before migrating further.

## Evidence map

- `OtdInterop/SettingsCoordinator.cs`: mutation serialization at 538; reload/observation handling at 1237; apply-and-save at 1465; automatic retries at 1709; explicit retry/destination discovery at 1728; separate live/ephemeral/restore operations at 1887 onward.
- `OtdInterop/OtdSession.cs`: connection transitions, destination discovery, ownership of settings access, execution context, and multi-phase close.
- `OtdInterop/IOtdSettingsSession.cs`, `IOtdExecutionContext.cs`, `SettingsHold.cs`, `SettingsConflict.cs`: current host obligations and conflict contract.
- `OpenTabletArtist/Services/AppSession.cs`: broad load at 827; cleanup write at 986; persistence retry at 1010; apply-then-reload at 1106; daemon restart at 1446.
- `OpenTabletArtist/ViewModels/TabletDetailViewModel.cs`: editor-held settings, draft generations, reconciliation at 1096, held results at 1285, and resubmission at 1361.
- `OpenTabletArtist/Services/PerAppSwitcher.cs`, `PerAppApplier.cs`, `ProfileSwitchService.cs`, `QuitSequence.cs`: temporary profile ownership and restoration.
- `OpenTabletArtist/Services/PluginInstallApplier.cs`: an updated plugin triggers daemon restart.
- `external/OpenTabletDriver/OpenTabletDriver.Daemon/DriverDaemon.cs`: `SetSettings` at 218, recovery at 277, startup load at 350, `GetSettings` at 636.
- `external/OpenTabletDriver/OpenTabletDriver.UX/MainForm.cs`: Save at 549 writes the settings file; Apply at 597 calls the daemon.
- `OtdInterop/AtomicFile.cs`, `SettingsFileStore.cs`, `SettingsCodec.cs`, `ProfileSanitizer.cs`: useful mechanisms to keep.
- `tests/OtdInterop.Tests` and `tests/OpenTabletArtist.UiTests/TabletEditorReconcileTests.cs`: existing lifecycle, conflict, and ownership coverage.

Validation for this assessment was source and Git-history inspection. No implementation changes were made and no test suite was executed. Estimated simplification remains a design judgment to validate with the first vertical implementation.
