using System.Collections.Concurrent;
using OtdInterop;

namespace OtdDaemonSwitchCheck;

/// <summary>
/// A serialized execution context for a console host: one thread, one queue.
/// </summary>
///
/// <remarks>
/// <para>
/// OtdInterop requires somewhere serialized to run the work it starts itself, and deliberately refuses to
/// invent one — a context inferred from an ambient <c>SynchronizationContext</c> would silently become the
/// thread pool in a host like this one, which has none.
/// </para>
/// <para>
/// So this is what a host without a UI framework supplies, and it is the whole of it. If this were hard to
/// write, the interface would be wrong.
/// </para>
/// </remarks>
internal sealed class PumpContext : IOtdExecutionContext, IDisposable
{
    private readonly BlockingCollection<(Action Work, TaskCompletionSource Done)> _queue = new();
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
        // for the queue would be waiting for a thread we are occupying.
        if (IsCurrent)
        {
            try { work(); return Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add((work, done));
        return done.Task;
    }

    private void Run()
    {
        foreach (var (work, done) in _queue.GetConsumingEnumerable())
        {
            try { work(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}
