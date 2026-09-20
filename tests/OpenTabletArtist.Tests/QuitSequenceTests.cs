using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What quitting does, in what order, and within what (#167, #596, #828).
/// </summary>
///
/// <remarks>
/// The order is the whole of it, and it is the kind that fails quietly: each step depends on the state
/// the next one takes away, so running them the wrong way round does not throw — the step simply does
/// nothing, or does it to the wrong thing. None of it was reachable while it lived in the tray, which
/// needs a real <c>TrayIcon</c> and a desktop lifetime.
/// </remarks>
public class QuitSequenceTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>A budget for tests about giving up, so they need not spend the real one.</summary>
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The per-app restore, the session close and the daemon stop happen in that order.
    /// </summary>
    [Fact]
    public async Task TheStepsRunInTheOrderEachOneNeeds()
    {
        var order = new List<string>();

        await QuitSequence.RunAsync(
            restorePerApp: () => { order.Add("restore"); return Task.CompletedTask; },
            closeSession: _ => { order.Add("close"); return Task.FromResult(true); },
            stopDaemon: () => { order.Add("stop"); return Task.CompletedTask; },
            warn: _ => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "restore", "close", "stop" }, order);
    }

    /// <summary>
    /// Stopping the daemon waits for the write the close is settling, rather than killing it out from
    /// under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sequence used to be restore → stop → close, and the consequence is what matters rather than
    /// the order in the abstract: an apply in flight when the user chose "quit and stop" lost its daemon
    /// mid-write. The close could then see that task finish, which is not the same as the edit surviving.
    /// </para>
    /// <para>
    /// So this models the consequence — the "write" succeeds only while the daemon is still running —
    /// rather than asserting a list of names, which the previous ordering test would also have passed
    /// with the stop still killing the connection first.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StoppingTheDaemon_WaitsForTheWriteTheCloseIsSettling()
    {
        var daemonRunning = true;
        var writeSurvived = false;

        await QuitSequence.RunAsync(
            restorePerApp: null,
            closeSession: async _ =>
            {
                // Stands in for a write that has reached the daemon and not yet disk.
                await Task.Delay(50, TestContext.Current.CancellationToken);
                writeSurvived = daemonRunning;
                return writeSurvived;
            },
            stopDaemon: () => { daemonRunning = false; return Task.CompletedTask; },
            warn: _ => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(writeSurvived);
        Assert.False(daemonRunning);
    }

    /// <summary>
    /// The restore still happens before the close, because it needs the connection the close ends.
    /// </summary>
    [Fact]
    public async Task TheRestoreStillHappensWhileTheSessionIsOpen()
    {
        var closed = false;
        var restoredWhileOpen = false;

        await QuitSequence.RunAsync(
            restorePerApp: () => { restoredWhileOpen = !closed; return Task.CompletedTask; },
            closeSession: _ => { closed = true; return Task.FromResult(true); },
            stopDaemon: null,
            warn: _ => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(restoredWhileOpen);
        Assert.True(closed);
    }

    /// <summary>
    /// The whole exit shares one budget; it is not one budget per step.
    /// </summary>
    /// <remarks>
    /// Three steps of five seconds each meant a wholly unresponsive daemon could hold the application
    /// open for fifteen, with nothing on screen explaining it.
    /// </remarks>
    [Fact]
    public async Task TheWholeExitSharesOneBudget()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan? whatTheCloseGot = null;

        var clock = Stopwatch.StartNew();
        try
        {
            await QuitSequence.RunAsync(
                restorePerApp: () => never.Task,
                closeSession: w => { whatTheCloseGot = w; return Task.FromResult(true); },
                stopDaemon: null,
                warn: _ => { }, budget: Short).WaitAsync(Bound, TestContext.Current.CancellationToken);
        }
        finally
        {
            never.TrySetResult();
        }

        clock.Stop();

        Assert.True(clock.Elapsed < Short + TimeSpan.FromSeconds(2), $"{clock.Elapsed}");
        Assert.NotNull(whatTheCloseGot);
        Assert.True(whatTheCloseGot < TimeSpan.FromMilliseconds(150),
            $"the close was handed {whatTheCloseGot}, so the budget was not shared");
    }

    /// <summary>
    /// With nothing spent before it, the close gets what is left of the budget — which is most of it.
    /// </summary>
    [Fact]
    public async Task TheCloseIsGivenWhatIsLeftOfTheApplicationsOwnBudget()
    {
        TimeSpan? given = null;

        await QuitSequence.RunAsync(
            restorePerApp: null,
            closeSession: w => { given = w; return Task.FromResult(true); },
            stopDaemon: null,
            warn: _ => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.NotNull(given);
        Assert.True(given > QuitSequence.Budget - TimeSpan.FromSeconds(1), $"{given}");
        Assert.True(given <= QuitSequence.Budget, $"{given}");
        Assert.NotEqual(System.Threading.Timeout.InfiniteTimeSpan, given);
    }

    /// <summary>
    /// A step that hangs is given up on, and the ones after it still run.
    /// </summary>
    [Fact]
    public async Task AStepThatHangs_DoesNotCostTheOnesAfterIt()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var warnings = new List<string>();
        var closed = false;

        try
        {
            await QuitSequence.RunAsync(
                restorePerApp: () => never.Task,
                closeSession: _ => { closed = true; return Task.FromResult(true); },
                stopDaemon: null,
                warn: warnings.Add, budget: Short).WaitAsync(Bound, TestContext.Current.CancellationToken);

            Assert.True(closed);
            Assert.Contains(warnings, w => w.Contains("per-app restore") && w.Contains("budget"));
        }
        finally
        {
            never.TrySetResult();
        }
    }

    /// <summary>
    /// A step that throws where it stands does not stop the ones after it.
    /// </summary>
    [Fact]
    public async Task AStepThatThrows_DoesNotStopTheOnesAfterIt()
    {
        var closed = false;
        var warnings = new List<string>();

        await QuitSequence.RunAsync(
            restorePerApp: () => throw new InvalidOperationException("the daemon went away"),
            closeSession: _ => { closed = true; return Task.FromResult(true); },
            stopDaemon: () => throw new InvalidOperationException("so did its process"),
            warn: warnings.Add).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(closed);
        Assert.Contains(warnings, w => w.Contains("the daemon went away"));
        Assert.Contains(warnings, w => w.Contains("so did its process"));
    }

    /// <summary>
    /// A step whose task is already faulted is reported, not taken for success.
    /// </summary>
    /// <remarks>
    /// The throw above happens before a task exists. This is the other half, and the half the first
    /// version missed: an <c>async</c> step reports its failure through the task it returns, and a
    /// sequence that only compares which task won the race would treat that as having finished.
    /// </remarks>
    [Fact]
    public async Task AStepWhoseTaskIsFaulted_IsReportedRatherThanTakenForSuccess()
    {
        var warnings = new List<string>();
        var closed = false;

        await QuitSequence.RunAsync(
            restorePerApp: () => Task.FromException(new InvalidOperationException("faulted, not thrown")),
            closeSession: _ => { closed = true; return Task.FromResult(true); },
            stopDaemon: null,
            warn: warnings.Add).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(closed);
        Assert.Contains(warnings, w => w.Contains("faulted, not thrown"));
    }

    /// <summary>
    /// A fault that arrives after the step was abandoned is still observed.
    /// </summary>
    /// <remarks>
    /// Abandonment is the fallback, not cancellation: the step goes on running. Its exception would
    /// otherwise reach nobody, which on a task nothing awaits means it is lost entirely.
    /// </remarks>
    [Fact]
    public async Task AFaultArrivingAfterAStepWasAbandoned_IsStillObserved()
    {
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var warned = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await QuitSequence.RunAsync(
            restorePerApp: () => late.Task,
            closeSession: null,
            stopDaemon: null,
            warn: w => { if (w.Contains("abandoned")) warned.TrySetResult(w); }, budget: Short)
            .WaitAsync(Bound, TestContext.Current.CancellationToken);

        late.SetException(new InvalidOperationException("arrived late"));

        var message = await warned.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Contains("arrived late", message);
    }

    /// <summary>
    /// A close that gave up says so, rather than letting the exit look clean.
    /// </summary>
    [Fact]
    public async Task ACloseThatGaveUp_IsReported()
    {
        var warnings = new List<string>();

        await QuitSequence.RunAsync(
            restorePerApp: null,
            closeSession: _ => Task.FromResult(false),
            stopDaemon: null,
            warn: warnings.Add).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Contains(warnings, w => w.Contains("gave up"));
    }

    /// <summary>
    /// A close that throws is reported differently from one that gave up, and does not escape.
    /// </summary>
    [Fact]
    public async Task ACloseThatThrows_IsReportedAsAFailureRatherThanATimeout()
    {
        var warnings = new List<string>();

        await QuitSequence.RunAsync(
            restorePerApp: null,
            closeSession: _ => throw new InvalidOperationException("boom"),
            stopDaemon: null,
            warn: warnings.Add).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Contains(warnings, w => w.Contains("failed") && w.Contains("boom"));
        Assert.DoesNotContain(warnings, w => w.Contains("gave up"));
    }

    /// <summary>
    /// Nothing wired up is not a crash: a lifetime with no session quits.
    /// </summary>
    [Fact]
    public async Task WithNothingWiredUp_ItStillCompletes()
        => await QuitSequence.RunAsync(null, null, null, _ => { })
            .WaitAsync(Bound, TestContext.Current.CancellationToken);
}
