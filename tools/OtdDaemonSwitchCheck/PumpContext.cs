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
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public PumpContext()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "otd-interop-context" };
        _thread.Start();
    }

    /// <inheritdoc />
    public bool IsCurrent => Thread.CurrentThread == _thread;

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
        _queue.Add(() =>
        {
            try { work(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }

    /// <summary>
    /// Runs <paramref name="body"/> on this context, with its awaits resuming here as well.
    /// </summary>
    /// <remarks>
    /// The point of the whole type. Without it the host's own settings operations would run wherever the
    /// console left them, and the library's automatic invalidation would be the only thing confined —
    /// which is the half that does not need protecting from itself.
    /// </remarks>
    public Task RunAsync(Func<Task> body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(async void () =>
        {
            try { await body().ConfigureAwait(true); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }

    private void Run()
    {
        // The reason awaits come back here rather than going to the thread pool.
        SynchronizationContext.SetSynchronizationContext(new QueueSynchronizationContext(_queue));
        foreach (var work in _queue.GetConsumingEnumerable()) work();
    }

    public void Dispose() => _queue.CompleteAdding();

    /// <summary>Sends continuations to the pump's queue, so <c>await</c> resumes on its thread.</summary>
    private sealed class QueueSynchronizationContext(BlockingCollection<Action> queue) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Dropped once the pump is closing: a continuation arriving after Dispose has nowhere to go,
            // and throwing here would surface on whatever thread completed the awaited work.
            if (!queue.IsAddingCompleted) queue.Add(() => d(state));
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }
}
