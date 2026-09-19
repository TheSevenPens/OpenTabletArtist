using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletArtist.Concurrency;
using Xunit;

namespace OpenTabletArtist.Tests;

public class LatestOnlyGateTests
{
    [Fact]
    public async Task SingleCall_Runs()
    {
        using var gate = new LatestOnlyGate();
        var ran = false;
        await gate.RunAsync(() => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
    }

    [Fact]
    public async Task SequentialCalls_AllRun()
    {
        using var gate = new LatestOnlyGate();
        var count = 0;
        await gate.RunAsync(() => { count++; return Task.CompletedTask; });
        await gate.RunAsync(() => { count++; return Task.CompletedTask; });
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task WhileOneRuns_OnlyTheLatestQueuedAlsoRuns()
    {
        using var gate = new LatestOnlyGate();
        var block = new TaskCompletionSource();
        var aEntered = new TaskCompletionSource();
        bool aRan = false, bRan = false, cRan = false;

        // A acquires the gate and holds it until we release `block`.
        var tA = gate.RunAsync(async () =>
        {
            aRan = true;
            aEntered.SetResult();
            await block.Task;
        });

        await aEntered.Task; // A is now inside its body, holding the gate.

        // B then C are requested while A holds the gate → both queue (generations 2, 3).
        var tB = gate.RunAsync(() => { bRan = true; return Task.CompletedTask; });
        var tC = gate.RunAsync(() => { cRan = true; return Task.CompletedTask; });

        // Release A. B acquires first (FIFO) but is superseded by C → skips; C runs.
        block.SetResult();
        await Task.WhenAll(tA, tB, tC);

        Assert.True(aRan);
        Assert.False(bRan); // superseded
        Assert.True(cRan);  // latest wins
    }

    // --- Disposal while work is in flight (#736) ---

    /// <summary>
    /// The regression: Dispose used to drop the semaphore while work still held it, and that work's
    /// finally block then called Release on a disposed semaphore — an ObjectDisposedException raised on
    /// a background continuation, where nothing was watching for it.
    /// </summary>
    [Fact]
    public async Task Dispose_WhileWorkHoldsTheGate_DoesNotThrowFromTheRunningWork()
    {
        var gate = new LatestOnlyGate();
        var block = new TaskCompletionSource();
        var entered = new TaskCompletionSource();

        var running = gate.RunAsync(async () =>
        {
            entered.SetResult();
            await block.Task;
        });

        await entered.Task;     // work is inside the gate
        gate.Dispose();         // owner torn down beneath it
        block.SetResult();

        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); // must complete, not fault
    }

    [Fact]
    public async Task RunAsync_AfterDispose_IsIgnored()
    {
        var gate = new LatestOnlyGate();
        gate.Dispose();
        var ran = false;

        await gate.RunAsync(() => { ran = true; return Task.CompletedTask; });

        Assert.False(ran);
    }

    /// <summary>Work queued behind a running operation must not start once the owner is gone — it would
    /// be acting on behalf of something already torn down.</summary>
    [Fact]
    public async Task Dispose_WhileWorkIsQueued_TheQueuedWorkNeverRuns()
    {
        var gate = new LatestOnlyGate();
        var block = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var queuedRan = false;

        var running = gate.RunAsync(async () =>
        {
            entered.SetResult();
            await block.Task;
        });
        await entered.Task;

        var queued = gate.RunAsync(() => { queuedRan = true; return Task.CompletedTask; });
        gate.Dispose();
        block.SetResult();

        await Task.WhenAll(running, queued).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(queuedRan);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var gate = new LatestOnlyGate();
        gate.Dispose();
        gate.Dispose();
    }
    /// <summary>
    /// #767. #736 fixed the operation that <em>holds</em> the gate; callers queued behind it were still
    /// stranded. <c>_active</c> counts only those that acquired the semaphore, and disposing a
    /// <c>SemaphoreSlim</c> does not complete outstanding <c>WaitAsync</c> calls — so their tasks never
    /// settle. A task that never completes is worse than one that faults: nothing observes it and nothing
    /// reports it, and <c>AppSession</c> disposes its load gate during shutdown with reloads potentially
    /// queued.
    /// </summary>
    [Fact]
    public async Task Dispose_WithSeveralQueued_SettlesEveryTask()
    {
        var gate = new LatestOnlyGate();
        // Asynchronous continuations: with the default, SetResult runs the awaiting continuation inline
        // on the completing thread, so the test body would resume *inside* the gate's own call stack.
        var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedRan = 0;

        var running = gate.RunAsync(async () =>
        {
            entered.SetResult();
            await block.Task;
        });
        await entered.Task;

        // Three more pile up behind the one holding the gate.
        var queued = new[]
        {
            gate.RunAsync(() => { Interlocked.Increment(ref queuedRan); return Task.CompletedTask; }),
            gate.RunAsync(() => { Interlocked.Increment(ref queuedRan); return Task.CompletedTask; }),
            gate.RunAsync(() => { Interlocked.Increment(ref queuedRan); return Task.CompletedTask; }),
        };

        gate.Dispose();
        block.SetResult();

        var all = queued.Append(running).ToArray();
        try { await Task.WhenAll(all).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
        catch (TimeoutException)
        {
            Assert.Fail($"Disposal stranded {all.Count(t => !t.IsCompleted)} of {all.Length} callers: "
                        + "a queued caller is still waiting on a gate nobody will ever release.");
        }
        Assert.Equal(0, Volatile.Read(ref queuedRan));   // and none of them ran
    }

    [Fact]
    public async Task Dispose_WithSeveralQueued_NoneFault()
    {
        var gate = new LatestOnlyGate();
        var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = gate.RunAsync(async () => { entered.SetResult(); await block.Task; });
        await entered.Task;
        var queued = Enumerable.Range(0, 5)
            .Select(_ => gate.RunAsync(() => Task.CompletedTask))
            .ToArray();

        gate.Dispose();
        block.SetResult();

        // Settling is not enough — a cancelled or faulted task is still a surprise to a caller that was
        // only ever asked to reload.
        await Task.WhenAll(queued.Append(running)).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.All(queued, t => Assert.Equal(TaskStatus.RanToCompletion, t.Status));
    }
}
