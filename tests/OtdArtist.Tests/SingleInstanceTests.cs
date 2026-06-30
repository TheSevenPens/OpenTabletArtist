using System;
using System.Threading;
using OtdArtist.Services;
using Xunit;

namespace OtdArtist.Tests;

public class SingleInstanceTests
{
    // Unique key per test so a real running app (or a parallel test) can't share our mutex/event.
    private static string Key() => "Test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void FirstInstance_IsPrimary()
    {
        using var first = new SingleInstance(Key());
        Assert.True(first.TryAcquire());
        Assert.True(first.IsPrimary);
    }

    [Fact]
    public void SecondInstance_IsNotPrimary_AndSignalsTheFirstToShow()
    {
        if (!OperatingSystem.IsWindows()) return; // the signalling mechanism is Windows-only

        var key = Key();
        using var first = new SingleInstance(key);
        Assert.True(first.TryAcquire());

        using var activated = new ManualResetEventSlim(false);
        first.ListenForActivation(() => activated.Set());

        using var second = new SingleInstance(key);
        Assert.False(second.TryAcquire()); // detects the primary
        Assert.False(second.IsPrimary);

        // The primary should have been woken to surface its window.
        Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
        second.Dispose();
    }

    [Fact]
    public void AfterPrimaryDisposed_NextInstanceBecomesPrimary()
    {
        if (!OperatingSystem.IsWindows()) return;

        var key = Key();
        var first = new SingleInstance(key);
        Assert.True(first.TryAcquire());
        first.Dispose(); // primary exits, releasing the mutex

        using var next = new SingleInstance(key);
        Assert.True(next.TryAcquire()); // ownership is available again
        Assert.True(next.IsPrimary);
    }
}
