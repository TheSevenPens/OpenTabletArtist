using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenTabletDriver.Desktop;

namespace OpenTabletArtist.Services;

/// <summary>
/// Real per-app applier (#167): applies snapshots to the daemon <b>ephemerally</b> (via
/// <see cref="ISettingsCoordinator.ApplyEphemeralAsync"/>, so the editor stays on the user's default) and
/// restores that default by re-applying <see cref="ISettingsCoordinator.CurrentSettings"/> — which is
/// never mutated by ephemeral applies, so it remains the user's default. Snapshots load from the same
/// preset directory the Saved Settings page uses.
/// </summary>
public sealed class PerAppApplier : IPerAppApplier
{
    private readonly ISettingsCoordinator _settings;
    private readonly ISettingsFileStore _store;
    private readonly Func<string?> _presetDirectory;

    public PerAppApplier(ISettingsCoordinator settings, ISettingsFileStore store, Func<string?> presetDirectory)
    {
        _settings = settings;
        _store = store;
        _presetDirectory = presetDirectory;
    }

    public async Task ApplyDefaultAsync()
    {
        var settings = _settings.CurrentSettings;
        if (settings == null) return;
        // Swallow a disconnected-daemon failure: the switch is best-effort background automation.
        try { await _settings.ApplyEphemeralAsync(settings); } catch { }
    }

    public async Task<bool> ApplySnapshotAsync(string snapshotName)
    {
        var dir = _presetDirectory();
        if (string.IsNullOrEmpty(dir)) return false;
        var path = Path.Combine(dir, snapshotName + ".json");
        if (!_store.TryLoad(path, out var settings) || settings == null) return false; // dangling → caller falls back

        // Keep the tablet on the monitor the user currently has it on rather than the one frozen into the
        // snapshot — moving an app between displays shouldn't yank the tablet to a stale monitor (#167).
        // The monitor is governed by the live settings (tablet page / cycle-monitor hotkey).
        if (_settings.CurrentSettings is { } current)
            OpenTabletArtist.Domain.DisplayMappingApplier.PreserveAreaMapping(settings, current);

        // The snapshot exists; treat a daemon hiccup as best-effort (don't misreport it as "missing").
        try { await _settings.ApplyEphemeralAsync(settings); } catch { }
        return true;
    }
}

/// <summary>Real trailing debounce on the UI thread (#167): coalesces rapid foreground changes into one
/// apply. A single-shot <see cref="DispatcherTimer"/> restarted on each <see cref="Schedule"/>.</summary>
public sealed class DispatcherDebounceScheduler : IDebounceScheduler
{
    private readonly DispatcherTimer _timer;
    private Action? _action;

    public DispatcherDebounceScheduler(TimeSpan interval)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var action = _action;
            _action = null;
            action?.Invoke();
        };
    }

    public void Schedule(Action action)
    {
        _action = action;
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _action = null;
    }
}
