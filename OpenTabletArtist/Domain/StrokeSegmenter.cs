using System;
using System.Collections.Generic;

namespace OpenTabletArtist.Domain;

/// <summary>
/// One pen report as the stroke recorder keeps it: tablet-space position, the host clock, and the
/// channels the stroke-field-guide format carries. <see cref="ArrivedUs"/> is a monotonic host clock in
/// microseconds (the recorder policy ages approach and departure on it, never on a pen timestamp).
/// <see cref="Pressure"/> is the device's own count, not normalized: only "above zero" matters to the
/// segmenter, and the file stores the count.
/// </summary>
public readonly record struct TabletReading(
    long ArrivedUs,
    double X,
    double Y,
    double Pressure,
    int? Height = null,
    double Lean = 0,
    double Azimuth = 0,
    double Twist = 0)
{
    /// <summary>The tip is down. The recorder's definition, kept as is: any pressure above zero.</summary>
    public bool InContact => Pressure > 0;
}

/// <summary>What became of one reading. Exactly one per reading.</summary>
public enum ReadingFate
{
    /// <summary>The tip was down and it went into a stroke.</summary>
    Contact,

    /// <summary>Airborne, and kept in the take's airborne record because that was asked for.</summary>
    RetainedAirborne,

    /// <summary>Airborne, not kept in the airborne record, but held beside a stroke as its approach or departure.</summary>
    KeptAlongside,

    /// <summary>Airborne, and deliberately left out.</summary>
    ExcludedAirborne,

    /// <summary>The tip touched down after the recording had been stopped.</summary>
    AfterTheStop,
}

/// <summary>One contact, tip down to tip up, with the pen in the air either side of it.</summary>
public sealed record SegmentedStroke(
    IReadOnlyList<TabletReading> Approach,
    IReadOnlyList<TabletReading> Readings,
    IReadOnlyList<TabletReading> Departure,
    long? SinceLastSeenUs,
    string EndedBy);

/// <summary>
/// Every reading the segmenter was handed, counted by what became of it. The columns add up to
/// <see cref="Routed"/>: "left out" is not "lost", so a reading that is in none of them is a fault.
/// </summary>
public sealed record StrokeLedger(
    int Routed,
    int Contact,
    int RetainedAirborne,
    int KeptAlongside,
    int ExcludedAirborne,
    int AfterTheStop)
{
    public int Accounted => Contact + RetainedAirborne + KeptAlongside + ExcludedAirborne + AfterTheStop;

    public bool Balances => Accounted == Routed;
}

public sealed record SegmentedTake(
    IReadOnlyList<SegmentedStroke> Strokes,
    IReadOnlyList<TabletReading> Aloft,
    StrokeLedger Ledger,
    ReadingFate[] Fates,
    string EndedBy,
    bool KeptAirborne);

/// <summary>
/// Cuts a recorded run of pen reports into the strokes, approaches, departures and airborne record that
/// the stroke-field-guide format stores, and accounts for every reading.
/// </summary>
/// <remarks>
/// <para>
/// This is StrokeRecorder's capture policy (<c>Capturing.cs</c>) for a many-stroke take, applied after the
/// fact to a list instead of reading by reading, so the same pen gives the same file from either tool:
/// contact is pressure above zero; an approach is the hovering readings within a quarter second
/// (<see cref="HoverKept"/>, host clock) before a landing; a departure is the hovering readings within a
/// quarter second after the stroke's last contact reading; a hovering reading is only held as
/// approach/departure if it moved (or carries lean or azimuth); a take runs until stopped.
/// </para>
/// <para>
/// Two behaviours of the original are kept on purpose because they are part of what the files contain: the
/// first hovering reading after a lift is not that stroke's departure (the stroke is still "drawing" when it
/// arrives) but is eligible for the next approach, and a departure reading also stays eligible for the next
/// approach. There is no pad region in OpenTabletArtist, so the recorder's off-pad outcome never occurs and
/// has no column here.
/// </para>
/// <para>
/// One deliberate difference: the original reports "left out" as a raw count minus the number of
/// approach/departure slots, which under-reports when one reading is both a departure and the next approach.
/// Here each reading has one <see cref="ReadingFate"/>, so the columns are exact.
/// </para>
/// </remarks>
public static class StrokeSegmenter
{
    /// <summary>How long a hovering reading is kept as a possible approach or departure, in microseconds.</summary>
    public const long HoverKept = 250_000;

    private enum Phase { Armed, Drawing, Between, Taken }

    /// <param name="readings">Every report from arming to the end, in arrival order.</param>
    /// <param name="keepAirborne">Keep every airborne reading in the take's airborne record.</param>
    /// <param name="stoppedAt">
    /// Index of the first reading that arrived after the recording was stopped; null if it never was (the
    /// run is then treated as stopped after its last reading, which only matters for the take's ending).
    /// </param>
    public static SegmentedTake Segment(IReadOnlyList<TabletReading> readings, bool keepAirborne, int? stoppedAt = null)
    {
        var stop = stoppedAt ?? readings.Count;
        if (stop < 0 || stop > readings.Count) throw new ArgumentOutOfRangeException(nameof(stoppedAt));

        var fates = new ReadingFate[readings.Count];
        var aloft = new List<TabletReading>();
        var hover = new List<int>();               // indices of recent hovering readings that moved
        var strokes = new List<Open>();
        var phase = Phase.Armed;

        for (var i = 0; i < readings.Count; i++)
        {
            var r = readings[i];

            if (i == stop)
            {
                if (phase == Phase.Drawing) strokes[^1].EndedBy = "the recording was stopped mid-stroke";
                phase = Phase.Taken;
            }

            if (!r.InContact)
            {
                if (keepAirborne)
                {
                    aloft.Add(r);
                    fates[i] = ReadingFate.RetainedAirborne;
                }
                else
                {
                    fates[i] = ReadingFate.ExcludedAirborne;
                }

                if (Moved(readings, hover, r)) Hovering(readings, hover, strokes, phase, i, keepAirborne, fates);

                if (phase == Phase.Drawing) phase = Phase.Between;

                continue;
            }

            switch (phase)
            {
                case Phase.Armed:
                case Phase.Between:
                    strokes.Add(Land(readings, hover, i, keepAirborne, fates));
                    strokes[^1].Readings.Add(r);
                    fates[i] = ReadingFate.Contact;
                    phase = Phase.Drawing;
                    break;

                case Phase.Drawing:
                    strokes[^1].Readings.Add(r);
                    fates[i] = ReadingFate.Contact;
                    break;

                default:
                    fates[i] = ReadingFate.AfterTheStop;
                    break;
            }
        }

        if (stop == readings.Count && phase == Phase.Drawing)
        {
            strokes[^1].EndedBy = "the recording was stopped mid-stroke";
        }

        var done = new List<SegmentedStroke>(strokes.Count);
        foreach (var s in strokes)
        {
            done.Add(new SegmentedStroke(s.Approach, s.Readings, s.Departure, s.SinceLastSeenUs, s.EndedBy));
        }

        var ledger = new StrokeLedger(
            Routed: readings.Count,
            Contact: Count(fates, ReadingFate.Contact),
            RetainedAirborne: Count(fates, ReadingFate.RetainedAirborne),
            KeptAlongside: Count(fates, ReadingFate.KeptAlongside),
            ExcludedAirborne: Count(fates, ReadingFate.ExcludedAirborne),
            AfterTheStop: Count(fates, ReadingFate.AfterTheStop));

        return new SegmentedTake(
            done,
            aloft,
            ledger,
            fates,
            done.Count == 0 ? "the recording was stopped before anything was drawn" : "the recording was stopped",
            keepAirborne);
    }

    private sealed class Open
    {
        public List<TabletReading> Approach { get; } = [];
        public List<TabletReading> Readings { get; } = [];
        public List<TabletReading> Departure { get; } = [];
        public long? SinceLastSeenUs { get; set; }
        public string EndedBy { get; set; } = "the pen lifted";
    }

    /// <summary>A pen resting in range repeats its position; lean or azimuth are kept whatever they say.</summary>
    private static bool Moved(IReadOnlyList<TabletReading> all, List<int> hover, TabletReading r) =>
        r.Lean != 0
        || r.Azimuth != 0
        || hover.Count == 0
        || all[hover[^1]].X != r.X
        || all[hover[^1]].Y != r.Y;

    private static void Hovering(
        IReadOnlyList<TabletReading> all, List<int> hover, List<Open> strokes, Phase phase, int i,
        bool keepAirborne, ReadingFate[] fates)
    {
        var r = all[i];
        hover.Add(i);

        while (hover.Count > 0 && r.ArrivedUs - all[hover[0]].ArrivedUs > HoverKept)
        {
            hover.RemoveAt(0);
        }

        if (phase is not (Phase.Between or Phase.Taken)) return;
        if (strokes.Count == 0 || strokes[^1].Readings.Count == 0) return;

        var just = strokes[^1];
        if (r.ArrivedUs - just.Readings[^1].ArrivedUs > HoverKept) return;

        just.Departure.Add(r);
        Adopt(i, keepAirborne, fates);
    }

    private static Open Land(IReadOnlyList<TabletReading> all, List<int> hover, int i, bool keepAirborne, ReadingFate[] fates)
    {
        var landing = all[i];
        var open = new Open();

        // The approach is a view of readings already counted: the ones that would otherwise be left out
        // are re-labelled as kept alongside, so each reading still has exactly one fate.
        foreach (var h in hover)
        {
            if (landing.ArrivedUs - all[h].ArrivedUs > HoverKept) continue;

            open.Approach.Add(all[h]);
            Adopt(h, keepAirborne, fates);
        }

        open.SinceLastSeenUs = hover.Count > 0 ? landing.ArrivedUs - all[hover[^1]].ArrivedUs : null;

        hover.Clear();
        return open;
    }

    private static void Adopt(int i, bool keepAirborne, ReadingFate[] fates)
    {
        if (!keepAirborne && fates[i] == ReadingFate.ExcludedAirborne) fates[i] = ReadingFate.KeptAlongside;
    }

    private static int Count(ReadingFate[] fates, ReadingFate fate)
    {
        var n = 0;
        foreach (var f in fates) if (f == fate) n++;
        return n;
    }
}
