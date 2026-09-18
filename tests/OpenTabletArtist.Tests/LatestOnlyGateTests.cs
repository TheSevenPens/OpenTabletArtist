using System;
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

        await running.WaitAsync(TimeSpan.FromSeconds(5)); // must complete, not fault
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

        await Task.WhenAll(running, queued).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(queuedRan);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var gate = new LatestOnlyGate();
        gate.Dispose();
        gate.Dispose();
    }
}
