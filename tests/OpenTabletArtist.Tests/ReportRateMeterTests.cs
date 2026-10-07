using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ReportRateMeterTests
{
    private static ReportRateMeter Feed(double periodMs, int count, double startMs = 0)
    {
        var m = new ReportRateMeter();
        for (var i = 0; i < count; i++) m.Record(startMs + i * periodMs);
        return m;
    }

    [Fact]
    public void NoRate_UntilTwoReports()
    {
        var m = new ReportRateMeter();
        Assert.Null(m.Hz);
        m.Record(0);
        Assert.Null(m.Hz);
        m.Record(8);
        Assert.Equal(125.0, m.Hz); // 8 ms apart
    }

    [Theory]
    [InlineData(1.0, 1000.0)]   // a 1000 Hz modified-firmware tablet
    [InlineData(4.0, 250.0)]
    [InlineData(7.5, 133.0)]    // 133.33 → 133
    public void SteadyStream_ReportsItsRate(double periodMs, double expectedHz)
        => Assert.Equal(expectedHz, Feed(periodMs, 2000).Hz);

    [Fact]
    public void OnlyTheTrailingWindowCounts()
    {
        // 1 s at 500 Hz, then 1 s at 1000 Hz: once the window has rolled past the slow part, it reads 1000.
        var m = Feed(2.0, 500);
        for (var i = 0; i < 1500; i++) m.Record(1000 + i * 1.0);
        Assert.Equal(1000.0, m.Hz);
    }

    [Fact]
    public void JitteryReports_StillReadTheTrueRate()
    {
        // Alternating 0.5 / 1.5 ms gaps — a single-report average would flicker; the window count doesn't.
        var m = new ReportRateMeter();
        double t = 0;
        for (var i = 0; i < 4000; i++) { m.Record(t); t += i % 2 == 0 ? 0.5 : 1.5; }
        Assert.Equal(1000.0, m.Hz);
    }

    [Fact]
    public void Pause_RestartsTheWindow()
    {
        var m = Feed(8.0, 50);               // 125 Hz
        m.Record(5000);                       // pen came back after a long gap
        Assert.Null(m.Hz);                    // no rate yet — the old window didn't leak in
        m.Record(5005);
        Assert.Equal(200.0, m.Hz);            // 5 ms apart
    }

    [Fact]
    public void Reset_ForgetsHistory()
    {
        var m = Feed(8.0, 10);
        m.Reset();
        Assert.Null(m.Hz);
        m.Record(10);
        Assert.Null(m.Hz);
    }
}
