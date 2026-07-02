using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenTabletArtist.Services;

/// <summary>
/// The single place that switches the active profile by applying a saved snapshot as a temporary
/// <em>live-only</em> override (not persisted to disk), and restores the saved default (#320). Both the
/// keyboard hotkeys (Part 1) and the future per-app auto-switch (#167) go through here rather than
/// touching <see cref="ISettingsCoordinator"/> directly, so the "active override" state, the restore
/// path, and the switch notification live in one spot.
/// </summary>
public sealed partial class ProfileSwitchService : ObservableObject
{
    private readonly ISettingsCoordinator _settings;
    private readonly ISettingsFileStore _store;
    private readonly Func<string?> _presetDirectory;

    public ProfileSwitchService(ISettingsCoordinator settings, ISettingsFileStore store,
        Func<string?> presetDirectory)
    {
        _settings = settings;
        _store = store;
        _presetDirectory = presetDirectory;
    }

    /// <summary>The snapshot currently applied as a live-only override, or null when the saved default is
    /// active. Drives the "Profile override: …" cue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverride))]
    private string? _activeSnapshot;

    public bool HasOverride => !string.IsNullOrEmpty(ActiveSnapshot);

    /// <summary>Raised after a successful switch or restore with the new active snapshot name (null =
    /// restored to default). Consumers show a transient toast.</summary>
    public event Action<string?>? Switched;

    /// <summary>Apply the named snapshot as a live-only override. Returns false if the snapshot no longer
    /// exists (e.g. a hotkey mapped to a deleted snapshot).</summary>
    public async Task<bool> SwitchToAsync(string snapshotName)
    {
        var path = SnapshotPath(snapshotName);
        if (path == null || !_store.TryLoad(path, out var settings) || settings == null)
            return false;

        await _settings.ApplyLiveOnlyAsync(settings);
        ActiveSnapshot = snapshotName;
        Switched?.Invoke(snapshotName);
        return true;
    }

    /// <summary>Revert to the saved on-disk default, clearing any override. No-op when not overridden.</summary>
    public async Task RestoreDefaultAsync()
    {
        if (!HasOverride) return;
        await _settings.RestoreDefaultAsync();
        ActiveSnapshot = null;
        Switched?.Invoke(null);
    }

    private string? SnapshotPath(string name)
    {
        var dir = _presetDirectory();
        return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, name + ".json");
    }
}
