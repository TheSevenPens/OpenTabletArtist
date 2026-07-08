using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>One recorded calibration point for the report (#460): the on-screen target it was captured
/// for (desktop pixels) and the raw tablet coordinate the pen actually reported, plus how many pen
/// samples were averaged for the tap.</summary>
public readonly record struct CalibrationReportPoint(float TargetX, float TargetY, float RawX, float RawY, int Samples);

/// <summary>
/// The recorded points from a completed calibration, persisted alongside the calibration so the
/// Calibration tab can show a positional report after the fact (#460). Serialized to a compact string
/// for the filter store: <c>displayName|capturedAt|tx,ty,rx,ry,n;tx,ty,rx,ry,n;…</c> (invariant numbers;
/// the two label fields have the delimiters stripped, which display names / timestamps never contain).
/// </summary>
public sealed record CalibrationReport(string DisplayName, string CapturedAt, IReadOnlyList<CalibrationReportPoint> Points)
{
    public string Serialize()
    {
        string pts = string.Join(";", Points.Select(p => string.Join(",",
            new[] { p.TargetX, p.TargetY, p.RawX, p.RawY }
                .Select(f => f.ToString(CultureInfo.InvariantCulture))
                .Append(p.Samples.ToString(CultureInfo.InvariantCulture)))));
        return string.Join("|", Strip(DisplayName), Strip(CapturedAt), pts);
    }

    public static CalibrationReport? TryParse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split('|');
        if (parts.Length != 3) return null;

        var points = new List<CalibrationReportPoint>();
        if (parts[2].Length > 0)
        {
            foreach (var seg in parts[2].Split(';'))
            {
                var f = seg.Split(',');
                if (f.Length != 5) return null;
                if (!float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tx) ||
                    !float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ty) ||
                    !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var rx) ||
                    !float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var ry) ||
                    !int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    return null;
                points.Add(new CalibrationReportPoint(tx, ty, rx, ry, n));
            }
        }
        return new CalibrationReport(parts[0], parts[1], points);
    }

    private static string Strip(string s) => s.Replace('|', ' ').Replace(';', ' ').Replace(',', ' ');
}
