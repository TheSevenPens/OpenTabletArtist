using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class QuitSequenceTests
{
    [Fact]
    public async Task CloseFinishesBeforeStoppingTheCapturedDaemon()
    {
        var close = new TaskCompletionSource<bool>();
        var stopped = false;
        var task = QuitSequence.RunAsync(_ => close.Task,
            () => { stopped = true; return Task.CompletedTask; }, _ => { });
        Assert.False(stopped);
        close.SetResult(true);
        await task;
        Assert.True(stopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseFailureIsReportedAndStillStops(bool throws)
    {
        var warnings = new List<string>();
        var stopped = false;
        await QuitSequence.RunAsync(_ => throws ? Task.FromException<bool>(new Exception("broken"))
                : Task.FromResult(false),
            () => { stopped = true; return Task.CompletedTask; }, warnings.Add);
        Assert.True(stopped);
        Assert.Single(warnings);
        Assert.Contains(throws ? "failed" : "gave up", warnings[0]);
    }

    [Fact]
    public async Task StopIsBoundedAndLateFaultIsObserved()
    {
        var stop = new TaskCompletionSource();
        var warned = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await QuitSequence.RunAsync(null, () => stop.Task,
            message => { if (message.Contains("abandoned")) warned.TrySetResult(message); },
            TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stop.SetException(new Exception("late failure"));
        Assert.Contains("late failure", await warned.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoCallbacksCompletes() => await QuitSequence.RunAsync(null, null, _ => { });
}
