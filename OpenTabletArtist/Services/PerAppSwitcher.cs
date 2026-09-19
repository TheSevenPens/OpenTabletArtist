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

/// <summary>
/// What came of applying a per-app target (#737). "Missing" and "failed" used to collapse into one
/// boolean — and worse, a failed daemon call reported success, so the switcher recorded the target as
/// applied and deduplicated every later attempt against it. The tablet then kept the previous app's
/// profile with the UI naming the new one, and focusing that app again did nothing.
/// </summary>
public enum PerAppApplyResult
{
    /// <summary>The daemon took it. Only this may be committed as the active profile.</summary>
    Applied,
    /// <summary>The mapping points at a snapshot that no longer loads. Fall back to the default and say so.</summary>
    SnapshotMissing,
    /// <summary>The snapshot exists but the daemon didn't take it. Nothing is committed, so the next
    /// focus change retries instead of deduplicating against a switch that never happened.</summary>
    ApplyFailed,
}

/// <summary>Applies a per-app target to the daemon (#167): a named snapshot (ephemerally) or the user's
/// default. Kept behind an interface so the switch policy is testable without daemon/disk.</summary>
public interface IPerAppApplier
{
    /// <summary>Return the daemon to the user's saved default, ending any per-app override.
    /// Returns false if the daemon didn't take it.</summary>
    Task<bool> ApplyDefaultAsync();

    /// <summary>Apply the named snapshot ephemerally.</summary>
    Task<PerAppApplyResult> ApplySnapshotAsync(string snapshotName);
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
    private string? _current;        // CONFIRMED applied target: profile name, or null = user default

    // Serializes applies and lets a superseded one bail out rather than publish a stale label (#737).
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private long _generation;
    private Task _lastApply = Task.CompletedTask;

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

    /// <summary>
    /// Stop watching and restore the user's default (so no per-app profile lingers).
    /// </summary>
    /// <returns>
    /// False when a per-app profile was live and the daemon would not take the default back, so the
    /// tablet is <b>still on that profile</b> with nothing watching it any more. Watching has stopped
    /// either way -- this says only whether the override ended with it.
    /// </returns>
    public async Task<bool> StopAsync()
    {
        if (!_running) return true;
        _running = false;
        _watcher.Stop();
        _debounce.Cancel();

        // Wait for an apply that is already running rather than racing it — otherwise it could re-apply
        // a snapshot after the restore below and leave the tablet on it. Anything merely QUEUED is
        // already dealt with: it re-checks _running after acquiring the lock and returns.
        //
        // Deliberately no generation bump here. Superseding the in-flight apply would stop it recording
        // what it just put on the tablet, and the restore below is driven by that record — so the
        // snapshot would stay applied with nothing watching it, which is the case this method exists for.
        bool restored = true;
        await _applyLock.WaitAsync().ConfigureAwait(true);
        try
        {
            // The result is load-bearing (#803). ApplyDefaultAsync returns false when the daemon did not
            // take the default -- and this used to clear _hasApplied, _current and ActiveProfile anyway,
            // which told the rest of the app no per-app profile was live while the tablet was still
            // running one. The two other ApplyDefaultAsync call sites already respect the bool; this one
            // was the exception, and it is the one that runs on shutdown, when a daemon going away first
            // is exactly how the false arises.
            if (_hasApplied && _current != null)
                restored = await _applier.ApplyDefaultAsync();

            // Only forget the override once it is genuinely over. Keeping the record is what a later
            // restore would need, and it stops the UI claiming a clean state that isn't.
            if (restored)
            {
                _hasApplied = false;
                _current = null;
            }
        }
        finally
        {
            _applyLock.Release();
        }

        if (!restored)
        {
            AppLog.Warn($"Stopped per-app switching, but couldn't restore your default: the tablet is " +
                        $"still on “{_current}”.");
            return false;
        }

        ActiveProfile = null;
        ActiveProfileChanged?.Invoke(null);
        return true;
    }

    private void OnForegroundChanged(AppIdentity app)
    {
        if (!_running) return;
        // Focusing our own window must not switch (editing/testing shouldn't thrash the profile).
        if (string.Equals(app.ExeName, _ownExeName, StringComparison.OrdinalIgnoreCase)) return;

        var target = _store.Resolve(app);
        if (_hasApplied && TargetEquals(target, _current))
        {
            // Already on this target — but a DIFFERENT one may be sitting in the debounce, and letting it
            // fire would switch away from what is now in front of the user (#737). Sequence: apply A,
            // focus B (queues B), focus A again before the window elapses. Expected A; the queued B won.
            _debounce.Cancel();
            return;
        }

        _debounce.Schedule(() => OnDebounced(target));
    }

    private void OnDebounced(string? target)
    {
        if (!_running) return;
        // Fire-and-forget by necessity — the debounce callback is synchronous — but serialized and
        // generation-checked inside, so overlapping applies can't interleave or publish out of order.
        // The task is kept so callers can await settling rather than guess at it (see WaitForIdleAsync).
        _lastApply = ApplyAsync(target, Interlocked.Increment(ref _generation));
    }

    /// <summary>
    /// Completes once the most recently requested apply has settled. Applies are started from a
    /// synchronous debounce callback, so there is otherwise no handle to await — and a test that polls
    /// or sleeps instead would be exactly the kind of timing-dependent check this class needs to not have.
    /// Because applies are serialized, awaiting the newest also awaits everything queued behind it.
    /// </summary>
    public Task WaitForIdleAsync() => _lastApply;

    private async Task ApplyAsync(string? target, long generation)
    {
        // One apply at a time. Without this, two switches in flight could finish in either order and the
        // later-finishing older one would publish its label over the newer one's (#737).
        await _applyLock.WaitAsync().ConfigureAwait(true);
        try
        {
            // Superseded while we queued: a newer target is already on its way, so applying this one
            // would be a switch the user never asked for, followed immediately by the right one.
            if (!_running || generation != Interlocked.Read(ref _generation)) return;

            if (target == null)
            {
                if (!await _applier.ApplyDefaultAsync()) return;   // nothing committed; next change retries
                Commit(null, generation);
                return;
            }

            switch (await _applier.ApplySnapshotAsync(target))
            {
                case PerAppApplyResult.Applied:
                    Commit(target, generation);
                    break;

                case PerAppApplyResult.SnapshotMissing:
                    // The mapping points at a deleted/renamed profile. Fall back to the default and say
                    // so — but only claim we're on the default if the fallback actually landed.
                    DanglingSnapshot?.Invoke(target);
                    if (await _applier.ApplyDefaultAsync())
                        Commit(null, generation);
                    break;

                case PerAppApplyResult.ApplyFailed:
                    // Deliberately commit nothing. Recording a failed switch as applied is what made the
                    // dedupe swallow every retry, so the app stayed on the wrong profile indefinitely.
                    AppLog.Warn($"Per-app switch to \"{target}\" didn't apply; leaving the active profile " +
                                "unchanged so the next focus change retries.");
                    break;
            }
        }
        finally
        {
            _applyLock.Release();
        }
    }

    /// <summary>Record and announce the active profile. Only ever called for an apply the daemon
    /// confirmed, and only while this apply is still the newest one.</summary>
    private void Commit(string? target, long generation)
    {
        if (generation != Interlocked.Read(ref _generation)) return;
        _hasApplied = true;
        _current = target;
        ActiveProfile = target;
        ActiveProfileChanged?.Invoke(target);
    }

    private static bool TargetEquals(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    public void Dispose()
    {
        _running = false;
        _debounce.Cancel();
        Interlocked.Increment(ref _generation);   // nothing queued may publish after disposal
        _watcher.Changed -= OnForegroundChanged;
        _watcher.Dispose();
        _applyLock.Dispose();
    }
}
