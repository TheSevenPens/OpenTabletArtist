using System.Collections.Concurrent;
using OtdInterop;

namespace OtdDaemonSwitchCheck;

/// <summary>
/// A serialized execution context for a console host: one thread, one queue, and a synchronization
/// context so that <c>await</c> comes back to it.
/// </summary>
///
/// <remarks>
/// <para>
/// OtdInterop requires somewhere serialized to run the work it starts itself, and deliberately refuses to
/// invent one — a context inferred from an ambient <see cref="SynchronizationContext"/> would silently
/// become the thread pool in a host like this one, which has none. So this is what a host without a UI
/// framework supplies.
/// </para>
/// <para>
/// <b>Posting is not enough, and that is the part worth reading.</b> Running the library's callbacks on a
/// dedicated thread while the host's own settings calls run on console continuations would leave those
/// calls, and everything their awaits resume onto, outside the context — which is exactly the confinement
/// the contract asks for and would not have. A thread with no synchronization context sends every
/// continuation to the thread pool, so starting work here does not keep it here.
/// </para>
/// <para>
/// Hence <see cref="RunAsync"/>: the whole body runs on this thread, and because the thread installs a
/// synchronization context that posts back to the same queue, each <c>await</c> resumes on it too.
/// </para>
/// </remarks>
internal sealed class PumpContext : IOtdExecutionContext, IDisposable
{
    /// <summary>How long shutdown waits for accepted work, and then for the thread.</summary>
    private static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(30);

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    /// <summary>
    /// Guards admission and closure together.
    /// </summary>
    /// <remarks>
    /// One lock for both because they are one decision. Checking <c>IsAddingCompleted</c> and then adding
    /// was a check-then-use race: <see cref="Dispose"/> could complete the queue in between, so the same
    /// call could either strand a continuation silently or throw on whatever thread happened to complete
    /// the awaited work.
    /// </remarks>
    private readonly object _gate = new();

    /// <summary>Root operations accepted and not yet settled. Shutdown waits for these.</summary>
    private readonly List<Task> _accepted = [];

    private bool _closing;

    public PumpContext()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "otd-interop-context" };
        _thread.Start();
    }

    /// <inheritdoc />
    public bool IsCurrent => Thread.CurrentThread == _thread;

    /// <summary>
    /// Whether shutdown settled everything it had accepted, rather than running out of patience.
    /// </summary>
    /// <remarks>
    /// Reported rather than swallowed: abandoning accepted work is a failed shutdown, and a tool whose
    /// exit code is its whole output should not present one as a success.
    /// </remarks>
    public bool ShutDownCleanly { get; private set; } = true;

    /// <inheritdoc />
    public Task PostAsync(Action work)
    {
        // Inline when already here, as the contract requires: queueing behind ourselves and then waiting
        // would be waiting for a thread we are occupying.
        if (IsCurrent)
        {
            try { work(); return Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = TryEnqueue(() =>
        {
            try { work(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        });

        // A faulted task rather than a silently stranded one. The library awaits this and reports what it
        // could not do on the host's context, which is exactly this situation.
        return queued
            ? done.Task
            : Task.FromException(new ObjectDisposedException(nameof(PumpContext)));
    }

    /// <summary>
    /// Runs <paramref name="body"/> on this context, with its awaits resuming here as well.
    /// </summary>
    /// <remarks>
    /// The point of the whole type. Without it the host's own settings operations would run wherever the
    /// console left them, and the library's automatic invalidation would be the only thing confined —
    /// which is the half that does not need protecting from itself.
    ///
    /// A root operation, so shutdown waits for it, and it is refused once shutdown has begun: admitting
    /// new work while trying to drain is how a shutdown fails to terminate.
    /// </remarks>
    public Task RunAsync(Func<Task> body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);

            _accepted.RemoveAll(t => t.IsCompleted);
            _accepted.Add(done.Task);

            // async void, deliberately. The queue holds Action, and the body must be able to return at
            // its first await so the pump can go on and service the continuation -- which is the whole
            // arrangement. Every path is inside the try, and `done` is private and completed once, so
            // there is no escape to the unhandled-exception path: completion and failure are both
            // reported through the task this returns.
            _queue.Add(async void () =>
            {
                try { await body().ConfigureAwait(true); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
            });
        }

        return done.Task;
    }

    /// <summary>Queues work unless the pump has closed, deciding both under the one lock.</summary>
    private bool TryEnqueue(Action work)
    {
        lock (_gate)
        {
            if (_queue.IsAddingCompleted) return false;
            _queue.Add(work);
            return true;
        }
    }

    private void Run()
    {
        // The reason awaits come back here rather than going to the thread pool. Constructed here so it
        // can capture the pump's own thread.
        SynchronizationContext.SetSynchronizationContext(
            new QueueSynchronizationContext(this, Thread.CurrentThread));
        foreach (var work in _queue.GetConsumingEnumerable()) work();
    }

    /// <summary>
    /// Stops accepting new operations, lets the accepted ones settle, and only then closes the queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the whole of it. Closing first and hoping is not a shutdown: every continuation an
    /// accepted operation is waiting on arrives through this queue, so a closed queue strands it and its
    /// task stays pending for the life of the process. Cancelling afterwards cannot rescue that either —
    /// by then there is no context left to run the cancellation on.
    /// </para>
    /// <para>
    /// So: refuse new roots, keep pumping, wait for what was accepted, close, join.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        // Would be waiting for a thread we are occupying, and the hang would point nowhere near the cause.
        if (IsCurrent)
            throw new InvalidOperationException("A PumpContext cannot be disposed from its own thread.");

        Task[] pending;
        lock (_gate)
        {
            if (_closing) return;
            _closing = true;
            pending = [.. _accepted];
        }

        try
        {
            // Faults belong to whoever awaited the operation; this only waits for them to settle.
            ShutDownCleanly = Task.WaitAll(pending, ShutdownBound);
        }
        catch (AggregateException)
        {
            // Settled, which is what was being waited for.
        }

        lock (_gate) _queue.CompleteAdding();

        if (!_thread.Join(ShutdownBound)) ShutDownCleanly = false;
    }

    /// <summary>Sends continuations to the pump's queue, so <c>await</c> resumes on its thread.</summary>
    private sealed class QueueSynchronizationContext(PumpContext pump, Thread owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Dropped once the pump has closed, which the shutdown order makes rare rather than
            // impossible: a continuation arriving then has nowhere to run, and throwing would surface on
            // whatever thread completed the awaited work, which has nothing to do with it.
            pump.TryEnqueue(() => d(state));
        }

        /// <summary>Refused from any thread but the pump's.</summary>
        /// <remarks>
        /// This was <c>d(state)</c>, which ran the callback on the calling thread — concurrently with the
        /// pump, while claiming to have serialized it. The obvious repair is to queue it and block, and
        /// that is worse: a caller the pump is itself waiting on deadlocks, and correct blocking
        /// semantics cannot remove a circular wait that the caller created.
        ///
        /// Nothing in this tool calls it. Refusing loudly is the honest answer for an internal pump; if a
        /// real caller ever appears, that call chain is worth reading rather than guessing at.
        /// </remarks>
        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Thread.CurrentThread == owner) { d(state); return; }

            throw new NotSupportedException(
                "PumpContext does not support Send from another thread. Post to it, or do the work on it.");
        }

        /// <summary>The same context, since it holds nothing per-copy and the queue must be shared.</summary>
        public override SynchronizationContext CreateCopy() => this;
    }
}
