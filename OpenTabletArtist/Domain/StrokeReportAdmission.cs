using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Decides, at the moment a report arrives from the daemon, whether it belongs in a recording, and if so adds it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why here and not on the page.</b> The page's sample path runs on the UI thread after the report has been
/// queued, and by then two things are gone. The report's identity is gone, so a second tablet's reports would be
/// recorded under the first's name and pressure scale. And the moment the report arrived is gone, so one that was
/// already received when Stop was pressed would be dropped by a phase check made later, and appear in no column of
/// the ledger. Admission runs on the receive thread, with the raw report in hand, and the session's lock puts every
/// report on one side or the other of the stop.
/// </para>
/// <para>
/// A report is admitted only if it is from this tablet, with the specifications the recording was started with,
/// and is a pen measurement (see <see cref="DeviceReportSample.IsPenMeasurement"/>) with finite numbers. Everything
/// else is counted by reason, because a recording that quietly ignored reports would be one more way for the ledger to
/// say less than happened.
/// </para>
/// </remarks>
public sealed class StrokeReportAdmission
{
    private readonly StrokeRecordingContext _context;
    private readonly StrokeRecordingSession _session;
    private int _otherTablet, _notPen;
    private volatile bool _specificationsChanged;

    public StrokeReportAdmission(StrokeRecordingContext context, StrokeRecordingSession session)
    {
        _context = context;
        _session = session;
    }

    /// <summary>Reports from some other device while this one was being recorded.</summary>
    public int IgnoredOtherTablet => Volatile.Read(ref _otherTablet);

    /// <summary>Reports from this tablet that were not complete pen measurements: a mouse report, a missing
    /// pressure, a half-present tilt, or a number that is not a number.</summary>
    public int IgnoredNotPen => Volatile.Read(ref _notPen);

    /// <summary>This tablet's reports began to describe a different digitizer or pressure scale than the one the
    /// recording was started with. The recording cannot continue: its units would no longer mean one thing.</summary>
    public bool SpecificationsChanged => _specificationsChanged;

    /// <summary>One report, with the sample parsed from it. Called on the receive thread.</summary>
    public void Offer(JObject data, PenSample sample)
    {
        if (_specificationsChanged) return;

        var who = DeviceReportSample.Identity(data);

        if (!string.Equals(who.Name, _context.Tablet, StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _otherTablet);
            return;
        }

        if (who.MaxX != _context.Space.MaxX
            || who.MaxY != _context.Space.MaxY
            || (int)who.MaxPressure != _context.FullScalePressure)   // the context truncates it the same way
        {
            _specificationsChanged = true;
            return;
        }

        if (!DeviceReportSample.IsPenMeasurement(data) || !IsFinite(sample))
        {
            Interlocked.Increment(ref _notPen);
            return;
        }

        _session.Add(sample);
    }

    private static bool IsFinite(PenSample s) =>
        double.IsFinite(s.RawX) && double.IsFinite(s.RawY) && double.IsFinite(s.Pressure)
        && double.IsFinite(s.RawPressure) && double.IsFinite(s.TiltX) && double.IsFinite(s.TiltY);
}
