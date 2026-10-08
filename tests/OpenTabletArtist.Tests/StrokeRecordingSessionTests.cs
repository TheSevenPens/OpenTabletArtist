using System;
using System.Diagnostics;
using System.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class StrokeRecordingSessionTests
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;

    private static PenSample Report(
        double ms, double rawX = 100, double rawPressure = 0, double tiltX = 0, double tiltY = 0,
        int? hover = 10, bool hasTilt = true, long? ticks = null) =>
        new(0, 0, rawX, rawX * 2, rawPressure / 1023.0, tiltX, tiltY, 0, rawPressure > 0, hover,
            Timestamp: ticks ?? 5_000_000 + (long)(ms * Ms), RawPressure: rawPressure, HasTilt: hasTilt);

    [Fact]
    public void ReportsBecomeReadingsInTheTabletsOwnUnitsWithArrivalsFromTheFirst()
    {
        var session = new StrokeRecordingSession(1023);

        session.Add(Report(0, rawX: 100, rawPressure: 0));
        session.Add(Report(2, rawX: 101, rawPressure: 300));

        var take = session.Segment(keepAirborne: false);
        var reading = Assert.Single(take.Strokes).Readings[0];

        Assert.Equal(2000, reading.ArrivedUs);
        Assert.Equal(101, reading.X);
        Assert.Equal(202, reading.Y);
        Assert.Equal(300, reading.Pressure);   // the device's count, not 300/1023
        Assert.Equal(10, reading.Height);
    }

    [Fact]
    public void TiltBecomesLeanAndAzimuthByTheAppsExistingConvention()
    {
        var session = new StrokeRecordingSession(1023);
        session.Add(Report(0, rawPressure: 100, tiltX: 3, tiltY: 4));

        var reading = session.Segment(false).Strokes[0].Readings[0];

        Assert.Equal(5.0, reading.Lean, 6);                                       // hypot(3, 4) degrees off vertical
        Assert.Equal(DiagnosticsMath.TiltAzimuthDegrees(3, 4), reading.Azimuth, 6);
        Assert.Equal(0, reading.Twist);
    }

    [Fact]
    public void AHandBuiltSampleWithoutRawPressureFallsBackToTheFractionOfFullScale()
    {
        var session = new StrokeRecordingSession(1000);
        session.Add(new PenSample(0, 0, 1, 2, 0.25, 0, 0, 0, true, 5, Timestamp: 100));

        Assert.Equal(250, session.Segment(false).Strokes[0].Readings[0].Pressure);
    }

    [Fact]
    public void AnUnstampedSampleIsStampedWhenItArrives()
    {
        var now = 1000L;
        var session = new StrokeRecordingSession(1023, () => now);

        session.Add(new PenSample(0, 0, 1, 2, 0, 0, 0, 0, false, 5));
        now += 3 * Ms;
        session.Add(new PenSample(0, 0, 1, 2, 0, 0, 0, 0, false, 5));

        Assert.Equal(3.0 / 1000, session.Seconds, 6);
    }

    [Fact]
    public void AChannelSomeReportsLackedIsLeftOutNotZeroed()
    {
        var session = new StrokeRecordingSession(1023);
        session.Add(Report(0, rawPressure: 100));
        Assert.Equal(new TakeChannels(Height: true, Tilt: true, Twist: false), session.Channels);

        session.Add(Report(1, rawPressure: 100, hover: null));
        Assert.False(session.Channels.Height);
        Assert.True(session.Channels.Tilt);

        session.Add(Report(2, rawPressure: 100, hasTilt: false));
        Assert.False(session.Channels.Tilt);
    }

    [Fact]
    public void StrokesAreCountedAsContactRunsAndStopFreezesTheCount()
    {
        var session = new StrokeRecordingSession(1023);
        session.Add(Report(0));
        session.Add(Report(1, rawPressure: 10));
        session.Add(Report(2, rawPressure: 20));
        session.Add(Report(3));
        session.Add(Report(4, rawPressure: 10));
        Assert.Equal(2, session.StrokeCount);

        session.Stop();
        session.Add(Report(5));
        session.Add(Report(6, rawPressure: 10));

        Assert.Equal(2, session.StrokeCount);
        Assert.Equal(2, session.Segment(false).Strokes.Count);
    }

    [Fact]
    public void ReportsAfterTheStopStillCountAndLandInTheLedger()
    {
        var session = new StrokeRecordingSession(1023);
        session.Add(Report(0, rawPressure: 10));
        session.Add(Report(1));
        session.Stop();
        session.Add(Report(2, rawPressure: 10));
        session.Add(Report(3));

        var ledger = session.Segment(false).Ledger;

        Assert.True(session.IsStopped);
        Assert.Equal(4, ledger.Routed);
        Assert.Equal(1, ledger.AfterTheStop);
        Assert.True(ledger.Balances);
    }

    [Fact]
    public void StoppingTwiceKeepsTheFirstStop()
    {
        var session = new StrokeRecordingSession(1023);
        session.Add(Report(0, rawPressure: 10));
        session.Stop();
        session.Add(Report(1, rawPressure: 10));
        session.Stop();

        Assert.Equal(1, session.Segment(false).Ledger.AfterTheStop);
    }

    [Fact]
    public void ARecordingStopsGrowingAtTheCapAndSaysSo()
    {
        var session = new StrokeRecordingSession(1023);

        for (var i = 0; i < StrokeRecordingSession.MaxReadings + 5; i++)
        {
            session.Add(Report(0, rawPressure: 0, ticks: 1000 + i));
        }

        Assert.Equal(StrokeRecordingSession.MaxReadings, session.Count);
        Assert.True(session.Truncated);
    }

    [Fact]
    public void ChoosingToKeepAirborneAfterwardsChangesTheFileNotTheReports()
    {
        var session = new StrokeRecordingSession(1023);
        session.Add(Report(0, rawX: 1));
        session.Add(Report(1, rawX: 2, rawPressure: 10));
        session.Add(Report(500, rawX: 3));

        Assert.Empty(session.Segment(false).Aloft);
        Assert.Equal(2, session.Segment(true).Aloft.Count);
    }
}
