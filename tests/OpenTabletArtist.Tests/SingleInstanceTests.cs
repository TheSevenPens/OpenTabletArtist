using System;
using System.Threading;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

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
        // Signalling is implemented on Windows (named event) and Linux (Unix socket); macOS is a no-op.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;

        var key = Key();
        using var first = new SingleInstance(key);
        Assert.True(first.TryAcquire());

        using var activated = new ManualResetEventSlim(false);
        first.ListenForActivation(() => activated.Set());

        using var second = new SingleInstance(key);
        Assert.False(second.TryAcquire()); // detects the primary
        Assert.False(second.IsPrimary);

        // The primary should have been woken to surface its window. A working signal arrives in
        // milliseconds; the generous timeout only bites on a genuine failure and absorbs ThreadPool
        // scheduling delay on loaded CI runners (the activation callback runs via
        // RegisterWaitForSingleObject), which previously flaked this test and blocked releases (#222).
        Assert.True(activated.Wait(TimeSpan.FromSeconds(30)));
        second.Dispose();
    }

    [Fact]
    public void AfterPrimaryDisposed_NextInstanceBecomesPrimary()
    {
        // The claim (Windows mutex / Linux lock file) is released on Dispose; macOS is always primary.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;

        var key = Key();
        var first = new SingleInstance(key);
        Assert.True(first.TryAcquire());
        first.Dispose(); // primary exits, releasing the mutex

        using var next = new SingleInstance(key);
        Assert.True(next.TryAcquire()); // ownership is available again
        Assert.True(next.IsPrimary);
    }
}
