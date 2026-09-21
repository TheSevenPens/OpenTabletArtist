namespace OtdInterop;

/// <summary>Result of checking and applying live settings.</summary>
public enum SettingsApplyStatus
{
    /// <summary>The requested values were confirmed by readback.</summary>
    AppliedLive,
    /// <summary>The requested values already match the driver.</summary>
    NoChange,
    /// <summary>Application failed, or the driver changed or rejected values.</summary>
    ApplyFailed,
    /// <summary>This connection is no longer usable.</summary>
    Disconnected,
    /// <summary>Outside edits must be reviewed with Reload.</summary>
    ChangedElsewhere,
    /// <summary>The preflight read failed. No write was sent.</summary>
    CouldNotCheck,
}

/// <summary>An apply never writes the settings file. Prepared contains confirmed daemon state.</summary>
/// <param name="Status">The result.</param>
/// <param name="Error">Diagnostic detail when available.</param>
/// <param name="Prepared">Detached confirmed values on success.</param>
public readonly record struct SettingsApplyOutcome(
    SettingsApplyStatus Status, Exception? Error = null, PreparedSettings? Prepared = null)
{
    /// <summary>Confirmed live application.</summary>
    public static readonly SettingsApplyOutcome Live = new(SettingsApplyStatus.AppliedLive);
    /// <summary>Already applied.</summary>
    public static readonly SettingsApplyOutcome NoChange = new(SettingsApplyStatus.NoChange);
    /// <summary>No usable connection.</summary>
    public static readonly SettingsApplyOutcome Disconnected = new(SettingsApplyStatus.Disconnected);
    /// <summary>Paused for outside changes.</summary>
    public static readonly SettingsApplyOutcome ChangedElsewhere = new(SettingsApplyStatus.ChangedElsewhere);
    /// <summary>Paused after a failed preflight read.</summary>
    public static readonly SettingsApplyOutcome CouldNotCheck = new(SettingsApplyStatus.CouldNotCheck);
    /// <summary>Failure with optional diagnostic detail.</summary>
    public static SettingsApplyOutcome Failed(Exception? error) => new(SettingsApplyStatus.ApplyFailed, error);
    /// <summary>The requested values are confirmed live.</summary>
    public bool IsLive => Status is SettingsApplyStatus.AppliedLive or SettingsApplyStatus.NoChange;
    /// <summary>This operation changed confirmed live values.</summary>
    public bool ChangedTheDaemon => Status == SettingsApplyStatus.AppliedLive;
}

/// <summary>Result of explicitly saving the current driver settings.</summary>
public enum SettingsSaveStatus
{
    /// <summary>The file was written and reread successfully.</summary>
    Saved,
    /// <summary>Persistence could not be confirmed.</summary>
    Failed,
    /// <summary>Reload is needed before saving.</summary>
    Paused,
    /// <summary>No usable connection.</summary>
    Disconnected
}

/// <summary>The persistence result, separate from live application.</summary>
/// <param name="Status">The result.</param>
/// <param name="Error">Diagnostic detail when available.</param>
public readonly record struct SettingsSaveOutcome(SettingsSaveStatus Status, Exception? Error = null)
{
    /// <summary>Persistence was confirmed.</summary>
    public bool IsSaved => Status == SettingsSaveStatus.Saved;
}

/// <summary>Result of applying the saved file.</summary>
public enum SettingsRestoreStatus
{
    /// <summary>Saved settings are confirmed live.</summary>
    Restored,
    /// <summary>Neither the file nor its backup could be read.</summary>
    SourceUnavailable,
    /// <summary>The saved values could not be applied.</summary>
    ApplyFailed,
    /// <summary>No usable connection.</summary>
    Disconnected
}

/// <summary>A restore changes live settings without writing the saved file.</summary>
/// <param name="Status">The result.</param>
/// <param name="Error">Diagnostic detail when available.</param>
/// <param name="Prepared">Detached confirmed values on success.</param>
public readonly record struct SettingsRestoreOutcome(
    SettingsRestoreStatus Status, Exception? Error = null, PreparedSettings? Prepared = null)
{
    /// <summary>Confirmed restoration.</summary>
    public static readonly SettingsRestoreOutcome Restored = new(SettingsRestoreStatus.Restored);
    /// <summary>Unreadable saved settings.</summary>
    public static readonly SettingsRestoreOutcome SourceUnavailable = new(SettingsRestoreStatus.SourceUnavailable);
    /// <summary>No usable connection.</summary>
    public static readonly SettingsRestoreOutcome Disconnected = new(SettingsRestoreStatus.Disconnected);
    /// <summary>Failure with optional diagnostic detail.</summary>
    public static SettingsRestoreOutcome Failed(Exception? error) => new(SettingsRestoreStatus.ApplyFailed, error);
    /// <summary>Saved settings are confirmed live.</summary>
    public bool IsRestored => Status == SettingsRestoreStatus.Restored;
}
