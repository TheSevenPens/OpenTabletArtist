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
    /// <summary>Apply settings that came from a file (a saved preset) as the daemon would load them: its
    /// button slots and wheel steps matched to each detected tablet, as <see cref="RestoreSavedAsync"/> does.
    /// Otherwise the daemon's normal matching reads back as a rejection. Never saves.</summary>
    Task<SettingsApplyOutcome> ApplyLoadedAsync(Settings source);
    /// <summary><paramref name="source"/> as <see cref="ApplyLoadedAsync"/> would send it, without sending it:
    /// matched to the detected tablets and stamped with the live revision. Null when that cannot be told
    /// (not connected, or the tablets could not be read).</summary>
    Task<Settings?> AsLoadedAsync(Settings source);
    /// <summary>Apply the saved file (or its last good backup). Never writes the saved file.</summary>
    Task<SettingsRestoreOutcome> RestoreSavedAsync();
}
