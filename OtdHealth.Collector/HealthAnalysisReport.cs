using System.Text.Json.Serialization;

namespace OtdHealth.Collector;

[JsonConverter(typeof(JsonStringEnumConverter<ProbeId>))]
public enum ProbeId
{
    Daemon, DaemonVersion, Profiles, Displays, ConfigurationOverrides, WindowsInk, VMulti,
    DriverConflicts, ProcessElevation, LinuxUdev, LinuxModules, LinuxHidAccess, MacOSAccess,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProbeOutcome>))]
public enum ProbeOutcome { Completed, NotApplicable, Unavailable, Unsupported, Failed, Cancelled }

public sealed record ProbeFailure(string Code, string Message, string? ExceptionType = null);
public sealed record ProbeResult(ProbeId Id, bool Requested, bool Applicable, ProbeOutcome Outcome,
    ProbeFailure? Failure = null);

/// <summary>Completeness describes requested coverage, never the absence of findings.
/// Findings are evaluated from the snapshot, including when reading a saved report.</summary>
public sealed record HealthAnalysisReport(HealthSnapshot Snapshot, IReadOnlyList<ProbeId> RequestedCoverage,
    IReadOnlyList<ProbeResult> Probes)
{
    public IReadOnlyList<HealthFinding> Findings => HealthEvaluator.Evaluate(Snapshot);
    public bool IsComplete => RequestedCoverage.Count > 0
        && RequestedCoverage.Distinct().Count() == RequestedCoverage.Count
        && (!RequestedCoverage.Any(HealthCollector.IsDaemonDependent) || RequestedCoverage.Contains(ProbeId.Daemon))
        && (!RequestedCoverage.Any(id => id is ProbeId.Displays or ProbeId.ConfigurationOverrides or ProbeId.MacOSAccess)
            || RequestedCoverage.Contains(ProbeId.Profiles))
        && (!RequestedCoverage.Contains(ProbeId.Daemon) || Snapshot.DaemonConnected)
        && RequestedCoverage.All(id => Enum.IsDefined(id) && Probes.Count(p => p.Id == id) == 1
            && Probes.Any(p => p.Id == id && p.Requested &&
                (p.Applicable ? p.Outcome == ProbeOutcome.Completed : p.Outcome == ProbeOutcome.NotApplicable && CanBeInapplicable(id))));
    private bool CanBeInapplicable(ProbeId id) => id switch
    {
        ProbeId.WindowsInk or ProbeId.VMulti or ProbeId.ProcessElevation => Snapshot.Platform != HealthPlatform.Windows,
        ProbeId.LinuxUdev or ProbeId.LinuxModules or ProbeId.LinuxHidAccess => Snapshot.Platform != HealthPlatform.Linux || Snapshot.Tablets.Any(t => t.Detected),
        ProbeId.MacOSAccess => Snapshot.Platform != HealthPlatform.MacOS || Snapshot.Tablets.Any(t => t.Detected),
        ProbeId.Displays or ProbeId.ConfigurationOverrides => !Snapshot.Tablets.Any(t => t.Detected),
        _ => false,
    };
}

public sealed record HealthCollectionOptions
{
    public IReadOnlyList<ProbeId> Coverage { get; init; } = Enum.GetValues<ProbeId>();
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Consumer choices, separate from observations. No OTA preferences are read here.</summary>
public sealed record HealthPolicy
{
    public string ExpectedOtdVersion { get; init; } = "";
    public bool ForeignDaemon { get; init; }
    public bool DaemonIsManagedButNotSelected { get; init; }
    public bool DaemonSourceUnknown { get; init; }
    public string? RequiredDynamicsFilter { get; init; }
    public IReadOnlySet<string> WinInkOptedOutProfileIds { get; init; } = new HashSet<string>();
}

/// <summary>A known limitation or unavailable prerequisite, as distinct from an unexpected exception.</summary>
public sealed class ProbeUnavailableException(string message, bool unsupported = false) : Exception(message)
{
    public bool Unsupported { get; } = unsupported;
}
