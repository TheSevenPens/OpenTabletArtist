namespace OpenTabletArtist.Concurrency;

/// <summary>
/// Serializes async work so only one runs at a time, and coalesces to "latest wins":
/// if newer work is requested while an operation waits for the gate, the older queued
/// operation is skipped. This prevents overlapping <c>LoadDataAsync</c> calls (Connected
/// handler, the 3s poll, and explicit Refresh) from interleaving or applying stale data
/// after newer data. See #19.
/// </summary>
public sealed class LatestOnlyGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _generation;

    /// <summary>
    /// Runs <paramref name="work"/> under the gate, unless a newer <see cref="RunAsync"/>
    /// was requested while this call was waiting — in which case it is skipped (the newer
    /// call will run). Exactly the most recently requested operation executes its body.
    /// </summary>
    public async Task RunAsync(Func<Task> work)
    {
        // Increment synchronously (before the first await) so concurrently-started calls
        // get strictly increasing generations and only the last-requested one survives.
        var mine = Interlocked.Increment(ref _generation);

        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (mine != Volatile.Read(ref _generation))
                return; // superseded by a newer request while we waited

            await work().ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
