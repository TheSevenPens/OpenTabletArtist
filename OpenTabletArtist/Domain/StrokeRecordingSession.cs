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
/// ledger always accounts for every report that was admitted.
/// </para>
/// <para>
/// <b>Thread-safe.</b> Reports are added from the daemon's receive thread, at the moment they arrive, and read from
/// the UI thread. Stopping takes the same lock, so a report is either before the stop or after it: there is no
/// third state where it was received but is in neither the take nor the ledger.
/// </para>
/// <para>
/// Reports added after <see cref="Stop"/> are still counted, in the ledger's "after the stop", the way StrokeRecorder
/// counts them. The recording screen detaches from the stream at Stop, so in files it writes that column is
/// normally 0; a report received in the instant either side of the stop lands in exactly one place.
/// </para>
/// </remarks>
public sealed class StrokeRecordingSession
{
    /// <summary>About ten minutes at 1000 Hz, over an hour at 133 Hz. A forgotten recording stops growing here
    /// rather than eating memory; the reports it turned away are counted in <see cref="Dropped"/>.</summary>
    public const int MaxReadings = 600_000;

    /// <summary>
    /// What the numbers in a recording made here are, in the words the file carries in <c>device.conventions</c>.
    /// </summary>
    public const string Conventions =
        "Reports as OpenTabletDriver's tablet reader delivers them over the daemon's DeviceReport debug stream, "
        + "before any output-mode filter, so a pressure curve or smoothing set in OpenTabletArtist does not apply. "
        + "Only reports from this tablet that carry a position and a pressure are recorded; reports from other "
        + "devices and mouse-type reports are ignored, and the notes say how many. A channel is written only if every "
        + "recorded report carried it. x and y are the digitizer's own counts and pressure is the device's own count. "
        + "lean and azimuth are an approximation derived by OpenTabletArtist from the driver's tilt X and Y: "
        + "lean = hypot(tiltX, tiltY) and azimuth = atan2(tiltX, tiltY), in degrees (azimuth 0 to 360). They are not "
        + "calibrated equivalents of Wintab's lean and azimuth, and the driver's tilt sign conventions are not "
        + "normalized to a compass bearing. height is the driver's raw hover-distance value (0 to 255), not a "
        + "calibrated distance, and on some tablets it is not zero while the pen touches. The driver reports no "
        + "twist, so there is no twist column. Nothing is collected after Stop. A recording ends at "
        + "600,000 reports and the notes say how many later ones were dropped.";

    private readonly object _gate = new();
    private readonly int _fullScalePressure;
    private readonly List<TabletReading> _readings = [];
    private readonly Func<long> _now;
    private readonly long _startedTicks;
    private long _firstTicks;
    private int? _stoppedAt;
    private bool _allHeight = true, _allTilt = true;
    private bool _wasContact;
    private int _strokeCount;
    private int _dropped;

    /// <param name="fullScalePressure">The tablet's maximum pressure count, for samples that carry only a fraction.</param>
    /// <param name="now">The clock for samples that carry no arrival stamp (hand-built ones) and for the elapsed
    /// time shown while recording; tests supply their own.</param>
    public StrokeRecordingSession(int fullScalePressure, Func<long>? now = null)
    {
        _fullScalePressure = fullScalePressure;
        _now = now ?? Stopwatch.GetTimestamp;
        _startedTicks = _now();
    }

    public int Count { get { lock (_gate) return _readings.Count; } }

    public bool IsStopped { get { lock (_gate) return _stoppedAt is not null; } }

    /// <summary>The cap was reached and later reports were turned away. See <see cref="Dropped"/>.</summary>
    public bool Truncated => Dropped > 0;

    /// <summary>Reports that arrived once the recording was full and were not kept.</summary>
    public int Dropped { get { lock (_gate) return _dropped; } }

    /// <summary>Contact runs seen before the stop, so a clock on the screen can say how many strokes so far.</summary>
    public int StrokeCount { get { lock (_gate) return _strokeCount; } }

    /// <summary>The tablet's own pressure count at full scale, which the readings do not carry.</summary>
    public int FullScalePressure => _fullScalePressure;

    /// <summary>Seconds since the recording was started, on the host clock. It keeps running while the pen is out of
    /// range, which is what a clock on the screen is expected to do.</summary>
    public double ElapsedSeconds => (_now() - _startedTicks) / (double)Stopwatch.Frequency;

    /// <summary>Seconds from the first report to the latest, on the host clock: the span the data covers.</summary>
    public double Seconds
    {
        get
        {
            lock (_gate)
            {
                return _readings.Count < 2 ? 0 : (_readings[^1].ArrivedUs - _readings[0].ArrivedUs) / 1e6;
            }
        }
    }

    /// <summary>
    /// The channels every report carried. A channel that some reports lacked is left out of the file: writing
    /// zero for the ones that lacked it would be inventing a measurement.
    /// </summary>
    public TakeChannels Channels
    {
        get { lock (_gate) return new(Height: _allHeight, Tilt: _allTilt, Twist: false); }
    }

    /// <summary>Adds one report. Called from the receive thread; cheap enough for the per-report path.</summary>
    public void Add(PenSample s)
    {
        lock (_gate)
        {
            if (_readings.Count >= MaxReadings)
            {
                _dropped++;
                return;
            }

            var ticks = s.Timestamp != 0 ? s.Timestamp : _now();
            if (_readings.Count == 0) _firstTicks = ticks;

            var reading = new TabletReading(
                ArrivedUs: Microseconds(ticks - _firstTicks),
                X: s.RawX,
                Y: s.RawY,
                Pressure: PressureCount(s),
                Height: s.HoverDistance,
                Lean: 90.0 - DiagnosticsMath.TiltAltitudeDegrees(s.TiltX, s.TiltY),
                Azimuth: DiagnosticsMath.TiltAzimuthDegrees(s.TiltX, s.TiltY),
                Twist: 0);

            _readings.Add(reading);

            if (s.HoverDistance is null) _allHeight = false;
            if (!s.HasTilt) _allTilt = false;

            if (_stoppedAt is null && reading.InContact && !_wasContact) _strokeCount++;
            _wasContact = reading.InContact;
        }
    }

    /// <summary>
    /// The device's pressure count. A real report carries it; a hand-built sample carries only the fraction, and a
    /// fraction above zero must stay above zero: rounding to even would turn a light touch into no contact.
    /// </summary>
    private double PressureCount(PenSample s)
    {
        if (s.RawPressure > 0) return s.RawPressure;
        if (s.Pressure <= 0) return 0;

        return Math.Max(1, Math.Round(s.Pressure * _fullScalePressure, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Stopwatch ticks to microseconds without the intermediate product overflowing: the whole seconds are converted
    /// separately from the remainder, so a recording that sat out of range for days still converts.
    /// </summary>
    internal static long Microseconds(long ticks)
    {
        var freq = Stopwatch.Frequency;
        return ticks / freq * 1_000_000 + ticks % freq * 1_000_000 / freq;
    }

    /// <summary>Ends the recording. A report added afterwards is counted in the ledger's "after the stop".</summary>
    public void Stop()
    {
        lock (_gate) _stoppedAt ??= _readings.Count;
    }

    /// <summary>Cuts what was recorded into strokes by StrokeRecorder's policy.</summary>
    public SegmentedTake Segment(bool keepAirborne)
    {
        lock (_gate) return StrokeSegmenter.Segment(_readings, keepAirborne, _stoppedAt);
    }
}
