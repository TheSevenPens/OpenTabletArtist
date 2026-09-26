# Read-only health collection and analyzer

Implements [#966](https://github.com/TheSevenPens/OpenTabletArtist/issues/966) and
[#967](https://github.com/TheSevenPens/OpenTabletArtist/issues/967), following the pure evaluator in
[#963](https://github.com/TheSevenPens/OpenTabletArtist/issues/963).

## Boundaries

`OtdHealth` remains a BCL-only evaluator. `OtdHealth.Collector` references it and `OtdInterop`; it owns
profile interpretation, metadata/configuration reads, conflict parsing, and native environment probes.
`OtdInterop.DiagnosticsConnection` exposes only five read RPCs and Windows pipe-server identification.
It connects once, does not reconnect automatically, and disposing it closes only the client connection.
`tools/OtdHealthCheck` references the collector, with no OTA, Avalonia, MVVM, or rendering dependency.

OTA captures detached profiles and display bounds on its UI thread, supplies its ownership, expected
release, Ink opt-out and dynamics policy, and awaits the same collector. Collection superseded by a
refresh or disposal cannot update the UI. `HealthService.Analysis` retains the real report; developer
overrides and app notices are layered afterward. App-native display enumeration remains a presentation
adapter (Avalonia on Linux/macOS); mapping interpretation is shared. The existing configuration browser
can still display known overrides alongside unreadable files; diagnostic collection reports that read
as failed instead of claiming a clean folder.

## Using the library

```csharp
using var connection = new OtdInterop.DiagnosticsConnection();
var report = await HealthCollector.CollectAsync(
    LiveHealthSource.Create(connection),
    new HealthPolicy { ExpectedOtdVersion = "0.6.7.0" },
    cancellationToken: cancellationToken);
```

`HealthSources` supplies read-only delegates for alternate transports, captured sessions, and tests.
Create a fresh source for each analysis. Read delegates must not mutate machine state. The collector
runs them off the caller's thread with a five-second per-probe deadline and a thirty-second total
deadline by default. Cancellation/deadline results retain evidence already collected. A native or
custom source that ignores cancellation may finish in the background, but its late result cannot alter
the returned report. Disposing a diagnostic connection also interrupts pending pipe reads.

The report contains `Snapshot`, freshly evaluated `Findings`, `RequestedCoverage`, `Probes`, and
computed `IsComplete`. Each probe records its ID, requested/applicable flags, outcome, and structured
failure code/message/type. Outcomes are `Completed`, `NotApplicable`, `Unavailable`, `Unsupported`,
`Failed`, and `Cancelled`. Completion requires every requested applicable observation to complete;
absence of findings is never used as evidence of completion. Missing daemon access leaves independent
host checks available. Unknown Ink/VMulti installation remains null. Defaults in the snapshot do not
certify that other probes succeeded; always inspect the report.

Coverage can be restricted. Daemon-dependent probes require `Daemon`; mapping, overrides and macOS
access additionally require `Profiles`. The complete flag refers only to the explicitly listed coverage.
Reports do not certify the authenticity or freshness of evidence supplied by another caller.

## Probe inventory and limits

| Probe | Evidence / limitation |
| --- | --- |
| Daemon | Existing named pipe for the current user; never launches or discovers installations. |
| DaemonVersion | Windows pipe-server PID, executable resource, managed DLL fallback. OTD has no version RPC; live headless Linux/macOS report `Unsupported` rather than guessing a process. OTA can supply its observed version. |
| Profiles | Existing settings and detected tablets; strict read failures stay failures. No settings normalization, repair or writeback. The reads are sequential, not an atomic daemon transaction; a detected tablet without a settings profile yields an incomplete report. |
| Displays | Mapping against captured bounds; CLI enumerates physical pixels on Windows with temporary thread DPI awareness. Live headless Linux/macOS enumeration is `Unsupported` when needed. Consumers can supply their own bounds. No detected tablets makes this probe inapplicable. |
| ConfigurationOverrides | Recursive JSON read in the daemon's reported directory, compared with the embedded pinned OTD configuration catalog. Unknown path or malformed/unreadable data cannot certify no overrides. |
| WindowsInk | Installed metadata, checked against the caller's expected OTD release. Does not download a repository or load plugin code. |
| VMulti | Windows SetupAPI present device nodes, including disabled and driverless-node classification; does not install, enable, or open a control channel. |
| DriverConflicts | Existing daemon detection log, including OTA self-match exclusion. Reflects that log's latest available observations, not a fresh manufacturer-driver scan. |
| ProcessElevation | The collecting Windows process (OTA or CLI), not the daemon. |
| LinuxUdev | Known OTD rule paths under `/etc` and `/usr/lib`; presence proxy, not a udev-rule validator. |
| LinuxModules | `/proc/modules` plus the two existing OTA blacklist paths. Not a complete interpretation of all modprobe includes. |
| LinuxHidAccess | Opens hidraw nodes read-only without reading reports; `/etc/group`, current process groups, and the systemd runtime directory. These retain OTA's existing heuristics, not a per-tablet permission proof. |
| MacOSAccess | Supported VID/PID visible to the daemon with no detected tablet: the existing Input Monitoring inference, not a direct macOS permission query. The catalog is the pinned OTD build. |

Linux prerequisites only apply when no tablet is detected. Windows-only probes are inapplicable on
other platforms. Failures are not converted to “installed,” “not installed,” or “no conflict.” The live
Windows smoke test was exercised; Linux/macOS CI exercises portable logic and the protocol with fake
sources, not native device/permission integration. Unsupported coverage remains explicit.

Caller-provided `ProfileObservation.Id` is independent of display name. OTD profiles have no durable
GUID, so the live adapter uses an escaped config name plus occurrence *within that name*. These IDs
remain stable when unrelated profiles reorder and distinguish duplicate names. They identify config
slots, not physical USB devices; renaming a profile or reordering otherwise indistinguishable same-name
slots changes correlation. A consumer with durable device IDs should supply those IDs directly. OTA
keeps these IDs through presentation; synthetic developer samples use evaluation-local IDs.

No probe installs drivers, starts/stops daemons, edits settings, enables tablet debugging, or performs
remediation. File reads and Linux permission-check opens can still fail or block, which is why coverage
and deadlines are part of the public contract.

## Console and PowerShell

Initialize the OTD submodule and use the .NET 10 SDK:

```powershell
dotnet run --project tools/OtdHealthCheck -- --help
dotnet run --project tools/OtdHealthCheck -- --json
dotnet run --project tools/OtdHealthCheck -- --snapshot snapshot.json
dotnet run --project tools/OtdHealthCheck -- --report report.json --json
dotnet run --project tools/OtdHealthCheck -- --coverage Daemon,Profiles,DriverConflicts

dotnet publish tools/OtdHealthCheck -c Release -o publish/health-check
dotnet publish/health-check/OtdHealthCheck.dll --json > report.json
$analysisExitCode = $LASTEXITCODE
$report = Get-Content report.json -Raw | ConvertFrom-Json
$report.Findings | Select-Object Code, Severity, TabletId, TabletName, Evidence
```

Live mode is the default. `--timeout SECONDS` sets the per-probe deadline (0 < seconds <= 300; the
overall deadline remains 30 seconds). `--pipe NAME` supports alternate/test daemons. `--expected-version`
defaults to the pinned OTD release `0.6.7.0`; it is policy and does not imply the connected version.
Supply numeric versions. Live-only flags cannot silently override a saved input.

`--snapshot` accepts an `OtdHealth.HealthSnapshot` JSON object. It evaluates its supplied facts without
touching a daemon or host probes. Completeness is **unavailable**, JSON `IsComplete` is null, and the
exit code is 2 even if there are no findings. `--report` accepts a saved collector/CLI report containing
snapshot, coverage and probe outcomes; it re-evaluates findings and recomputes completion, rather than
trusting saved `Findings` or `IsComplete`. JSON uses enum names, separate tablet name/ID, evidence,
coverage, probe failures and the underlying snapshot. Text prints the same findings and probe outcomes.
Reports can include local paths and tablet configuration details; choose where to share them.

| Exit | Meaning |
| --- | --- |
| 0 | Requested coverage complete; no findings above `Information`. |
| 1 | Requested coverage complete; at least one `Recommendation`, `Misconfigured`, or `Broken` finding. |
| 2 | Incomplete/failed collection, unknown snapshot completeness, invalid arguments, or unreadable/invalid input. |
| 130 | User cancellation or a supplied report containing a cancelled probe. |

Precedence: cancellation, then incomplete/invalid, then actionable, then 0. Actionable findings still
appear in an incomplete report. Ctrl+C cancels live analysis and preserves partial report output when
collection has begun. Input/argument errors go to stderr, with no success-shaped JSON on stdout.

The published command is framework-dependent and needs .NET 10. Invoking it from PowerShell does not
load the library into PowerShell's runtime; direct hosting in Windows PowerShell 5.1 is unsupported.
NuGet publication, a PowerShell module, native Linux/macOS display discovery and direct macOS permission
verification remain separate work.

## Validation

```powershell
dotnet test --project tests/OtdHealth.Collector.Tests/OtdHealth.Collector.Tests.csproj
dotnet test --solution OpenTabletArtist.slnx -c Debug
```

Tests use injectable sources and a named-pipe fake daemon; they do not require OTD installed or modify
an installation. They cover partial/cancelled/timed-out reports, missing versus malformed metadata,
policy, duplicate subject names, unchanged inputs, actual command-process JSON/text and exit codes,
and a restore-output dependency check. OTA tests cover collection supersession/disposal and its adapter.
Build and release CI explicitly run the suite; build CI covers Windows, Linux and macOS.
