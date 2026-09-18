using System;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletArtist.Concurrency;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Covers the trailing debounce the tablet editor uses for its wheel, active-area, dynamics and hover
/// edits (#736). The disposal cases are the point: four hand-rolled copies of this pattern were never
/// cancelled when the editor was disposed, so a closed editor could still push settings 250–400 ms later.
/// </summary>
public class TrailingDebounceTests
{
    // Short enough to keep the suite fast, long enough that the "did not run" assertions are not
    // racing the scheduler on a loaded machine.
    private const int Delay = 40;
    private const int PastDelay = Delay * 6;

    [Fact]
    public async Task Schedule_RunsTheWorkAfterTheQuietPeriod()
    {
        using var debounce = new TrailingDebounce(Delay, "test");
        var ran = new TaskCompletionSource();

        debounce.Schedule(() => { ran.TrySetResult(); return Task.CompletedTask; });

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Schedule_DoesNotRunBeforeTheQuietPeriodElapses()
    {
        using var debounce = new TrailingDebounce(2000, "test");
        var runs = 0;

        debounce.Schedule(() => { Interlocked.Increment(ref runs); return Task.CompletedTask; });
        await Task.Delay(PastDelay);

        Assert.Equal(0, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task Schedule_SupersedesThePreviousCall_OnlyTheLastRuns()
    {
        using var debounce = new TrailingDebounce(Delay, "test");
        var runs = 0;
        var last = 0;

        for (var i = 1; i <= 5; i++)
        {
            var n = i;
            debounce.Schedule(() =>
            {
                Interlocked.Increment(ref runs);
                Volatile.Write(ref last, n);
                return Task.CompletedTask;
            });
        }
        await Task.Delay(PastDelay);

        Assert.Equal(1, Volatile.Read(ref runs));
        Assert.Equal(5, Volatile.Read(ref last));
    }

    /// <summary>The regression this class exists for: a disposed owner must not have its pending edit
    /// wake up and write settings at a session that is being torn down.</summary>
    [Fact]
    public async Task Dispose_CancelsPendingWork()
    {
        var debounce = new TrailingDebounce(Delay, "test");
        var runs = 0;

        debounce.Schedule(() => { Interlocked.Increment(ref runs); return Task.CompletedTask; });
        debounce.Dispose();
        await Task.Delay(PastDelay);

        Assert.Equal(0, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task Schedule_AfterDispose_IsIgnored()
    {
        var debounce = new TrailingDebounce(Delay, "test");
        debounce.Dispose();
        var runs = 0;

        debounce.Schedule(() => { Interlocked.Increment(ref runs); return Task.CompletedTask; });
        await Task.Delay(PastDelay);

        Assert.Equal(0, Volatile.Read(ref runs));
        Assert.True(debounce.IsDisposed);
        Assert.False(debounce.IsPending);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var debounce = new TrailingDebounce(Delay, "test");
        debounce.Schedule(() => Task.CompletedTask);

        debounce.Dispose();
        debounce.Dispose();

        Assert.True(debounce.IsDisposed);
    }

    [Fact]
    public async Task CancelPending_DropsTheEdit_ButKeepsTheDebounceUsable()
    {
        using var debounce = new TrailingDebounce(Delay, "test");
        var cancelled = 0;

        debounce.Schedule(() => { Interlocked.Increment(ref cancelled); return Task.CompletedTask; });
        debounce.CancelPending();
        await Task.Delay(PastDelay);
        Assert.Equal(0, Volatile.Read(ref cancelled));

        var ran = new TaskCompletionSource();
        debounce.Schedule(() => { ran.TrySetResult(); return Task.CompletedTask; });
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>The work is fire-and-forget, so a throw has nowhere to surface. It must be swallowed
    /// (and logged) rather than becoming an unobserved task exception, and must not wedge the debounce.</summary>
    [Fact]
    public async Task WorkThatThrows_DoesNotBreakTheDebounce()
    {
        using var debounce = new TrailingDebounce(Delay, "test");
        var threw = new TaskCompletionSource();

        debounce.Schedule(() =>
        {
            threw.TrySetResult();
            throw new InvalidOperationException("boom");
        });
        await threw.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var ran = new TaskCompletionSource();
        debounce.Schedule(() => { ran.TrySetResult(); return Task.CompletedTask; });
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IsPending_TracksTheOutstandingEdit()
    {
        using var debounce = new TrailingDebounce(Delay, "test");
        Assert.False(debounce.IsPending);

        var ran = new TaskCompletionSource();
        debounce.Schedule(() => { ran.TrySetResult(); return Task.CompletedTask; });
        Assert.True(debounce.IsPending);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        debounce.CancelPending();
        Assert.False(debounce.IsPending);
    }
}
