namespace OtdInterop;

/// <summary>Result of observing or accepting live driver settings.</summary>
public enum SettingsReloadStatus
{
    /// <summary>The caller explicitly accepted current live settings.</summary>
    Adopted,
    /// <summary>The driver still matches the current document.</summary>
    Unchanged,
    /// <summary>Outside changes require explicit reload.</summary>
    Paused,
    /// <summary>No usable connection.</summary>
    Disconnected,
    /// <summary>The read failed.</summary>
    Failed
}

/// <summary>Observation does not apply or save.</summary>
/// <param name="Status">The result.</param>
/// <param name="Adopted">Detached document after successful explicit reload.</param>
/// <param name="Error">Diagnostic detail when available.</param>
public readonly record struct SettingsReloadOutcome(
    SettingsReloadStatus Status, PreparedSettings? Adopted = null, Exception? Error = null)
{
    /// <summary>The editable document was replaced.</summary>
    public bool ChangedTheBaseline => Status == SettingsReloadStatus.Adopted;
}
