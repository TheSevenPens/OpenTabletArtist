using System;
using System.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class StrokeSegmenterTests
{
    // Time is in milliseconds here; readings carry microseconds. X is just a label to tell readings apart.
    private static TabletReading Air(double ms, double x, double lean = 0) =>
        new((long)(ms * 1000), x, 0, 0, Height: 20, Lean: lean);

    private static TabletReading Down(double ms, double x, double pressure = 0.5) =>
        new((long)(ms * 1000), x, 0, pressure, Height: 0);

    private static SegmentedTake Seg(bool keep, int? stop, params TabletReading[] r) =>
        StrokeSegmenter.Segment(r, keep, stop);

    private static double[] Xs(System.Collections.Generic.IEnumerable<TabletReading> r) =>
        r.Select(x => x.X).ToArray();

    [Fact]
    public void ContactIsAnyPressureAboveZero()
    {
        var take = Seg(false, null, Down(0, 1, 0.0001), Down(1, 2, 0.0001), Air(2, 3));

        Assert.Equal(2, Assert.Single(take.Strokes).Readings.Count);
    }

    [Fact]
    public void AnApproachIsWhatFallsInsideTheQuarterSecondBeforeTheLanding()
    {
        var take = Seg(false, null, Air(0, 1), Air(100, 2), Air(300, 3), Air(400, 4), Down(500, 9));

        var stroke = Assert.Single(take.Strokes);

        // Landing at 500 ms, so the window opens at 250: only the readings at 300 and 400 are in it.
        Assert.Equal([3.0, 4.0], Xs(stroke.Approach));
        Assert.Equal(100_000, stroke.SinceLastSeenUs);
        Assert.Equal(
            [ReadingFate.ExcludedAirborne, ReadingFate.ExcludedAirborne, ReadingFate.KeptAlongside, ReadingFate.KeptAlongside, ReadingFate.Contact],
            take.Fates);
    }

    [Fact]
    public void AReadingExactlyAtTheEdgeOfTheWindowIsKept()
    {
        var take = Seg(false, null, Air(0, 1), Down(250, 9));

        Assert.Single(Assert.Single(take.Strokes).Approach);
    }

    [Fact]
    public void ALandingWithNoHoverBeforeItHasNoApproachAndNoLastSeen()
    {
        var stroke = Assert.Single(Seg(false, null, Down(0, 1)).Strokes);

        Assert.Empty(stroke.Approach);
        Assert.Null(stroke.SinceLastSeenUs);
    }

    [Fact]
    public void AnEmptyApproachStillSaysHowLongTheGapWas()
    {
        var stroke = Assert.Single(Seg(false, null, Air(0, 1), Down(2000, 9)).Strokes);

        Assert.Empty(stroke.Approach);
        Assert.Equal(2_000_000, stroke.SinceLastSeenUs);
    }

    [Fact]
    public void ARestingPenIsNotHeldAgainButAMovingOneIs()
    {
        var take = Seg(false, null, Air(0, 5), Air(1, 5), Air(2, 5), Air(3, 6), Air(4, 6), Down(5, 9));

        Assert.Equal([5.0, 6.0], Xs(Assert.Single(take.Strokes).Approach));
    }

    [Fact]
    public void LeanOrAzimuthKeepsAReadingWhateverThePositionDoes()
    {
        var take = Seg(false, null, Air(0, 5), Air(1, 5, lean: 12), Air(2, 5, lean: 12), Down(3, 9));

        Assert.Equal(3, Assert.Single(take.Strokes).Approach.Count);
    }

    [Fact]
    public void TheFirstHoverAfterALiftIsNotThatStrokesDepartureButLaterOnesAre()
    {
        var take = Seg(false, null, Down(0, 1), Down(1, 2), Air(2, 3), Air(3, 4), Air(4, 5));

        Assert.Equal([4.0, 5.0], Xs(Assert.Single(take.Strokes).Departure));
    }

    [Fact]
    public void ADepartureStopsAQuarterSecondAfterTheLastContactReading()
    {
        var take = Seg(false, null, Down(0, 1), Air(1, 2), Air(100, 3), Air(251, 4), Air(260, 5));

        Assert.Equal([3.0], Xs(Assert.Single(take.Strokes).Departure));
    }

    [Fact]
    public void ALiftsHoverAlsoFeedsTheNextLanding()
    {
        var take = Seg(false, null, Down(0, 1), Air(1, 2), Air(2, 3), Down(100, 8));

        Assert.Equal([3.0], Xs(take.Strokes[0].Departure));
        Assert.Equal([2.0, 3.0], Xs(take.Strokes[1].Approach));
        // One reading, one fate, though 3.0 sits in both.
        Assert.Equal(ReadingFate.KeptAlongside, take.Fates[2]);
        Assert.True(take.Ledger.Balances);
    }

    [Fact]
    public void ATakeRunsUntilItIsStopped()
    {
        var take = Seg(false, null,
            Down(0, 1), Down(1, 2), Air(2, 3), Down(500, 7), Down(501, 8), Air(502, 9), Down(1000, 11));

        Assert.Equal([2, 2, 1], take.Strokes.Select(s => s.Readings.Count));
        Assert.Equal(
            ["the pen lifted", "the pen lifted", "the recording was stopped mid-stroke"],
            take.Strokes.Select(s => s.EndedBy));
        Assert.Equal("the recording was stopped", take.EndedBy);
    }

    [Fact]
    public void ContactAfterTheStopIsCountedAndKeptOut()
    {
        // Stopped before index 4. The lift at index 2 is the stroke's end; what follows the stop is accounted for.
        var take = Seg(false, 4, Down(0, 1), Down(1, 2), Air(2, 3), Air(3, 4), Down(10, 5), Air(11, 6));

        var stroke = Assert.Single(take.Strokes);
        Assert.Equal(2, stroke.Readings.Count);
        Assert.Equal("the pen lifted", stroke.EndedBy);
        Assert.Equal([4.0, 6.0], Xs(stroke.Departure));   // hovering after the stop still feeds the departure

        Assert.Equal(
            new StrokeLedger(Routed: 6, Contact: 2, RetainedAirborne: 0, KeptAlongside: 2, ExcludedAirborne: 1, AfterTheStop: 1),
            take.Ledger);
    }

    [Fact]
    public void StoppingWhileTheTipIsDownEndsTheStrokeMidStroke()
    {
        var take = Seg(false, 2, Down(0, 1), Down(1, 2), Down(2, 3));

        var stroke = Assert.Single(take.Strokes);
        Assert.Equal("the recording was stopped mid-stroke", stroke.EndedBy);
        Assert.Equal(2, stroke.Readings.Count);
        Assert.Equal(1, take.Ledger.AfterTheStop);
    }

    [Fact]
    public void ARecordingWithNoContactIsSaidToHaveNothingDrawn()
    {
        var take = Seg(false, null, Air(0, 1), Air(1, 2));

        Assert.Empty(take.Strokes);
        Assert.Equal("the recording was stopped before anything was drawn", take.EndedBy);
        Assert.Equal(2, take.Ledger.ExcludedAirborne);
    }

    [Fact]
    public void KeepingAirborneRecordsEveryHoverAndLeavesNothingToBeAlongside()
    {
        var take = Seg(true, null, Air(0, 1), Air(1, 2), Down(2, 5), Down(3, 6), Air(4, 7));

        Assert.Equal([1.0, 2.0, 7.0], Xs(take.Aloft));
        Assert.Equal([1.0, 2.0], Xs(take.Strokes[0].Approach));   // copies of readings already in the airborne record
        Assert.Equal(
            new StrokeLedger(Routed: 5, Contact: 2, RetainedAirborne: 3, KeptAlongside: 0, ExcludedAirborne: 0, AfterTheStop: 0),
            take.Ledger);
    }

    [Fact]
    public void WithoutKeepingAirborneTheAloftRecordIsEmpty()
    {
        Assert.Empty(Seg(false, null, Air(0, 1), Down(1, 2), Air(2, 3)).Aloft);
    }

    [Fact]
    public void EveryReadingHasExactlyOneFateAndTheLedgerBalancesWhateverTheInput()
    {
        var rng = new Random(1234);

        foreach (var keep in new[] { false, true })
        {
            for (var trial = 0; trial < 200; trial++)
            {
                var n = rng.Next(0, 80);
                var ms = 0.0;
                var readings = new TabletReading[n];

                for (var i = 0; i < n; i++)
                {
                    ms += rng.NextDouble() < 0.1 ? rng.Next(100, 600) : rng.Next(1, 4);
                    readings[i] = rng.NextDouble() < 0.5 ? Down(ms, rng.Next(0, 4)) : Air(ms, rng.Next(0, 4), rng.Next(0, 3) == 0 ? 5 : 0);
                }

                var stop = rng.Next(0, 3) == 0 ? rng.Next(0, n + 1) : (int?)null;
                var take = StrokeSegmenter.Segment(readings, keep, stop);

                Assert.Equal(n, take.Fates.Length);
                Assert.True(take.Ledger.Balances);
                Assert.Equal(n, take.Ledger.Routed);
                Assert.Equal(take.Ledger.Contact, take.Strokes.Sum(s => s.Readings.Count));
                Assert.Equal(take.Ledger.RetainedAirborne, take.Aloft.Count);

                var airborne = readings.Count(r => !r.InContact);
                Assert.Equal(
                    airborne,
                    take.Ledger.RetainedAirborne + take.Ledger.KeptAlongside + take.Ledger.ExcludedAirborne);
                Assert.Equal(readings.Length - airborne, take.Ledger.Contact + take.Ledger.AfterTheStop);

                if (keep) Assert.Equal(0, take.Ledger.KeptAlongside + take.Ledger.ExcludedAirborne);
            }
        }
    }

    [Fact]
    public void HowLongAgoThePenWasLastSeenCountsAHoverThatDidNotMove()
    {
        // Hover at 0 ms, the same hover at 1000 ms, contact at 1001 ms. The pen was in the air 1 ms before it landed.
        // Measured from the moving-hover buffer (StrokeRecorder's way) this came out as 1001 ms.
        var stroke = Assert.Single(Seg(false, null, Air(0, 5), Air(1000, 5), Down(1001, 9)).Strokes);

        Assert.Equal(1_000, stroke.SinceLastSeenUs);
        Assert.Empty(stroke.Approach);   // which readings are kept as the approach is unchanged: the window is 250 ms
    }

    [Fact]
    public void TheLastSeenIntervalStartsOverAfterEachLanding()
    {
        var take = Seg(false, null, Air(0, 1), Down(10, 2), Down(11, 3), Down(700, 4));

        // The second contact run is part of the first stroke (no airborne reading in between), so one stroke.
        Assert.Single(take.Strokes);

        var two = Seg(false, null, Air(0, 1), Down(10, 2), Air(11, 3), Air(500, 3), Down(520, 4), Down(521, 5));
        Assert.Equal(20_000, two.Strokes[1].SinceLastSeenUs);   // 500 -> 520, not the 11 ms hover before the first lift
    }

    [Fact]
    public void EveryFateAgreesWithWhereTheReadingActuallyEndedUp()
    {
        // Each reading gets a unique Twist so identical measurements can be told apart, then the fate is checked
        // against membership in the output lists, independently of how the fate was computed.
        var rng = new Random(77);

        foreach (var keep in new[] { false, true })
        {
            for (var trial = 0; trial < 300; trial++)
            {
                var n = rng.Next(0, 90);
                var ms = 0.0;
                var readings = new TabletReading[n];

                for (var i = 0; i < n; i++)
                {
                    ms += rng.NextDouble() < 0.15 ? rng.Next(100, 700) : rng.Next(0, 4);
                    var at = (long)(ms * 1000);
                    readings[i] = rng.NextDouble() < 0.45
                        ? new TabletReading(at, rng.Next(3), 0, 100, Twist: i)
                        : new TabletReading(at, rng.Next(3), 0, 0, Lean: rng.Next(4) == 0 ? 5 : 0, Twist: i);
                }

                var stop = rng.Next(0, 3) == 0 ? rng.Next(0, n + 1) : (int?)null;
                var take = StrokeSegmenter.Segment(readings, keep, stop);

                var inStrokes = take.Strokes.SelectMany(s => s.Readings).Select(r => (int)r.Twist).ToHashSet();
                var alongside = take.Strokes.SelectMany(s => s.Approach.Concat(s.Departure)).Select(r => (int)r.Twist).ToHashSet();
                var aloft = take.Aloft.Select(r => (int)r.Twist).ToHashSet();

                for (var i = 0; i < n; i++)
                {
                    var expected = readings[i].InContact
                        ? (inStrokes.Contains(i) ? ReadingFate.Contact : ReadingFate.AfterTheStop)
                        : aloft.Contains(i) ? ReadingFate.RetainedAirborne
                        : alongside.Contains(i) ? ReadingFate.KeptAlongside
                        : ReadingFate.ExcludedAirborne;

                    Assert.Equal(expected, take.Fates[i]);
                }

                Assert.Equal(keep ? readings.Count(r => !r.InContact) : 0, aloft.Count);
                Assert.Empty(inStrokes.Intersect(alongside));
                Assert.Empty(inStrokes.Intersect(aloft));
            }
        }
    }

    [Fact]
    public void AStopOutsideTheRunIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Seg(false, 3, Down(0, 1)));
    }
}
