using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OtdInterop;

namespace OpenTabletArtist.Tests;

/// <summary>
/// An execution context a test steps by hand.
/// </summary>
///
/// <remarks>
/// This is the reason <see cref="IOtdExecutionContext"/> is an interface rather than a dispatcher
/// reference. Orderings around a reconnect are otherwise reproducible only by luck: the transport
/// reconnects on its own thread, the library posts its identification somewhere, and the interesting
/// question is what a host can observe <em>while that is still pending</em>. Holding the queue makes that
/// a deliberate step rather than a race to lose.
/// </remarks>
internal sealed class ControllableContext : IOtdExecutionContext
{
    private readonly List<(Action Work, TaskCompletionSource Done)> _pending = new();

    /// <summary>How many posted items are waiting to run.</summary>
    public int Pending => _pending.Count;

    /// <summary>
    /// Whether the calling thread is this context.
    /// </summary>
    /// <remarks>
    /// True, because a test drives one thread and posted work runs on it when released. The property
    /// exists for the library to assert with, and making it lie here would fail assertions about a
    /// contract the test is in fact honouring.
    /// </remarks>
    public bool IsCurrent => true;

    /// <inheritdoc />
    public Task PostAsync(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Add((work, done));
        return done.Task;
    }

    /// <summary>Runs everything posted so far, in order, including anything those items post.</summary>
    public void Drain()
    {
        while (_pending.Count > 0)
        {
            var (work, done) = _pending[0];
            _pending.RemoveAt(0);
            try { work(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        }
    }
}
