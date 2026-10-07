using System;
using System.Collections.Generic;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Live tablet report rate (reports per second) for the scribble page's readout, from the reports'
/// own timestamps. It counts the reports that arrived in the trailing <see cref="WindowMs"/> and divides by
/// the time they span — so it is a true reports-per-second figure that doesn't hang on any single report's
/// timing, which is what keeps it steady at 1000 Hz where a per-report average blurs.
/// A pause longer than <see cref="PauseThresholdMs"/> (pen out of range) restarts the window, so a returning
/// pen isn't averaged against the silence.
/// </summary>
public sealed class ReportRateMeter
{
    /// <summary>How much recent history the rate is counted over.</summary>
    public const double WindowMs = 1000;

    /// <summary>A gap longer than this (ms) is a pause, not a slow report — the window restarts.</summary>
    public const double PauseThresholdMs = 250;

    private readonly Queue<double> _times = new();
    private double _newest;

    /// <summary>The rate in Hz over the current window, or null until two reports have arrived close together.
    /// Holds its last value while the pen is silent (it is only recomputed by <see cref="Record"/>).</summary>
    public double? Hz { get; private set; }

    /// <summary>Records a report that arrived at <paramref name="timeMs"/> (any monotonic clock, in ms).</summary>
    public void Record(double timeMs)
    {
        if (_times.Count > 0 && timeMs - _newest > PauseThresholdMs)
        {
            _times.Clear();
            Hz = null;
        }

        _times.Enqueue(timeMs);
        _newest = timeMs;
        while (timeMs - _times.Peek() > WindowMs) _times.Dequeue();

        var span = timeMs - _times.Peek();
        if (_times.Count >= 2 && span > 0)
            Hz = Math.Round((_times.Count - 1) * 1000.0 / span);
    }

    public void Reset()
    {
        _times.Clear();
        Hz = null;
    }
}
