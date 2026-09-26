# Health diagnostics library

Tracked by [#963](https://github.com/TheSevenPens/OpenTabletArtist/issues/963).
This first delivery covers evaluator extraction (#964) and validation (#965).

## Dependency direction

`OtdHealth` targets `net10.0`, matching OTA. It has no project or package references, needs no OTD
submodule, and performs no I/O. `OpenTabletArtist` references it alongside `OtdInterop`.
`OtdInterop` remains independent of health policy. There is no extra runtime in the OTA distribution.

The application still collects snapshots in `HealthService`. Its `Domain.Health.HealthEvaluator`
translates those facts into `OtdHealth.HealthSnapshot`, presents shared findings through
`HealthIssuePresenter`, and adds app-only notices: legacy daemon paths, app-settings recovery,
tray availability, and developer-induced warnings. OTA owns text, card grouping, navigation,
buttons, event subscriptions, observable collections, and every fix.

The health library owns diagnostic decisions and severity for Windows Ink/VMulti, manufacturer-driver
conflicts, elevation, daemon provenance/version/permission observations, tablet pen/mapping/dynamics/config
facts, and Linux prerequisites. Its version comparison is also used by OTA's existing binary-version reader.

## Evaluating supplied evidence

Reference `OtdHealth/OtdHealth.csproj` from a .NET 10 consumer:

```csharp
using OtdHealth;
using System.Text.Json;
using System.Text.Json.Serialization;

var snapshot = new HealthSnapshot
{
    Platform = HealthPlatform.Windows,
    DaemonConnected = true,
    WinInkInstalled = true,
    VMultiInstalled = false,
    Tablets = [new("Tablet A", Detected: true, OutputModeIsWinInk: true,
        PressureDisabled: true)],
};
var findings = HealthEvaluator.Evaluate(snapshot);
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() },
};
Console.WriteLine(JsonSerializer.Serialize(findings, options));
```

This yields `vmulti.notInstalled` (Broken) and `tablet.pressureDisabled` (Recommendation, subject
`Tablet A`). A caller can inspect `Code`, `Severity`, `TabletName`, and typed `Evidence` without
parsing prose. `Id` combines code and tablet name; callers must supply unique tablet names.
Results sort by descending severity and ordinal ID. Keep input collections stable during evaluation;
the evaluator never mutates them and copies the module names included in output evidence.

Findings are individual facts. OTA combines `daemon.foreign`, `daemon.sourceUnknown`, and
`daemon.versionMismatch` into its existing `otd.driver` card. It combines deliberate Windows Ink
opt-out and disabled tip/pressure/tilt into `tablet.penBehavior:<name>`, preserving row order and actions.
The library's `process.elevated` becomes OTA's existing `app.elevated`; the two distinct HID permission
codes share OTA's existing `linux.hidAccess` card ID. These presentation IDs are not the library codes.

## Evidence and policy limits

- Evaluation analyzes supplied observations. **No findings does not certify that a machine is healthy
  or that collection completed.** Defaults mean no problem was reported. `WinInkInstalled` and
  `VMultiInstalled` are nullable so an unknown installation is not reported as absent. Other flags
  retain the current catalog's observation semantics; complete probe-status modeling is #966.
- `Platform` refers to the machine being analyzed, independent of the evaluator's host. Windows stack
  checks require Windows; Linux prerequisite proxies require Linux and no detected tablet. macOS
  permission inference is supplied by the caller and gated on connection, as in the existing catalog.
  It is not a direct permission query; live verification remains [#721](https://github.com/TheSevenPens/OpenTabletArtist/issues/721).
- Expected daemon release, ownership, deliberate Windows Ink opt-out, and dynamics expectations are
  consumer policy. The library does not discover OTA installations or read OTA preferences. OTA still
  suppresses dynamics warnings on foreign daemons before supplying the snapshot.
- Tablet checks apply to detected tablets. The app already excludes stale mapping facts on undetected
  profiles; the library enforces that boundary for independent consumers too.
- Release matching intentionally preserves numeric major/minor/patch comparison, ignoring revision
  and suffixes. It is a compatibility warning policy, not a full semantic-version support resolver.
- The existing catalog emits no daemon-disconnected finding: OTA has a separate connection card.
  A headless collector/tool must report inability to collect daemon facts as incomplete analysis (#966).
- The evaluator does not manage daemon reachability UI or execute remediation. It does not start or
  stop OTD, grant permissions, install drivers, or write configuration.

## Validation

Run from the repository root so `global.json` selects Microsoft Testing Platform:

```powershell
dotnet build OtdHealth/OtdHealth.csproj
dotnet test --project tests/OtdHealth.Tests/OtdHealth.Tests.csproj
dotnet test --project tests/OpenTabletArtist.Tests/OpenTabletArtist.Tests.csproj --filter-class '*Health*'
```

The first two commands need only this repository and the .NET 10 SDK (plus restored xUnit for tests),
with no initialized OTD submodule. The library suite guards its dependency graph, platform applicability,
unknown installation states, version semantics, separate findings, ordering, evidence copying, and JSON
consumption. OTA tests retain text/action/service coverage and verify complete presenter coverage.
Build and release workflows explicitly run the new suite.

## Remaining pieces

- [#966](https://github.com/TheSevenPens/OpenTabletArtist/issues/966): shared read-only evidence collection,
  explicit unavailable/unsupported/failed observations, cancellation, partial reports, and platform validation.
- [#967](https://github.com/TheSevenPens/OpenTabletArtist/issues/967): console analyzer with text/JSON output
  and documented exit codes, usable from PowerShell. It will use the shared collection layer for live analysis.

No console analyzer or public NuGet package ships in this first extraction. The library currently requires
a .NET 10 host; invoking a future console executable from PowerShell does not require loading its DLL into
the PowerShell runtime. Direct module hosting and broader framework support need separate validation.
