using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>Trailing debounce that coalesces rapid foreground changes (#167). Injected so the policy is
/// testable synchronously (the fake fires on command).</summary>
public interface IDebounceScheduler
{
    /// <summary>Schedule <paramref name="action"/> after the debounce window, replacing any pending one.</summary>
    void Schedule(Action action);
    void Cancel();
}

/// <summary>Applies a per-app target to the daemon (#167): a named snapshot (ephemerally) or the user's
/// default. Kept behind an interface so the switch policy is testable without daemon/disk.</summary>
public interface IPerAppApplier
{
    Task ApplyDefaultAsync();
    /// <summary>Apply the named snapshot ephemerally. Returns false if it's missing/failed to load.</summary>
    Task<bool> ApplySnapshotAsync(string snapshotName);
}

/// <summary>
/// The brain of per-app profile switching (#167): watches the foreground app, resolves it to a target
/// profile via <see cref="PerAppProfileStore"/>, and applies it through <see cref="IPerAppApplier"/> —
/// applying the switch policy (debounce, dedupe-by-target, default fallback, ignore-own-window,
/// dangling-profile→default). Headless and fully unit-testable; the real watcher / applier / debouncer
/// are injected. UI-thread only in production.
///
/// Note: switches apply immediately, without waiting for pen-up. That was only needed while a switch
/// could move the tablet mid-stroke; per-app switching now preserves the current monitor mapping
/// (<see cref="DisplayMappingApplier.PreserveAreaMapping"/>), so a switch no longer jumps the cursor.
/// </summary>
public sealed partial class PerAppSwitcher : ObservableObject, IDisposable
{
    private readonly IForegroundAppWatcher _watcher;
    private readonly PerAppProfileStore _store;
    private readonly IPerAppApplier _applier;
    private readonly IDebounceScheduler _debounce;
    private readonly string _ownExeName;

    private bool _running;
    private bool _hasApplied;
    private string? _current;        // applied target: profile name, or null = user default

    /// <summary>The active per-app profile (null = user default / none) — bound by the shell for the
    /// "App profile" cue, and mirrored by the <see cref="ActiveProfileChanged"/> event for the page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveProfile))]
    private string? _activeProfile;

    public bool HasActiveProfile => !string.IsNullOrEmpty(ActiveProfile);

    /// <summary>The active per-app profile (null = user default / none) — for the UI status + cue.</summary>
    public event Action<string?>? ActiveProfileChanged;
    /// <summary>A resolved mapping pointed at a profile that no longer loads; we fell back to default.</summary>
    public event Action<string>? DanglingSnapshot;

    public PerAppSwitcher(IForegroundAppWatcher watcher, PerAppProfileStore store,
        IPerAppApplier applier, IDebounceScheduler debounce, string ownExeName)
    {
        _watcher = watcher;
        _store = store;
        _applier = applier;
        _debounce = debounce;
        _ownExeName = ownExeName;
        _watcher.Changed += OnForegroundChanged;
    }

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _watcher.Start();
    }

    /// <summary>Stop watching and restore the user's default (so no per-app profile lingers).</summary>
    public async Task StopAsync()
    {
        if (!_running) return;
        _running = false;
        _watcher.Stop();
        _debounce.Cancel();
        if (_hasApplied && _current != null)
            await _applier.ApplyDefaultAsync();
        _hasApplied = false;
        _current = null;
        ActiveProfile = null;
        ActiveProfileChanged?.Invoke(null);
    }

    private void OnForegroundChanged(AppIdentity app)
    {
        if (!_running) return;
        // Focusing our own window must not switch (editing/testing shouldn't thrash the profile).
        if (string.Equals(app.ExeName, _ownExeName, StringComparison.OrdinalIgnoreCase)) return;

        var target = _store.Resolve(app);
        if (_hasApplied && TargetEquals(target, _current)) return; // dedupe by TARGET, not app
        _debounce.Schedule(() => OnDebounced(target));
    }

    private void OnDebounced(string? target)
    {
        if (!_running) return;
        _ = ApplyAsync(target);
    }

    private async Task ApplyAsync(string? target)
    {
        _hasApplied = true;
        _current = target;

        if (target == null)
        {
            await _applier.ApplyDefaultAsync();
        }
        else if (!await _applier.ApplySnapshotAsync(target))
        {
            // Mapping references a deleted/renamed profile → fall back to default, warn (don't fail).
            _current = null;
            DanglingSnapshot?.Invoke(target);
            await _applier.ApplyDefaultAsync();
            ActiveProfile = null;
            ActiveProfileChanged?.Invoke(null);
            return;
        }

        ActiveProfile = target;
        ActiveProfileChanged?.Invoke(target);
    }

    private static bool TargetEquals(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    public void Dispose()
    {
        _watcher.Changed -= OnForegroundChanged;
        _watcher.Dispose();
    }
}
