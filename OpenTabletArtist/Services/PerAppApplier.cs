using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenTabletDriver.Desktop;
using OtdInterop;

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
    private readonly IPresetStore _store;
    private readonly Func<string?> _presetDirectory;

    public PerAppApplier(ISettingsCoordinator settings, IPresetStore store, Func<string?> presetDirectory)
    {
        _settings = settings;
        _store = store;
        _presetDirectory = presetDirectory;
    }

    public async Task<bool> ApplyDefaultAsync()
    {
        var settings = _settings.CurrentSettings;
        if (settings == null) return false;

        // Background automation must not throw at its caller (a disconnected daemon is not exceptional
        // here), but a silent failure means per-app switching quietly stopped working — report it both
        // to the log and to the switcher, which needs to know not to record a switch that didn't happen.
        try
        {
            // Anything short of live means the override is still on the tablet (#766) — the switcher
            // must not record a return to the default that did not happen. The outcome says which kind
            // of short: no transport, a superseded session, a failure. That reaches the log below at its
            // source; this contract only needs to know whether the tablet is back.
            var outcome = await _settings.ClearEphemeralOverrideAsync();
            if (!outcome.IsLive)
                AppLog.Warn($"Per-app switch to the default profile did not take effect ({outcome.Status}).");
            return outcome.IsLive;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Per-app switch to the default profile failed to apply.", ex);
            return false;
        }
    }

    public async Task<PerAppApplyResult> ApplySnapshotAsync(string snapshotName)
    {
        var dir = _presetDirectory();
        if (string.IsNullOrEmpty(dir)) return PerAppApplyResult.SnapshotMissing;
        var path = Path.Combine(dir, snapshotName + ".json");
        if (!_store.TryLoad(path, out var settings) || settings == null)
            return PerAppApplyResult.SnapshotMissing;   // dangling → caller falls back to the default

        // Keep the tablet on the monitor the user currently has it on rather than the one frozen into the
        // snapshot — moving an app between displays shouldn't yank the tablet to a stale monitor (#167).
        // The monitor is governed by the live settings (tablet page / cycle-monitor hotkey).
        if (_settings.CurrentSettings is { } current)
            OpenTabletArtist.Domain.DisplayMappingApplier.PreserveAreaMapping(settings, current);

        // The snapshot exists, so a daemon failure here is NOT "missing" — reporting it as such would
        // send the switcher to the default and raise a dangling-profile warning about a profile that is
        // perfectly fine. It is its own outcome, and the switcher commits nothing for it (#737).
        try
        {
            var outcome = await _settings.ApplyEphemeralAsync(settings);
            if (outcome.IsLive) return PerAppApplyResult.Applied;
            // Nothing reached the daemon (#766). The reason is worth logging even though this result
            // type cannot carry it — a per-app switch that silently does nothing is hard to attribute.
            AppLog.Warn($"Per-app snapshot did not reach the daemon ({outcome.Status}).");
            return PerAppApplyResult.ApplyFailed;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Per-app switch to snapshot \"{snapshotName}\" failed to apply.", ex);
            return PerAppApplyResult.ApplyFailed;
        }
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
