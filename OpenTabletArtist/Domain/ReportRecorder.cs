using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Records the pen reports the scribble page receives (every one, hovering included) so a stroke can be
/// analysed offline — e.g. how often position and pressure actually change between reports, which says what
/// rate each is really being measured at. Opt-in (see <c>TestViewModel</c>); nothing is kept unless a
/// recorder exists. Capped, so a forgotten recording can't grow without bound.
/// </summary>
public sealed class ReportRecorder
{
    /// <summary>About 10 minutes at 1000 Hz.</summary>
    public const int MaxReports = 600_000;

    private readonly List<(double TMs, PenSample S)> _rows = new();
    private long _firstTicks;

    public int Count => _rows.Count;

    /// <summary>Adds a report. <paramref name="s"/>'s <see cref="PenSample.Timestamp"/> (Stopwatch ticks, stamped
    /// when the daemon event reached the app) gives the time; an unstamped sample gets its index as a placeholder.</summary>
    public void Add(PenSample s)
    {
        if (_rows.Count >= MaxReports) return;
        if (_rows.Count == 0) _firstTicks = s.Timestamp;
        var t = s.Timestamp != 0
            ? (s.Timestamp - _firstTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency
            : _rows.Count;
        _rows.Add((t, s));
    }

    public void Clear() => _rows.Clear();

    /// <summary>CSV with a header: t_ms (since the first report), raw_x, raw_y, pressure (0..1, as the app sees
    /// it), tilt_x, tilt_y, hover (blank when the report carried none). Invariant culture, full precision.</summary>
    public string ToCsv()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("t_ms,raw_x,raw_y,pressure,tilt_x,tilt_y,hover\n");
        foreach (var (t, s) in _rows)
            sb.Append(t.ToString("0.###", inv)).Append(',')
              .Append(s.RawX.ToString("R", inv)).Append(',')
              .Append(s.RawY.ToString("R", inv)).Append(',')
              .Append(s.Pressure.ToString("R", inv)).Append(',')
              .Append(s.TiltX.ToString("R", inv)).Append(',')
              .Append(s.TiltY.ToString("R", inv)).Append(',')
              .Append(s.HoverDistance?.ToString(inv) ?? "").Append('\n');
        return sb.ToString();
    }

    /// <summary>Writes the recording to <paramref name="path"/> (replacing it) and starts a new one. Returns the
    /// number of reports written; 0 means there was nothing to write and the file was left alone.</summary>
    public int WriteAndReset(string path)
    {
        var n = _rows.Count;
        if (n == 0) return 0;
        File.WriteAllText(path, ToCsv());
        _rows.Clear();
        return n;
    }
}
