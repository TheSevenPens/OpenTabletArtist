using System;
using System.Threading;
using System.Threading.Tasks;
using OtdDaemonSwitchCheck;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The switch-check tool's execution context.
/// </summary>
///
/// <remarks>
/// <para>
/// Here rather than in a throwaway harness because a harness that endorses its own subject is not
/// evidence. The file is compiled into this assembly (see the csproj), so these run against the real
/// <c>PumpContext</c> on all three CI platforms — while CI still never runs the tool itself.
/// </para>
/// <para>
/// What is being checked is that <c>await</c> comes back to the pump. A thread with no synchronization
/// context sends every continuation to the thread pool, so a pump that only accepts posts confines the
/// work it is handed and nothing that work goes on to do.
/// </para>
/// </remarks>
public class PumpContextTests
{
    [Fact]
    public async Task AwaitsResumeOnThePump()
    {
        using var pump = new PumpContext();
        var pumpThread = 0;
        var offThread = 0;
        var afterTimer = 0;
        var afterYield = 0;

        await pump.RunAsync(async () =>
        {
            pumpThread = Environment.CurrentManagedThreadId;

            await Task.Delay(20, TestContext.Current.CancellationToken);
            afterTimer = Environment.CurrentManagedThreadId;

            // The shape that matters: completed from another thread entirely.
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(() => held.SetResult(), TestContext.Current.CancellationToken);
            await held.Task;
            offThread = Environment.CurrentManagedThreadId;

            await Task.Yield();
            afterYield = Environment.CurrentManagedThreadId;
        });

        Assert.Equal(pumpThread, afterTimer);
        Assert.Equal(pumpThread, offThread);
        Assert.Equal(pumpThread, afterYield);
    }

    /// <summary>The escape hatch still escapes, so the pump is a default and not a trap.</summary>
    [Fact]
    public async Task ConfigureAwaitFalseLeavesThePump()
    {
        using var pump = new PumpContext();
        var left = false;

        await pump.RunAsync(async () =>
        {
            await Task.Delay(20, TestContext.Current.CancellationToken).ConfigureAwait(false);
            left = !pump.IsCurrent;
        });

        Assert.True(left);
    }

    /// <summary>
    /// Posting from the pump runs inline on the caller, rather than queueing behind it.
    /// </summary>
    /// <remarks>
    /// Compared against the calling thread rather than <c>IsCurrent</c>: a queued item reports
    /// <c>IsCurrent</c> too, so that check passed even with the synchronization context removed.
    /// </remarks>
    [Fact]
    public async Task PostingFromThePumpRunsInline()
    {
        using var pump = new PumpContext();
        var caller = 0;
        var ranOn = 0;
        var synchronous = false;

        await pump.RunAsync(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            var posted = pump.PostAsync(() => ranOn = Environment.CurrentManagedThreadId);
            synchronous = posted.IsCompleted;
            return posted;
        });

        Assert.Equal(caller, ranOn);
        Assert.True(synchronous);
    }

    [Fact]
    public async Task PostingFromOutsideRunsOnThePump()
    {
        using var pump = new PumpContext();
        var pumpThread = 0;
        await pump.RunAsync(() =>
        {
            pumpThread = Environment.CurrentManagedThreadId;
            return Task.CompletedTask;
        });

        var ranOn = 0;
        await pump.PostAsync(() => ranOn = Environment.CurrentManagedThreadId);

        Assert.Equal(pumpThread, ranOn);
    }

    [Fact]
    public async Task FailuresAreReportedRatherThanLost()
    {
        using var pump = new PumpContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pump.RunAsync(() => throw new InvalidOperationException("synchronous")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pump.RunAsync(async () =>
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                throw new InvalidOperationException("after an await");
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pump.PostAsync(() => throw new InvalidOperationException("posted")));
    }

    /// <summary>
    /// Shutdown waits for an accepted operation that is still mid-await, rather than abandoning it.
    /// </summary>
    /// <remarks>
    /// The case that made the previous implementation wrong. It closed the queue immediately, so the
    /// continuation this operation was waiting on had nowhere to run: the task stayed pending for the
    /// life of the process, and a harness of mine asserted that as correct behaviour. Closing a queue is
    /// not the same as finishing the work that depends on it.
    /// </remarks>
    [Fact]
    public async Task ShutdownWaitsForAnAcceptedOperationStillInFlight()
    {
        var pump = new PumpContext();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;

        var accepted = pump.RunAsync(async () =>
        {
            await release.Task;
            finished = true;
        });

        // Let it reach the await.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(accepted.IsCompleted);

        var shutdown = Task.Run(pump.Dispose, TestContext.Current.CancellationToken);

        // Wait until shutdown has actually begun rather than racing it. Releasing immediately let this
        // pass against a Dispose that closed the queue first, because the continuation usually got in
        // before it: the mutation went undetected until this wait was added.
        await WaitUntilAdmissionIsRefused(pump);

        release.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.True(accepted.IsCompletedSuccessfully);
        Assert.True(finished);
        Assert.True(pump.ShutDownCleanly);
    }

    /// <summary>
    /// Waits until the pump refuses new root operations, which is the observable start of shutdown.
    /// </summary>
    /// <remarks>
    /// The only signal available from outside, and enough: once roots are refused, <c>_closing</c> is
    /// set, so the remaining question is whether the queue is still pumping for what was already
    /// accepted. The bound is a failure bound, not the synchronisation.
    /// </remarks>
    private static async Task WaitUntilAdmissionIsRefused(PumpContext pump)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try { _ = pump.RunAsync(() => Task.CompletedTask); }
            catch (ObjectDisposedException) { return; }

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Fail("shutdown never began");
    }

    /// <summary>A new root operation offered once shutdown has begun is refused, not queued.</summary>
    /// <remarks>
    /// Admitting work while draining is how a shutdown fails to terminate. Refusing is also what makes
    /// the wait above finite.
    /// </remarks>
    [Fact]
    public void AdmissionAfterShutdownIsRefused()
    {
        var pump = new PumpContext();
        pump.Dispose();

        // An Action, not a Func<Task>, so this also asserts the refusal is synchronous: a caller finds
        // out at the call rather than by awaiting something that was never going to run.
        Assert.Throws<ObjectDisposedException>(() => { _ = pump.RunAsync(() => Task.CompletedTask); });
    }

    /// <summary>
    /// Posting to a closed pump comes back as a faulted task rather than a silently stranded one.
    /// </summary>
    /// <remarks>
    /// This is the path the library takes on every transition, and it reports what it could not do on the
    /// host's context. A task that never completes would give it nothing to report.
    /// </remarks>
    [Fact]
    public async Task PostingToAClosedPumpFaults()
    {
        var pump = new PumpContext();
        pump.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pump.PostAsync(() => { }));
    }

    /// <summary>Disposing twice is not an error, and the second call does nothing.</summary>
    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var pump = new PumpContext();
        pump.Dispose();
        pump.Dispose();

        Assert.True(pump.ShutDownCleanly);
    }

    /// <summary>
    /// <c>Send</c> runs inline on the pump, and is refused from anywhere else.
    /// </summary>
    /// <remarks>
    /// It used to run the callback on the calling thread — concurrently with the pump, while claiming to
    /// have serialized it. Queueing and blocking instead would deadlock whenever the pump is itself
    /// waiting on the caller, so for an internal pump with no callers the honest answer is to refuse.
    /// </remarks>
    [Fact]
    public async Task SendIsInlineOnThePumpAndRefusedOffIt()
    {
        using var pump = new PumpContext();
        SynchronizationContext? pumpSync = null;
        var ranOn = 0;
        var pumpThread = 0;

        await pump.RunAsync(() =>
        {
            pumpThread = Environment.CurrentManagedThreadId;
            pumpSync = SynchronizationContext.Current;
            pumpSync!.Send(_ => ranOn = Environment.CurrentManagedThreadId, null);
            return Task.CompletedTask;
        });

        Assert.Equal(pumpThread, ranOn);
        Assert.Throws<NotSupportedException>(() => pumpSync!.Send(_ => { }, null));
    }

    /// <summary>Disposing from the pump's own thread is refused rather than deadlocking.</summary>
    /// <remarks>
    /// It would be waiting for a thread it is occupying, and the hang would point nowhere near the cause.
    /// </remarks>
    [Fact]
    public async Task DisposingFromThePumpIsRefused()
    {
        using var pump = new PumpContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pump.RunAsync(() =>
            {
                pump.Dispose();
                return Task.CompletedTask;
            }));
    }
}
