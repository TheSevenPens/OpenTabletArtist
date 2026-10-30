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

    /// <summary>Whether the host holds input typed and not yet submitted, which a load would throw away.
    /// Unset means none.</summary>
    public Func<bool>? HasPendingInput { get; set; }
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
            // The preset as saved stays as it is; today's profiles fill its gaps, afresh each time they are needed.
            // Loading what the workspace already holds replaces nothing of the artist's, so the question about
            // unsaved changes (which are the preset itself) is skipped, unless input is waiting to be submitted.
            // Only the question: the apply still happens, because it reads the daemon first and reports a
            // pause, a pending apply or an outside change, none of which the local document can know.
            // The stamp is taken before the comparison and checked in the same step as the decision, with no wait
            // between that check and the apply: an edit submitted while the comparison waited for the daemon
            // makes its answer about a document that is gone, however the workspace itself reports it (#1060).
            var stamp = _settings.Stamp;
            var nothingToLose = !await _settings.WouldChangeLoadedAsync(
                    PresetCompletion.Complete(settings, _settings.CurrentSettings))
                && _settings.Stamp == stamp
                && HasPendingInput?.Invoke() != true;
            if (!nothingToLose && BeforeReplaceAsync is { } confirm && !await confirm(false)) return false;
            var outcome = await _settings.ApplyLoadedSettingsAsync(
                PresetCompletion.Complete(settings, _settings.CurrentSettings));
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
