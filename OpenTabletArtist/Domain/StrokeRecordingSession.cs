using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenTabletArtist.Domain;

/// <summary>
/// One stroke-recording run on the Scribble page: collects the pen reports from "Record" until it is saved or
/// thrown away, as <see cref="TabletReading"/>s in the tablet's own units, and cuts them into strokes on demand.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is decided while recording. The reports are kept as they arrive and <see cref="Segment"/> applies the
/// capture policy afterwards, which is why "keep every airborne reading" can be chosen after the fact and why the
/// ledger always accounts for every report.
/// </para>
/// <para>
/// <b>Reports keep being counted after <see cref="Stop"/></b>, until the session is saved or discarded, the way
/// StrokeRecorder keeps counting after its stop: somebody finishing the stroke they were in the middle of is not
/// asking for it to be kept, but it was handed over, so the ledger has a column for it.
/// </para>
/// </remarks>
public sealed class StrokeRecordingSession
{
    /// <summary>About ten minutes at 1000 Hz. A forgotten recording stops growing here rather than eating memory.</summary>
    public const int MaxReadings = 600_000;

    /// <summary>
    /// What the numbers in a recording made here are, in the words the file carries in <c>device.conventions</c>.
    /// </summary>
    public const string Conventions =
        "Reports as OpenTabletDriver's tablet reader delivers them over the daemon's DeviceReport debug stream, "
        + "before any output-mode filter, so a pressure curve or smoothing set in OpenTabletArtist does not apply. "
        + "x and y are the digitizer's own counts; pressure is the device's own count. lean and azimuth are derived "
        + "from the driver's tilt X and Y: lean = the angle off vertical, hypot(tiltX, tiltY); azimuth = "
        + "atan2(tiltX, tiltY) in degrees, 0 to 360. The driver's tilt sign conventions are not normalized to a "
        + "compass bearing. height is the driver's hover distance (0 to 255), not Wintab's pkZ. The driver reports "
        + "no twist, so there is no twist column.";

    private readonly int _fullScalePressure;
    private readonly List<TabletReading> _readings = [];
    private readonly Func<long> _now;
    private long _firstTicks;
    private int? _stoppedAt;
    private bool _allHeight = true, _allTilt = true;
    private bool _wasContact;

    /// <param name="fullScalePressure">The tablet's maximum pressure count, for samples that carry only a fraction.</param>
    /// <param name="now">The clock for samples that carry no arrival stamp (hand-built ones); tests supply their own.</param>
    public StrokeRecordingSession(int fullScalePressure, Func<long>? now = null)
    {
        _fullScalePressure = fullScalePressure;
        _now = now ?? Stopwatch.GetTimestamp;
    }

    public int Count => _readings.Count;

    public bool IsStopped => _stoppedAt is not null;

    /// <summary>The cap was reached and later reports were dropped. The recording says so in its notes.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Contact runs seen before the stop, so a clock on the screen can say how many strokes so far.</summary>
    public int StrokeCount { get; private set; }

    /// <summary>The tablet's own pressure count at full scale, which the readings do not carry.</summary>
    public int FullScalePressure => _fullScalePressure;

    /// <summary>Seconds from the first report to the latest, on the host clock.</summary>
    public double Seconds => _readings.Count < 2 ? 0 : (_readings[^1].ArrivedUs - _readings[0].ArrivedUs) / 1e6;

    /// <summary>
    /// The channels every report carried. A channel that some reports lacked is left out of the file: writing
    /// zero for the ones that lacked it would be inventing a measurement.
    /// </summary>
    public TakeChannels Channels => new(Height: _allHeight, Tilt: _allTilt, Twist: false);

    /// <summary>Adds one report. Cheap enough for the per-report path.</summary>
    public void Add(PenSample s)
    {
        if (_readings.Count >= MaxReadings)
        {
            Truncated = true;
            return;
        }

        var ticks = s.Timestamp != 0 ? s.Timestamp : _now();
        if (_readings.Count == 0) _firstTicks = ticks;

        var pressure = s.RawPressure > 0
            ? s.RawPressure
            : Math.Round(s.Pressure * _fullScalePressure);

        var reading = new TabletReading(
            ArrivedUs: (ticks - _firstTicks) * 1_000_000 / Stopwatch.Frequency,
            X: s.RawX,
            Y: s.RawY,
            Pressure: pressure,
            Height: s.HoverDistance,
            Lean: 90.0 - DiagnosticsMath.TiltAltitudeDegrees(s.TiltX, s.TiltY),
            Azimuth: DiagnosticsMath.TiltAzimuthDegrees(s.TiltX, s.TiltY),
            Twist: 0);

        _readings.Add(reading);

        if (s.HoverDistance is null) _allHeight = false;
        if (!s.HasTilt) _allTilt = false;

        if (!IsStopped && reading.InContact && !_wasContact) StrokeCount++;
        _wasContact = reading.InContact;
    }

    /// <summary>Ends the recording. Reports arriving afterwards are still counted, in the ledger's "after the stop".</summary>
    public void Stop() => _stoppedAt ??= _readings.Count;

    /// <summary>Cuts what was recorded into strokes by StrokeRecorder's policy.</summary>
    public SegmentedTake Segment(bool keepAirborne) =>
        StrokeSegmenter.Segment(_readings, keepAirborne, _stoppedAt);
}
