namespace OpenTabletArtist.Concurrency;

/// <summary>
/// Serializes async work so only one runs at a time, and coalesces to "latest wins":
/// if newer work is requested while an operation waits for the gate, the older queued
/// operation is skipped. This prevents overlapping <c>LoadDataAsync</c> calls (Connected
/// handler, the 30s fallback poll, and explicit Refresh) from interleaving or applying stale data
/// after newer data. See #19.
/// </summary>
public sealed class LatestOnlyGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _generation;
    // Disposal used to drop the semaphore out from under work that was still running — whose finally
    // block then called Release on it and threw ObjectDisposedException onto a background continuation
    // (#736). The gate is now disposed by whoever leaves last.
    private volatile bool _disposed;
    // Every caller currently inside RunAsync, including ones still queued on the semaphore. #736 counted
    // only the callers that had *acquired* the gate, which is what made disposal unsafe: see DisposeGate.
    private int _inFlight;
    // Wakes callers queued on the semaphore when the gate is disposed (#767). Disposing a SemaphoreSlim
    // does NOT complete outstanding WaitAsync calls, so without this anyone still waiting is stranded on
    // a task that never finishes. A never-completing task is worse than a faulting one: nothing observes
    // it and nothing reports it.
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _disposeLock = new();

    /// <summary>
    /// Runs <paramref name="work"/> under the gate, unless a newer <see cref="RunAsync"/>
    /// was requested while this call was waiting — in which case it is skipped (the newer
    /// call will run). Exactly the most recently requested operation executes its body.
    /// </summary>
    public async Task RunAsync(Func<Task> work)
    {
        if (_disposed) return;

        // Increment synchronously (before the first await) so concurrently-started calls
        // get strictly increasing generations and only the last-requested one survives.
        var mine = Interlocked.Increment(ref _generation);

        Interlocked.Increment(ref _inFlight);
        try
        {
            // A disposed gate cancels this rather than leaving us queued forever.
            try { await _gate.WaitAsync(_shutdown.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { return; }   // disposed while we queued
            catch (ObjectDisposedException) { return; }      // ...and torn down before we looked

            try
            {
                if (_disposed) return;  // disposed while we held the queue
                if (mine != Volatile.Read(ref _generation))
                    return; // superseded by a newer request while we waited

                await work().ConfigureAwait(true);
            }
            finally
            {
                try { _gate.Release(); }
                catch (ObjectDisposedException) { /* torn down beneath us; nothing to release */ }
            }
        }
        finally
        {
            // Last one out turns off the lights. Reaching here means this caller is finished touching
            // both _gate and _shutdown, so once the count is zero nothing can be mid-unwind.
            if (Interlocked.Decrement(ref _inFlight) == 0 && _disposed)
                DisposeGate();
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;

            // Wake every waiter *before* marking disposed — the order is load-bearing. Marking first
            // opens a window where the last in-flight caller observes _disposed, runs DisposeGate, and
            // disposes this CTS out from under the Cancel below, which then throws and is swallowed.
            // Cancel runs its registrations synchronously, so once it returns every waiter is released.
            try { _shutdown.Cancel(); }
            catch (ObjectDisposedException) { /* already torn down */ }

            _disposed = true;
            // Supersede anyone still queued so they return without running their body.
            Interlocked.Increment(ref _generation);
        }

        if (Volatile.Read(ref _inFlight) == 0) DisposeGate();
    }

    /// <summary>
    /// Only ever called when no caller is inside <see cref="RunAsync"/>, and that precondition is the
    /// whole point of <c>_inFlight</c>.
    ///
    /// <c>SemaphoreSlim.Dispose</c> drops its queue of waiters without completing their tasks. A waiter
    /// woken by <c>_shutdown</c> unwinds by asking the semaphore to remove it from that queue; if Dispose
    /// already emptied the queue, the removal reports "not found", which the semaphore reads as "you were
    /// granted the lock" — so it awaits a task nobody will ever complete, and hangs forever. Disposing
    /// while a cancelled-but-not-yet-unwound waiter exists is therefore not a tidy-up, it is a deadlock.
    /// </summary>
    private void DisposeGate()
    {
        try { _gate.Dispose(); }
        catch (ObjectDisposedException) { /* raced with the other exit path */ }
        try { _shutdown.Dispose(); }
        catch (ObjectDisposedException) { /* ditto */ }
    }
}
