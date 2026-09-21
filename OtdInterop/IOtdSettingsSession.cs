using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// Settings for one connection. Operations are serialized; inputs and results are detached.
/// A disconnected instance is terminal: obtain the new instance after OtdSession reconnects.
/// </summary>
public interface IOtdSettingsSession
{
    /// <summary>A detached copy of the last confirmed live settings, or null before loading.</summary>
    PreparedSettings? GetCurrent();
    /// <summary>Live settings differ from the last observed saved file, or that file is unreadable.</summary>
    bool HasUnsavedChanges { get; }
    /// <summary>Outside edits or a failed check require explicit reload before more writes.</summary>
    bool IsPaused { get; }
    /// <summary>Check for outside edits, apply live, then verify by reading back. Never saves.</summary>
    Task<SettingsApplyOutcome> ApplyAsync(Settings requested);
    /// <summary>Persist confirmed live settings after checking for outside changes. Repairs invalid null areas live if needed; never retries automatically.</summary>
    Task<SettingsSaveOutcome> SaveAsync();
    /// <summary>Observe outside edits without replacing a paused workspace.</summary>
    Task<SettingsReloadOutcome> RefreshAsync();
    /// <summary>Explicitly accept live settings and reread the saved baseline. Does not apply or save.</summary>
    Task<SettingsReloadOutcome> ReloadAsync();
    /// <summary>Apply the saved file (or its last good backup). Never writes the saved file.</summary>
    Task<SettingsRestoreOutcome> RestoreSavedAsync();
}
