using System;
using System.Collections.Generic;
using System.Threading;
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
    /// True by default, because a test drives one thread and posted work runs on it when released; making
    /// it lie would fail assertions about a contract the test is in fact honouring.
    ///
    /// Settable for the one test whose subject IS the lie -- a host whose context runs the library's work
    /// somewhere else. Without a way to express that, the library's check could never be shown to fire,
    /// and a check that cannot fail is not a check.
    /// </remarks>
    public bool IsCurrent { get; set; } = true;

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately NOT <c>RunContinuationsAsynchronously</c>. That is the right default almost
    /// everywhere, and it is wrong here: it would push whatever awaits this onto the thread pool, so work
    /// the library does <em>after</em> posting — reporting a failure, for instance — would land after
    /// <see cref="Drain"/> had already returned, and a test would race it.
    ///
    /// It cost a green local run and a red CI one to notice, in two different shapes: a missing entry on
    /// one platform and a collection modified mid-enumeration on another. A context a test controls has
    /// to control the continuations too, or it controls nothing.
    /// </remarks>
    /// <summary>
    /// When set, posts are refused with a faulted task and the work never runs.
    /// </summary>
    /// <remarks>
    /// A host shutting down does this: its dispatcher stops accepting work while the library is still
    /// deciding things. The library has to settle whatever represented that work rather than leaving a
    /// caller waiting for a callback nobody will ever run.
    /// </remarks>
    public bool RefusePosts { get; set; }

    /// <summary>
    /// When set, posts come back cancelled and the work never runs.
    /// </summary>
    /// <remarks>
    /// A host abandoning queued work reports it this way. No cancellation token is needed to express it:
    /// the returned task carries the answer, which is a thing I asserted was impossible and was wrong
    /// about.
    /// </remarks>
    public bool CancelPosts { get; set; }

    public Task PostAsync(Action work)
    {
        if (CancelPosts) return Task.FromCanceled(new CancellationToken(canceled: true));

        if (RefusePosts)
            return Task.FromException(new ObjectDisposedException(nameof(ControllableContext)));

        var done = new TaskCompletionSource();
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
