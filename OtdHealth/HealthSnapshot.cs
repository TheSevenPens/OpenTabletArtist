using System.Text.Json.Serialization;

namespace OtdHealth;

/// <summary>The platform being analyzed, independent of the evaluator's host OS.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthPlatform>))]
public enum HealthPlatform
{
    /// <summary>No platform-specific checks are enabled.</summary>
    Unspecified,
    /// <summary>Windows Ink and VMulti checks apply.</summary>
    Windows,
    /// <summary>The supplied Input Monitoring observation can apply.</summary>
    MacOS,
    /// <summary>udev, HID permissions, and kernel module checks apply.</summary>
    Linux,
}

/// <summary>Classification of an absolute mapping against the observed display layout.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DisplayMappingStatus>))]
public enum DisplayMappingStatus
{
    /// <summary>No mapping could be assessed.</summary>
    None,
    /// <summary>Exactly one whole display.</summary>
    Clean,
    /// <summary>On-screen, but a subregion or multiple displays.</summary>
    Custom,
    /// <summary>Some of the mapped area falls outside every display.</summary>
    OffScreen,
}

/// <summary>The collector's Linux HID access observation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HidAccessStatus>))]
public enum HidAccessStatus
{
    /// <summary>No access problem was reported; this is not proof that a probe ran.</summary>
    NoProblemReported,
    /// <summary>Devices exist but cannot be opened, and the required group is not granted.</summary>
    Blocked,
    /// <summary>The group is granted on disk but is not active in the current session.</summary>
    PendingRestart,
}

/// <summary>Observed tablet facts and caller-supplied policy. Checks only apply to detected tablets.</summary>
/// <param name="Name">Display name; may repeat when distinct Id values are supplied.</param>
/// <param name="Detected">Whether the daemon detected this tablet.</param>
/// <param name="OutputModeIsWinInk">Whether its output mode uses Windows Ink.</param>
/// <param name="Mapping">Mapping classification, or None if not assessed.</param>
/// <param name="NonCardinalRotation">The observed rotation is not within tolerance of a multiple of 90 degrees.</param>
/// <param name="DynamicsWarningRequired">True when the consumer's policy requires a warning about a
/// disabled dynamics filter. This is a policy decision, not an observation of filter presence. False
/// does not certify that a filter is installed, active, or even required by the consumer.</param>
/// <param name="ConfigIsOverride">A user configuration shadows a built-in configuration.</param>
/// <param name="WinInkOptedOut">The caller knows that Windows Ink was deliberately disabled.</param>
/// <param name="PenTipDisabled">No pen tip binding is configured.</param>
/// <param name="PressureDisabled">Pressure delivery is disabled.</param>
/// <param name="TiltDisabled">Tilt delivery is disabled.</param>
/// <param name="Id">Optional consumer-assigned identity, independent of the display name. When omitted,
/// Name is the identity. Effective identities must be nonblank and ordinally unique within a snapshot,
/// including undetected tablets. Keep IDs stable across snapshots when correlating findings.</param>
public sealed record TabletHealthSnapshot(
    string Name, bool Detected, bool OutputModeIsWinInk,
    DisplayMappingStatus Mapping = DisplayMappingStatus.None,
    bool NonCardinalRotation = false,
    bool DynamicsWarningRequired = false,
    bool ConfigIsOverride = false,
    bool WinInkOptedOut = false,
    bool PenTipDisabled = false,
    bool PressureDisabled = false,
    bool TiltDisabled = false,
    string? Id = null);

/// <summary>
/// Facts supplied by a consumer. Evaluation does not collect evidence or certify completeness.
/// Defaults mean no problem was reported, except nullable installation facts where null means unknown.
/// Callers must keep collections stable during evaluation. No machine state is read by the evaluator.
/// </summary>
public sealed record HealthSnapshot
{
    /// <summary>The platform being assessed, not necessarily the current process's platform.</summary>
    public HealthPlatform Platform { get; init; }
    /// <summary>Whether the daemon currently answers.</summary>
    public bool DaemonConnected { get; init; }
    /// <summary>The connected daemon is external according to the consumer's ownership policy.</summary>
    public bool ForeignDaemon { get; init; }
    /// <summary>The external daemon is another managed copy rather than an independent installation.</summary>
    public bool DaemonIsManagedButNotSelected { get; init; }
    /// <summary>The connected daemon's executable location could not be read.</summary>
    public bool DaemonSourceUnknown { get; init; }
    /// <summary>Observed daemon version, or an empty string if unavailable.</summary>
    public string DaemonVersion { get; init; } = "";
    /// <summary>Release the consumer supports, or empty to omit version comparison.</summary>
    public string ExpectedOtdVersion { get; init; } = "";
    /// <summary>
    /// Caller inferred a macOS Input Monitoring problem: a supported device is visible but not detected.
    /// Only evaluated for MacOS. This is a supplied inference, not an independent permission check.
    /// </summary>
    public bool DaemonCannotOpenTablet { get; init; }
    /// <summary>Windows Ink installation observation. Null means it has not been established.</summary>
    public bool? WinInkInstalled { get; init; }
    /// <summary>The installed Windows Ink plugin does not support the expected driver version.</summary>
    public bool WinInkVersionMismatch { get; init; }
    /// <summary>VMulti installation observation. Null means it has not been established.</summary>
    public bool? VMultiInstalled { get; init; }
    /// <summary>A conflicting manufacturer driver was reported.</summary>
    public bool HasDriverConflict { get; init; }
    /// <summary>The conflict blocks tablet detection rather than only affecting behavior.</summary>
    public bool BlockingDriverConflict { get; init; }
    /// <summary>The process being assessed is elevated; the collector chooses that process.</summary>
    public bool RunningElevated { get; init; }
    /// <summary>OTD's udev rules were not found.</summary>
    public bool LinuxUdevRulesMissing { get; init; }
    /// <summary>The collector's HID access observation.</summary>
    public HidAccessStatus LinuxHidAccess { get; init; }
    /// <summary>A systemd user manager retains the session's previous group membership.</summary>
    public bool LinuxUserManagerRunning { get; init; }
    /// <summary>Loaded modules that may claim tablet devices before OTD.</summary>
    public IReadOnlyList<string> LinuxConflictingModulesLoaded { get; init; } = Array.Empty<string>();
    /// <summary>The conflicting modules are not blacklisted for subsequent boots.</summary>
    public bool LinuxConflictingModulesNotBlacklisted { get; init; }
    /// <summary>Tablet facts and per-tablet policy inputs.</summary>
    public IReadOnlyList<TabletHealthSnapshot> Tablets { get; init; } = Array.Empty<TabletHealthSnapshot>();
}
