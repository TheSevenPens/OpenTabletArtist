using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>One recorded calibration point for the report (#460). Screen coordinates are <em>relative to
/// the calibrated display</em> (0..Width, 0..Height), not virtual-desktop pixels, so they read naturally
/// against that one display (#461). Fields: the on-screen target it was captured for, the raw tablet
/// coordinate the pen actually reported, the <em>pixel-equivalent</em> of that raw coordinate (where the
/// uncorrected tap landed on the display, via the capture-time mapping — so it's directly comparable to
/// the target), and how many pen samples were averaged for the tap. <see cref="MeasuredX"/>/<see cref="MeasuredY"/>
/// are <see cref="float.NaN"/> for legacy reports captured before the pixel-equivalent was recorded.</summary>
public readonly record struct CalibrationReportPoint(
    float TargetX, float TargetY, float RawX, float RawY, float MeasuredX, float MeasuredY, int Samples)
{
    /// <summary>The tap's on-screen error in pixels (target → where the uncorrected pen landed) — the
    /// parallax this calibration corrects. NaN when the pixel-equivalent wasn't recorded.</summary>
    public float ErrorPx =>
        float.IsNaN(MeasuredX) || float.IsNaN(MeasuredY)
            ? float.NaN
            : MathF.Sqrt((MeasuredX - TargetX) * (MeasuredX - TargetX) + (MeasuredY - TargetY) * (MeasuredY - TargetY));
}

/// <summary>Fit quality for a completed calibration, derived from the per-tap on-screen error (#461):
/// how far the uncorrected pen was from each target. RMS/max summarize the parallax corrected; a single
/// tap standing far apart from the rest (<see cref="OutlierIndex"/>) is the tell-tale of a misfired tap,
/// which is what makes a bad capture obvious. Post-correction residual is ~0 by construction for the
/// homography/grid models (they fit their captured points exactly), so this reports the pre-correction
/// error instead.</summary>
public readonly record struct CalibrationFit(int PointCount, float AvgErrorPx, float MaxErrorPx, float RmsErrorPx, int OutlierIndex)
{
    public bool HasOutlier => OutlierIndex >= 0;
}

/// <summary>
/// The recorded points from a completed calibration, persisted alongside the calibration so the
/// Calibration tab can show a positional report after the fact (#460). Serialized to a compact string
/// for the filter store: <c>displayName|capturedAt|tx,ty,rx,ry,mx,my,n;…</c> (invariant numbers; the two
/// label fields have the delimiters stripped, which display names / timestamps never contain). Legacy
/// points without the pixel-equivalent serialize/parse as 5 fields (<c>tx,ty,rx,ry,n</c>).
/// </summary>
public sealed record CalibrationReport(string DisplayName, string CapturedAt, IReadOnlyList<CalibrationReportPoint> Points)
{
    public string Serialize()
    {
        string pts = string.Join(";", Points.Select(p => string.Join(",",
            new[] { p.TargetX, p.TargetY, p.RawX, p.RawY, p.MeasuredX, p.MeasuredY }
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
                // 7 fields = tx,ty,rx,ry,mx,my,n (current); 5 = tx,ty,rx,ry,n (legacy, no pixel-equivalent).
                if (f.Length == 7)
                {
                    if (!F(f[0], out var tx) || !F(f[1], out var ty) || !F(f[2], out var rx) || !F(f[3], out var ry) ||
                        !F(f[4], out var mx) || !F(f[5], out var my) || !I(f[6], out var n))
                        return null;
                    points.Add(new CalibrationReportPoint(tx, ty, rx, ry, mx, my, n));
                }
                else if (f.Length == 5)
                {
                    if (!F(f[0], out var tx) || !F(f[1], out var ty) || !F(f[2], out var rx) || !F(f[3], out var ry) ||
                        !I(f[4], out var n))
                        return null;
                    points.Add(new CalibrationReportPoint(tx, ty, rx, ry, float.NaN, float.NaN, n));
                }
                else return null;
            }
        }
        return new CalibrationReport(parts[0], parts[1], points);
    }

    /// <summary>Summarize the on-screen error across the taps, or null when no point recorded a
    /// pixel-equivalent (legacy reports).</summary>
    public CalibrationFit? ComputeFit()
    {
        var errs = new List<(int Index, float Err)>();
        for (int i = 0; i < Points.Count; i++)
        {
            var e = Points[i].ErrorPx;
            if (!float.IsNaN(e)) errs.Add((i, e));
        }
        if (errs.Count == 0) return null;

        float avg = errs.Average(e => e.Err);
        float max = errs.Max(e => e.Err);
        float rms = MathF.Sqrt(errs.Average(e => e.Err * e.Err));

        // Outlier = one tap standing clearly apart from the rest (> 10 px and more than twice the
        // next-largest error) — the signature of a misfired tap. Needs ≥3 points to be meaningful.
        int outlier = -1;
        if (errs.Count >= 3)
        {
            var ordered = errs.OrderByDescending(e => e.Err).ToList();
            if (ordered[0].Err > 10f && ordered[0].Err > 2f * MathF.Max(ordered[1].Err, 0.001f))
                outlier = ordered[0].Index;
        }
        return new CalibrationFit(errs.Count, avg, max, rms, outlier);
    }

    private static bool F(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    private static bool I(string s, out int v) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static string Strip(string s) => s.Replace('|', ' ').Replace(';', ' ').Replace(',', ' ');
}
