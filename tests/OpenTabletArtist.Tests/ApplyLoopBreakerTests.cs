using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ApplyLoopBreakerTests
{
    [Fact]
    public void AllowsUpToThreshold_ThenTripsWithinTheWindow()
    {
        var b = new ApplyLoopBreaker(threshold: 5, windowMs: 1000);
        long t = 10_000;

        for (int i = 0; i < 5; i++)
            Assert.True(b.Allow(t + i));   // 5 rapid applies within the window are allowed

        Assert.False(b.Allow(t + 5));      // the 6th is a runaway → denied
        Assert.True(b.IsTripped);
    }

    [Fact]
    public void ReArms_AfterAGapLongerThanTheWindow()
    {
        var b = new ApplyLoopBreaker(threshold: 3, windowMs: 1000);

        for (int i = 0; i < 3; i++) b.Allow(i);
        Assert.False(b.Allow(3));          // tripped
        Assert.True(b.IsTripped);

        // A gap beyond the window means the burst ended — the breaker re-arms.
        Assert.True(b.Allow(3 + 1001));
        Assert.False(b.IsTripped);
    }

    [Fact]
    public void SlowSteadyRate_NeverTrips()
    {
        var b = new ApplyLoopBreaker(threshold: 10, windowMs: 1000);

        // One apply every 500ms (2/sec) — far under the threshold rate — never trips.
        for (int i = 0; i < 20; i++)
            Assert.True(b.Allow(i * 500L));

        Assert.False(b.IsTripped);
    }
}
