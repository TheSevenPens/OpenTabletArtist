# Tablet configurations — handling in OpenTabletArtist

How OpenTabletArtist (OTA) deals with OpenTabletDriver (OTD) **tablet configuration** files: the
per-tablet JSON definitions (digitizer identifiers, report parser, specifications) that let the daemon
recognise and drive a tablet. Covers the base/override model, where the files live, the **CONFIGS** tab,
and two features layered on top: installing approved configs from OTD's repo (#480) and warning when a
tablet is on a custom override instead of the vetted base (#467).

## Background: how OTD loads configs

There are two sources, merged by the daemon:

1. **Base configs (built-in, vetted).** Shipped *inside the daemon* as embedded assembly resources.
   `OpenTabletDriver.Configurations/DeviceConfigurationProvider` enumerates every `*.json` manifest
   resource in the `OpenTabletDriver.Configurations` assembly. The source JSONs live in the OTD repo at
   `OpenTabletDriver.Configurations/Configurations/<Manufacturer>/<Model>.json`. OTA references this
   assembly directly, so it can enumerate the base set **in-process**:
   `new DeviceConfigurationProvider().TabletConfigurations` → each a `TabletConfiguration` with a `.Name`.

2. **Override folder (user-writable, shadows the base).** `DesktopDeviceConfigurationProvider` reads loose
   `*.json` from `AppInfo.ConfigurationDirectory` (recursively) and **merges by `TabletConfiguration.Name`**
   — a file whose `Name` matches a built-in **replaces** that built-in (the daemon logs
   `Overriding tablet configuration '{Name}'`). A file with a new `Name` simply **adds** support for a
   tablet the base set doesn't cover.

So the model per tablet is: **base (vetted) ← optionally overridden/added by a same-folder file.**

### Where the override folder actually is (and a bug we fixed)

On Windows the daemon resolves `AppInfo.ConfigurationDirectory` to the first existing of:
`…\userdata\Configurations` (portable install), `%LOCALAPPDATA%\OpenTabletDriver\Configurations`, or
`<cwd>\Configurations`. **Not** Roaming AppData.

OTA's original `ConfigurationsDirectoryProvider` hard-coded **Roaming** `%APPDATA%\OpenTabletDriver\
Configurations`, so the CONFIGS tab could browse a *different, often empty* folder than the one the daemon
loads. Both features here depend on reading/writing the folder the daemon **actually** uses, so the
authoritative source is the daemon itself: `IDriverDaemon.GetApplicationInfo()` →
`AppInfo.ConfigurationDirectory` (already reachable via `DaemonClient.GetAppInfoAsync()`; `AppSession`
already captures the sibling preset/settings/plugin dirs from the same call). The heuristic path is kept
only as a fallback when the daemon isn't connected.

## The CONFIGS tab

`CustomTabletConfigsView` / `CustomTabletConfigsViewModel` (nav label **CONFIGS**, enum
`AdvancedTab.CustomTabletConfigs`). It's a manager for the **override folder**: list the loose config
JSONs (friendly name via `TabletConfigNaming.FriendlyName`, filename, size), view the pretty-printed
JSON, open the folder, and delete a file. It has no concept of the base set, no download, and no diffing —
the two features below add exactly those.

## Config trust spectrum

For any tablet, its effective config sits somewhere on:

| State | Meaning | Feature |
|---|---|---|
| **Base built-in** | vetted, shipped in the bundled daemon | — (the good state) |
| **Approved, not-yet-bundled** | in OTD's repo, vetted for a future release, absent from your daemon | **#480** — offer to install |
| **Custom override of a base** | a folder file shadows a same-named base config | **#467** — warn (off the vetted base) |
| **Custom new** | a folder file for a tablet with no base config | legitimate (unsupported tablet); not warned |

## Decisions

1. **#480 offers new-tablet configs only (v1).** We only surface repo configs whose tablet isn't already
   in the bundled base set. This is the clear win (make a not-yet-released tablet work) and it sidesteps
   the collision with #467 — a new-tablet config adds a new `Name`, it never shadows a base, so installing
   it can't trip the override warning. Offering *updated* versions of already-supported configs is future
   work (it would need provenance tracking so a sanctioned update isn't flagged as a hand-edit).

2. **#467 warns only on overrides of a base.** The warning fires when a folder file's `Name` collides with
   a base config `Name` (it's shadowing vetted defaults). A **fully-custom** config (new `Name`, no base)
   is *not* warned — that's the legitimate way to support an unlisted tablet.

3. **Repo ref = the bundled OTD branch.** The daemon submodule is pinned at **v0.6.7 on `0.6.x`**, so #480
   fetches from `0.6.x` — which is also the branch the issue links. Matching the bundled branch avoids
   pulling a config that references a report-parser type the bundled daemon doesn't have.

4. **Parser-compatibility guard.** Even on the right branch, a very new config can reference a report
   parser absent from the bundled daemon (it would silently fail to load). On install we validate the
   downloaded JSON parses as a `TabletConfiguration`; a missing-parser check is a follow-up refinement.

5. **Folder path comes from the daemon** (see above) — the prerequisite both features build on.

## Feature designs

### #480 — install approved configs from OTD's repo

1. **List** the repo's config files with one request: the GitHub git-trees API,
   `GET /repos/OpenTabletDriver/OpenTabletDriver/git/trees/0.6.x?recursive=1`, filtered to
   `OpenTabletDriver.Configurations/Configurations/**.json`. (Trees API returns the whole tree in one
   response; we avoid per-file requests at browse time.)
2. **Diff** against what the user already has: exclude any candidate already covered by the **bundled base
   set** (in-process `DeviceConfigurationProvider`) or already present in the **override folder**. Browse
   matching is best-effort by normalised manufacturer+filename.
3. **Present** the remaining candidates grouped by manufacturer, with a derived display name.
4. **Install** on selection: download that file's raw content
   (`raw.githubusercontent.com/OpenTabletDriver/OpenTabletDriver/0.6.x/<path>`), parse it as a
   `TabletConfiguration`, and — authoritatively — **skip if its `Name` is already in the base set** (this
   closes any browse-time false "new" from step 2). Otherwise write it into the override folder. A daemon
   re-detect / restart picks it up.

Network is best-effort: offline or a failed fetch shows a clear status, never blocks the tab.

### #467 — custom-override warning

A per-tablet health check (`HealthEvaluator`), fed by a new input in `HealthService.GatherInputs`:

- Compute the **base `Name` set** (in-process `DeviceConfigurationProvider`).
- Scan the **override folder** (daemon path) for `*.json`; a file whose `Name` is in the base set is an
  **override of a base**.
- If the **active tablet** matches such an override, emit a `HealthSeverity.Recommendation`
  `HealthIssue` (stable id `tablet.configOverride:{Name}`) explaining it's running a custom config that
  replaces OTD's vetted default, with a **Fix** remediation deep-linking to the CONFIGS tab (a new
  `RemediationArea`).
- A matching `Force*` toggle in `DeveloperSettings` (mirroring `ForceTabletMappingCustom`) lets the card
  be previewed without a real override file.

It's a *Recommendation* (not Misconfigured/Broken): an override is often deliberate, but the user should
know they're off the vetted base — useful context when debugging odd behaviour or seeking support.

## Not doing (yet)

- Offering **updated** versions of already-supported configs via #480 (needs provenance so a sanctioned
  update isn't flagged by #467).
- A **missing-parser** pre-check before install (currently we validate JSON shape only).
- Auto re-detect after install (we prompt/rely on the daemon's folder re-scan; a restart is the reliable
  path).
