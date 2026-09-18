using OpenTabletArtist.Services;

namespace OpenTabletArtist.Concurrency;

/// <summary>
/// A trailing debounce with an owner: each <see cref="Schedule"/> supersedes the one before it, and only
/// the last one to survive the quiet period runs. Disposing cancels whatever is pending and refuses
/// anything further (#736).
///
/// This existed four times over inside the tablet editor as a hand-rolled
/// <c>CancellationTokenSource</c> + <c>Task.Delay</c> pair. None of them was cancelled on disposal, so a
/// disposed editor could still wake up 250–400 ms later and push settings at a session that was being
/// torn down; none disposed the token source it superseded; and all of them dropped exceptions from the
/// fire-and-forget task on the floor. One implementation fixes all three, and can be tested without a
/// dispatcher.
///
/// Continuations deliberately resume on the captured context: callers schedule from the UI thread and
/// their callbacks touch UI state, so <c>ConfigureAwait(false)</c> here would move that work off-thread.
/// </summary>
public sealed class TrailingDebounce : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly string _name;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;

    /// <param name="delayMs">Quiet period before the scheduled work runs.</param>
    /// <param name="name">Used only to identify the source in a log line when the work throws.</param>
    public TrailingDebounce(int delayMs, string name)
    {
        _delay = TimeSpan.FromMilliseconds(delayMs);
        _name = name;
    }

    /// <summary>True once <see cref="Dispose"/> has run. Nothing further will be scheduled or fire.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>True while work is scheduled and waiting out the quiet period.</summary>
    public bool IsPending => !_disposed && _cts is { IsCancellationRequested: false };

    /// <summary>Schedule <paramref name="work"/>, superseding anything already pending. A no-op after
    /// disposal — a closed editor must not keep queuing writes.</summary>
    public void Schedule(Func<Task> work)
    {
        if (_disposed) return;

        var previous = _cts;
        var cts = new CancellationTokenSource();
        _cts = cts;
        Cancel(previous);

        _ = RunAsync(cts.Token, work);
    }

    /// <summary>Cancel anything pending without disposing — the debounce stays usable.</summary>
    public void CancelPending()
    {
        var pending = _cts;
        _cts = null;
        Cancel(pending);
    }

    private async Task RunAsync(CancellationToken ct, Func<Task> work)
    {
        try { await Task.Delay(_delay, ct); }
        catch (TaskCanceledException) { return; }

        // Re-check both: the token may have been cancelled during the resume, and Dispose may have run
        // while this continuation was queued.
        if (ct.IsCancellationRequested || _disposed) return;

        try
        {
            await work();
        }
        catch (Exception ex)
        {
            // Fire-and-forget by design, but a silently dropped failure here means an edit the artist
            // made never landed and nothing said so.
            AppLog.Warn($"A debounced {_name} edit failed to apply.", ex);
        }
    }

    private static void Cancel(CancellationTokenSource? cts)
    {
        if (cts == null) return;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* already torn down */ }
        cts.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPending();
    }
}
