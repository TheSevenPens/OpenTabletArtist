namespace OtdArtist.Concurrency;

/// <summary>
/// Runs an async operation single-flight: only one runs at a time. If <see cref="Trigger"/>
/// is called while an operation is in progress, exactly one more run is scheduled after the
/// current one finishes (any number of concurrent requests during a run coalesce into a
/// single rerun).
///
/// This is the reconnect coordinator for <c>DaemonClient</c> (#33): it closes the
/// immediate-disconnect-during-connect window where a reconnect request could otherwise be
/// dropped because the previous connect attempt hadn't released its single-flight slot yet.
/// The decision to rerun vs stop is made under a lock together with consuming the pending
/// flag, so a request can never be lost in the release window.
/// </summary>
public sealed class CoalescingSingleFlight
{
    private readonly object _lock = new();
    private bool _running;
    private bool _pending;
    private Func<Task>? _pendingOperation;

    /// <summary>
    /// Ensures <paramref name="operation"/> runs. If one is already running, records that
    /// another run is wanted and returns immediately; that rerun happens once the current
    /// run completes. Fire-and-forget. Exceptions from <paramref name="operation"/> are
    /// swallowed so one failed run can't tear down the coordinator.
    /// </summary>
    /// <remarks>
    /// If multiple triggers arrive during an active run they coalesce into a single rerun
    /// that uses the <em>most recently</em> supplied operation (latest-wins).
    /// </remarks>
    public void Trigger(Func<Task> operation)
    {
        lock (_lock)
        {
            if (_running)
            {
                _pending = true;
                _pendingOperation = operation; // latest wins for the coalesced rerun
                return;
            }
            _running = true;
        }

        _ = RunLoopAsync(operation);
    }

    private async Task RunLoopAsync(Func<Task> operation)
    {
        var current = operation;
        while (true)
        {
            try
            {
                await current().ConfigureAwait(false);
            }
            catch
            {
                // Swallow: a failed run must not strand the coordinator in the running state.
            }

            lock (_lock)
            {
                if (!_pending)
                {
                    _running = false;
                    _pendingOperation = null;
                    return;
                }
                _pending = false; // consume one coalesced request and run again
                current = _pendingOperation ?? current;
                _pendingOperation = null;
            }
        }
    }
}
