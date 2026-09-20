using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What quitting does, in what order (#167, #596, #828).
/// </summary>
///
/// <remarks>
/// The order is the whole of it, and it is the kind that fails quietly: each step needs the connection
/// the next one takes away, so running them the wrong way round does not throw — the step simply does
/// nothing, and the snapshot stays applied or the write never reaches disk. None of it was reachable
/// while it lived in the tray, which needs a real <c>TrayIcon</c> and a desktop lifetime.
/// </remarks>
public class QuitSequenceTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The per-app restore, the daemon stop and the session close happen in that order.
    /// </summary>
    [Fact]
    public async Task TheStepsRunInTheOrderEachOneNeeds()
    {
        var order = new List<string>();

        await QuitSequence.RunAsync(
            restorePerApp: () => { order.Add("restore"); return Task.CompletedTask; },
            stopDaemon: () => { order.Add("stop"); return Task.CompletedTask; },
            closeSession: _ => { order.Add("close"); return Task.FromResult(true); },
            warn: _ => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "restore", "stop", "close" }, order);
    }

    /// <summary>
    /// The session is closed last, after the steps that need the connection it takes away.
    /// </summary>
    /// <remarks>
    /// Stated separately from the ordering above because it is the one this change introduced, and the
    /// one whose absence is silent: closing first leaves the restore talking to a transport that has gone.
    /// </remarks>
    [Fact]
    public async Task TheSessionIsClosedAfterTheStepsThatNeedIt()
    {
        var closed = false;
        var restoredWhileOpen = false;
        var stoppedWhileOpen = false;

        await QuitSequence.RunAsync(
            restorePerApp: () => { restoredWhileOpen = !closed; return Task.CompletedTask; },
            stopDaemon: () => { stoppedWhileOpen = !closed; return Task.CompletedTask; },
            closeSession: _ => { closed = true; return Task.FromResult(true); },
            warn: _ => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(restoredWhileOpen);
        Assert.True(stoppedWhileOpen);
        Assert.True(closed);
    }

    /// <summary>
    /// A step that hangs is given up on, and the ones after it still run.
    /// </summary>
    /// <remarks>
    /// The reason every step is bounded: quitting has to work against a daemon that has stopped
    /// answering. A restore that never returns must not cost the session its chance to close.
    /// </remarks>
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
                stopDaemon: null,
                closeSession: _ => { closed = true; return Task.FromResult(true); },
                warn: warnings.Add).WaitAsync(Bound, TestContext.Current.CancellationToken);

            Assert.True(closed);
            Assert.Contains(warnings, w => w.Contains("Per-app restore"));
        }
        finally
        {
            never.TrySetResult();
        }
    }

    /// <summary>
    /// A step that throws does not stop the ones after it either.
    /// </summary>
    [Fact]
    public async Task AStepThatThrows_DoesNotStopTheOnesAfterIt()
    {
        var closed = false;

        await QuitSequence.RunAsync(
            restorePerApp: () => throw new InvalidOperationException("the daemon went away"),
            stopDaemon: () => throw new InvalidOperationException("so did its process"),
            closeSession: _ => { closed = true; return Task.FromResult(true); },
            warn: _ => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(closed);
    }

    /// <summary>
    /// A close that gave up says so, rather than letting the exit look clean.
    /// </summary>
    /// <remarks>
    /// False does not mean nothing happened: the write may have reached the daemon, and may have reached
    /// disk. What it means is that nothing here knows, which is worth a line in the log.
    /// </remarks>
    [Fact]
    public async Task ACloseThatGaveUp_IsReported()
    {
        var warnings = new List<string>();

        await QuitSequence.RunAsync(
            restorePerApp: null,
            stopDaemon: null,
            closeSession: _ => Task.FromResult(false),
            warn: warnings.Add).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Contains(warnings, w => w.Contains("gave up"));
    }

    /// <summary>
    /// A close that throws is reported differently from one that gave up, and does not escape.
    /// </summary>
    /// <remarks>
    /// The two steps before this swallow everything, because a stuck daemon is ordinary. A close that
    /// throws is a defect rather than a stuck daemon — so it is caught, because an exception here would
    /// leave the application running with the window already allowed to close, but it is reported as
    /// itself rather than folded into the timeout message.
    /// </remarks>
    [Fact]
    public async Task ACloseThatThrows_IsReportedAsAFailureRatherThanATimeout()
    {
        var warnings = new List<string>();

        await QuitSequence.RunAsync(
            restorePerApp: null,
            stopDaemon: null,
            closeSession: _ => throw new InvalidOperationException("boom"),
            warn: warnings.Add).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Contains(warnings, w => w.Contains("failed") && w.Contains("boom"));
        Assert.DoesNotContain(warnings, w => w.Contains("gave up"));
    }

    /// <summary>
    /// The close gets this application's window, not the library's default.
    /// </summary>
    [Fact]
    public async Task TheCloseIsGivenTheApplicationsOwnWindow()
    {
        TimeSpan? given = null;

        await QuitSequence.RunAsync(
            restorePerApp: null,
            stopDaemon: null,
            closeSession: w => { given = w; return Task.FromResult(true); },
            warn: _ => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(QuitSequence.StepBound, given);
        Assert.NotEqual(TimeSpan.Zero, given);
        Assert.NotEqual(System.Threading.Timeout.InfiniteTimeSpan, given);
    }

    /// <summary>
    /// Nothing wired up is not a crash: a lifetime with no session quits.
    /// </summary>
    [Fact]
    public async Task WithNothingWiredUp_ItStillCompletes()
        => await QuitSequence.RunAsync(null, null, null, _ => { })
            .WaitAsync(Bound, TestContext.Current.CancellationToken);
}
