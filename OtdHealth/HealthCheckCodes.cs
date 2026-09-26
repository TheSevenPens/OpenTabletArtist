namespace OtdHealth;

/// <summary>Stable machine-readable finding codes. Consumers must not infer a code from display text.</summary>
public static class HealthCheckCodes
{
    /// <summary>The winink.notInstalled diagnostic.</summary>
    public const string WinInkNotInstalled = "winink.notInstalled";
    /// <summary>The winink.versionMismatch diagnostic.</summary>
    public const string WinInkVersionMismatch = "winink.versionMismatch";
    /// <summary>The vmulti.notInstalled diagnostic.</summary>
    public const string VMultiNotInstalled = "vmulti.notInstalled";
    /// <summary>The driver.conflict diagnostic.</summary>
    public const string DriverConflict = "driver.conflict";
    /// <summary>The process.elevated diagnostic.</summary>
    public const string ProcessElevated = "process.elevated";
    /// <summary>The otd.permissionsMissing diagnostic.</summary>
    public const string DaemonPermissionsMissing = "otd.permissionsMissing";
    /// <summary>The daemon.foreign diagnostic.</summary>
    public const string ForeignDaemon = "daemon.foreign";
    /// <summary>The daemon.sourceUnknown diagnostic.</summary>
    public const string DaemonSourceUnknown = "daemon.sourceUnknown";
    /// <summary>The daemon.versionMismatch diagnostic.</summary>
    public const string DaemonVersionMismatch = "daemon.versionMismatch";
    /// <summary>The tablet.notWinInk diagnostic.</summary>
    public const string TabletNotWinInk = "tablet.notWinInk";
    /// <summary>The tablet.winInkOff diagnostic.</summary>
    public const string TabletWinInkOff = "tablet.winInkOff";
    /// <summary>The tablet.penTipDisabled diagnostic.</summary>
    public const string TabletPenTipDisabled = "tablet.penTipDisabled";
    /// <summary>The tablet.pressureDisabled diagnostic.</summary>
    public const string TabletPressureDisabled = "tablet.pressureDisabled";
    /// <summary>The tablet.tiltDisabled diagnostic.</summary>
    public const string TabletTiltDisabled = "tablet.tiltDisabled";
    /// <summary>The tablet.dynamicsOff diagnostic.</summary>
    public const string TabletDynamicsOff = "tablet.dynamicsOff";
    /// <summary>The tablet.configOverride diagnostic.</summary>
    public const string TabletConfigOverride = "tablet.configOverride";
    /// <summary>The tablet.mappingOffScreen diagnostic.</summary>
    public const string TabletMappingOffScreen = "tablet.mappingOffScreen";
    /// <summary>The tablet.mappingCustom diagnostic.</summary>
    public const string TabletMappingCustom = "tablet.mappingCustom";
    /// <summary>The tablet.mappingRotation diagnostic.</summary>
    public const string TabletMappingRotation = "tablet.mappingRotation";
    /// <summary>The linux.udevRules diagnostic.</summary>
    public const string LinuxUdevRulesMissing = "linux.udevRules";
    /// <summary>The linux.hidAccessBlocked diagnostic.</summary>
    public const string LinuxHidAccessBlocked = "linux.hidAccessBlocked";
    /// <summary>The linux.hidAccessPending diagnostic.</summary>
    public const string LinuxHidAccessPending = "linux.hidAccessPending";
    /// <summary>The linux.conflictingModules diagnostic.</summary>
    public const string LinuxConflictingModules = "linux.conflictingModules";
}
