using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;
using OtdInterop;

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

    /// <summary>
    /// Raised when a switch could not be applied, with the snapshot it was asked for. Reported here
    /// rather than left to the caller because the callers are triggers, not UI: a hotkey press arrives
    /// with no window in front of it, and until this existed a press whose preset had been deleted did
    /// nothing at all — <see cref="ProfileHotkeyManager"/> discarded the <c>false</c> and the user got
    /// silence from a key that used to work.
    /// </summary>
    public event Action<string>? SwitchFailed;

    /// <summary>
    /// Raised when a restore could not be completed, with the reason. The override is still active when
    /// this fires — same rationale as <see cref="SwitchFailed"/>: the trigger is usually a hotkey with no
    /// window in front of it, so silence would leave the artist with a tablet that isn't behaving as the
    /// UI claims (#734).
    /// </summary>
    public event Action<SettingsRestoreStatus>? RestoreFailed;

    /// <summary>Apply the named snapshot as a live-only override. Returns false — and raises
    /// <see cref="SwitchFailed"/> — if the snapshot can't be loaded (deleted, moved, or unreadable), or
    /// if it couldn't be applied because there is no daemon connection (#766).</summary>
    public async Task<bool> SwitchToAsync(string snapshotName)
    {
        var path = SnapshotPath(snapshotName);
        if (path == null || !_store.TryLoad(path, out var settings) || settings == null)
        {
            SwitchFailed?.Invoke(snapshotName);
            return false;
        }

        // Only claim the switch once the daemon has it (#766). This path is reached from a hotkey with
        // no window in front of it, so the toast is the entire feedback — announcing a switch that never
        // left the app leaves the artist believing the tablet changed when it did not.
        if (!await _settings.ApplyLiveOnlyAsync(settings))
        {
            SwitchFailed?.Invoke(snapshotName);
            return false;
        }

        ActiveSnapshot = snapshotName;
        Switched?.Invoke(snapshotName);
        return true;
    }

    /// <summary>
    /// Revert to the saved on-disk default, clearing any override. No-op when not overridden.
    /// Returns false — and raises <see cref="RestoreFailed"/> — when the default could not be reached.
    ///
    /// The override indicator is only cleared on a confirmed restore (#734). It used to clear
    /// unconditionally and announce a restoration, so a missing or unreadable settings file left the
    /// tablet running the override while the UI said it was back on the default.
    /// </summary>
    public async Task<bool> RestoreDefaultAsync()
    {
        if (!HasOverride) return true;

        var outcome = await _settings.RestoreDefaultAsync();
        if (!outcome.IsRestored)
        {
            RestoreFailed?.Invoke(outcome.Status);
            return false;
        }

        ActiveSnapshot = null;
        Switched?.Invoke(null);
        return true;
    }

    private string? SnapshotPath(string name)
    {
        var dir = _presetDirectory();
        return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, name + ".json");
    }
}
