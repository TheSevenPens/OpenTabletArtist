using System.IO;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>Manual preset loading and reverting. Both replace the normal editable workspace.</summary>
public sealed class ProfileSwitchService
{
    private readonly ISettingsCoordinator _settings;
    private readonly IPresetStore _store;
    private readonly Func<string?> _presetDirectory;
    private bool _replacing;

    public ProfileSwitchService(ISettingsCoordinator settings, IPresetStore store, Func<string?> presetDirectory)
    {
        _settings = settings;
        _store = store;
        _presetDirectory = presetDirectory;
    }

    /// <summary>The host resolves unsaved input before replacement; true means restoring the saved file.</summary>
    public Func<bool, Task<bool>>? BeforeReplaceAsync { get; set; }
    public event Action<string?>? Switched;
    public event Action<string>? SwitchFailed;
    public event Action<SettingsRestoreStatus>? RestoreFailed;

    public async Task<bool> SwitchToAsync(string snapshotName)
    {
        if (_replacing) return false;
        _replacing = true;
        try
        {
            var directory = _presetDirectory();
            if (string.IsNullOrEmpty(directory)
                || !_store.TryLoad(Path.Combine(directory, snapshotName + ".json"), out var settings)
                || settings is null)
            {
                SwitchFailed?.Invoke(snapshotName);
                return false;
            }
            if (BeforeReplaceAsync is { } confirm && !await confirm(false)) return false;
            var outcome = await _settings.ApplySettingsAsync(settings);
            if (!outcome.IsLive)
            {
                AppLog.Warn($"Preset \"{snapshotName}\" did not reach the daemon ({outcome.Status}).");
                SwitchFailed?.Invoke(snapshotName);
                return false;
            }
            Switched?.Invoke(snapshotName);
            return true;
        }
        finally { _replacing = false; }
    }

    public async Task<bool> RestoreDefaultAsync()
    {
        if (_replacing) return false;
        _replacing = true;
        try
        {
            if (BeforeReplaceAsync is { } confirm && !await confirm(true)) return false;
            var outcome = await _settings.RestoreDefaultAsync();
            if (!outcome.IsRestored)
            {
                RestoreFailed?.Invoke(outcome.Status);
                return false;
            }
            Switched?.Invoke(null);
            return true;
        }
        finally { _replacing = false; }
    }
}
