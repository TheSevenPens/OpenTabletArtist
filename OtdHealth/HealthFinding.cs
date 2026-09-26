using System.Text.Json.Serialization;

namespace OtdHealth;

/// <summary>Increasing diagnostic severity. Information describes context, not a failure.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthSeverity>))]
public enum HealthSeverity
{
    /// <summary>Context about a deliberate choice or a pending transition.</summary>
    Information,
    /// <summary>Works, but is not the recommended configuration.</summary>
    Recommendation,
    /// <summary>A feature will not behave as expected.</summary>
    Misconfigured,
    /// <summary>A missing prerequisite prevents core functionality.</summary>
    Broken,
}

/// <summary>Additional observed values for daemon and Linux findings; unused fields are null.</summary>
public sealed record HealthEvidence
{
    /// <summary>Observed daemon version, for a version mismatch.</summary>
    public string? ActualVersion { get; init; }
    /// <summary>The release the consumer expected.</summary>
    public string? ExpectedVersion { get; init; }
    /// <summary>Whether a foreign daemon is another consumer-managed copy.</summary>
    public bool? ManagedButNotSelected { get; init; }
    /// <summary>Whether a systemd user manager makes a reboot preferable to a re-login.</summary>
    public bool? UserManagerRunning { get; init; }
    /// <summary>Whether loaded conflicting modules can return at the next boot.</summary>
    public bool? ModulesNotBlacklisted { get; init; }
    /// <summary>Names of the loaded conflicting kernel modules, copied from the input.</summary>
    public IReadOnlyList<string>? Modules { get; init; }
}

/// <summary>A diagnostic fact, without text, navigation, grouping, or executable remediation.</summary>
/// <param name="Code">A stable value from <see cref="HealthCheckCodes"/>.</param>
/// <param name="Severity">Impact of the finding.</param>
/// <param name="TabletName">Subject of a per-tablet finding; null for process/daemon/system findings.</param>
/// <param name="Evidence">Additional observations needed to explain the finding.</param>
/// <param name="TabletId">The effective snapshot subject identity, separate from its display name.
/// Null for findings without a tablet subject. Evaluator-produced tablet findings always populate it.</param>
public sealed record HealthFinding(
    string Code, HealthSeverity Severity, string? TabletName = null, HealthEvidence? Evidence = null,
    string? TabletId = null)
{
    /// <summary>Finding key formed from Code and subject identity. Stability across evaluations depends
    /// on stable caller-supplied identities. A manually constructed finding may fall back to TabletName.</summary>
    public string Id => (TabletId ?? TabletName) is { } subject ? $"{Code}:{subject}" : Code;
}
