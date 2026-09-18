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
    private int _active;

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

        try { await _gate.WaitAsync().ConfigureAwait(true); }
        catch (ObjectDisposedException) { return; } // disposed while we queued

        Interlocked.Increment(ref _active);
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

            // Last one out turns off the lights, so Dispose never pulls the semaphore from under
            // running work.
            if (Interlocked.Decrement(ref _active) == 0 && _disposed)
                DisposeGate();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Supersede anyone still queued so they return without running their body.
        Interlocked.Increment(ref _generation);
        if (Volatile.Read(ref _active) == 0) DisposeGate();
    }

    private void DisposeGate()
    {
        try { _gate.Dispose(); }
        catch (ObjectDisposedException) { /* raced with the other exit path */ }
    }
}
