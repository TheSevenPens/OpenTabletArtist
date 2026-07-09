using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Bridges a <see cref="CalibrationCapture"/> (portable snapshot, #484) and the live calibration store.
/// Builds a capture from a tablet's saved calibration + mapping context, restores the embedded solved
/// model as-is, or re-solves the captured taps with a different algorithm — the latter being the fast
/// way to compare models on the exact same taps without re-tapping. Import is <b>matching-only</b>:
/// <see cref="Matches"/> refuses a capture whose digitizer range or area mapping differs from the
/// current tablet, since the raw taps would otherwise be meaningless.
/// </summary>
public static class CalibrationCaptureService
{
    /// <summary>Build a portable capture from a tablet's saved calibration and the mapping context it was
    /// captured against. Requires <paramref name="cal"/> to carry a recorded report (the raw taps).</summary>
    public static CalibrationCapture Build(
        string tablet, TabletDigitizerSpec digi, MappingArea input, MappingArea output,
        DisplayInfo display, CalibrationProfile.CalibrationData cal)
    {
        var points = new List<CalibrationCapturePoint>();
        if (cal.Report != null)
        {
            foreach (var p in cal.Report.Points)
                // Report targets are display-relative; store desktop px (what the solver consumes). Tilt
                // (TiltX/TiltY °) rides along so the capture carries the natural pen angle (#481).
                points.Add(new CalibrationCapturePoint(
                    p.TargetX + display.X, p.TargetY + display.Y, p.RawX, p.RawY, p.Samples, p.TiltX, p.TiltY));
        }

        string mode = cal.Model == CalibrationProfile.CalibrationModel.Grid ? "Grid" : "Corners";
        int cols = cal.Grid?.Cols ?? 0;
        int rows = cal.Grid?.Rows ?? 0;

        var solved = new CalibrationSolvedModel(
            cal.Model.ToString(),
            cal.Model == CalibrationProfile.CalibrationModel.Affine ? FormatMatrix(cal.Transform) : "",
            cal.Model == CalibrationProfile.CalibrationModel.Homography ? cal.Homography.ToCsv() : "",
            cal.Model == CalibrationProfile.CalibrationModel.Grid && cal.Grid != null ? cal.Grid.ToCsv() : "");

        var context = new CalibrationCaptureContext(
            new CaptureDigitizer(digi.Width, digi.Height, digi.MaxX, digi.MaxY),
            new CaptureArea(input.CenterX, input.CenterY, input.Width, input.Height, input.Rotation),
            new CaptureArea(output.CenterX, output.CenterY, output.Width, output.Height, output.Rotation),
            new CaptureDisplay(display.Number, display.Name, display.X, display.Y, display.Width, display.Height));

        return new CalibrationCapture(
            CalibrationCapture.CurrentSchemaVersion, tablet,
            cal.Report?.CapturedAt ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            mode, cols, rows, context, points, solved);
    }

    /// <summary>Reconstruct the exact calibration the capture stored (the "known-good" restore), or null
    /// if it carries no embedded solved model. <paramref name="fingerprint"/> stamps it against the
    /// current mapping so staleness detection stays correct.</summary>
    public static CalibrationProfile.CalibrationData? ToCalibrationData(CalibrationCapture c, string fingerprint)
    {
        if (c.Solved is not { } s) return null;
        var report = BuildReport(c);
        switch (s.Model)
        {
            case "Affine":
                if (TryParseMatrix(s.Transform, out var m))
                    return new CalibrationProfile.CalibrationData(m, Enabled: true, Fingerprint: fingerprint)
                        with { Report = report };
                break;
            case "Homography":
                if (Homography.TryParse(s.Homography) is { } h)
                    return CalibrationProfile.CalibrationData.ForHomography(h, enabled: true, fingerprint)
                        with { Report = report };
                break;
            case "Grid":
                if (CalibrationGrid.TryParse(s.Grid) is { } g)
                    return CalibrationProfile.CalibrationData.ForGrid(g, enabled: true, fingerprint)
                        with { Report = report };
                break;
        }
        return null;
    }

    /// <summary>Re-solve the captured taps with <paramref name="mode"/> ("Affine", "Homography", or "Grid")
    /// and stamp it with <paramref name="fingerprint"/>. Null if the model can't be solved from these taps
    /// (e.g. Grid on a capture that isn't a grid, or degenerate input).</summary>
    public static CalibrationProfile.CalibrationData? ReSolve(CalibrationCapture c, string mode, string fingerprint)
    {
        var digi = c.DigitizerSpec;
        var input = c.InputArea;
        var output = c.OutputArea;
        var targets = c.TargetsDesktop;
        var raw = c.MeasuredRaw;
        var report = BuildReport(c);

        switch (mode)
        {
            case "Affine":
                if (CalibrationSolver.Solve(targets, raw, digi, input, output) is { } m)
                    return new CalibrationProfile.CalibrationData(m, Enabled: true, Fingerprint: fingerprint)
                        with { Report = report };
                break;
            case "Homography":
                if (CalibrationSolver.SolveHomography(targets, raw, digi, input, output) is { } h)
                    return CalibrationProfile.CalibrationData.ForHomography(h, enabled: true, fingerprint)
                        with { Report = report };
                break;
            case "Grid":
                if (c.Cols >= 2 && c.Rows >= 2 &&
                    CalibrationSolver.SolveGrid(targets, raw, digi, input, output, c.Cols, c.Rows) is { } g)
                    return CalibrationProfile.CalibrationData.ForGrid(g, enabled: true, fingerprint)
                        with { Report = report };
                break;
        }
        return null;
    }

    /// <summary>True when the capture's context matches the current tablet closely enough to apply. Raw
    /// taps only mean something if the digitizer range and the area mapping (+ display) are the same, so
    /// this is what enforces matching-only import. <paramref name="reason"/> explains a mismatch.</summary>
    public static bool Matches(
        CalibrationCapture c, TabletDigitizerSpec digi, MappingArea input, MappingArea output,
        int displayNumber, out string reason)
    {
        if (!Near(c.Context.Digitizer.MaxX, digi.MaxX) || !Near(c.Context.Digitizer.MaxY, digi.MaxY))
        {
            reason = "the tablet's digitizer range differs";
            return false;
        }
        var captured = CalibrationProfile.Fingerprint(c.InputArea, c.OutputArea, c.Context.Display.Number);
        var current = CalibrationProfile.Fingerprint(input, output, displayNumber);
        if (captured != current)
        {
            reason = "the area mapping or display differs";
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>Rebuild the tab report (target/measured display-relative px + raw + samples) from the
    /// capture's taps, so a re-solved/restored calibration shows the same populated report. The
    /// pre-correction parallax is independent of which model is fitted.</summary>
    private static CalibrationReport BuildReport(CalibrationCapture c)
    {
        var digi = c.DigitizerSpec;
        var input = c.InputArea;
        var output = c.OutputArea;
        float ox = c.Context.Display.X, oy = c.Context.Display.Y;

        var points = new List<CalibrationReportPoint>(c.Points.Count);
        foreach (var p in c.Points)
        {
            var measuredPx = AbsolutePositionMapper.MapToDesktop(new Vector2(p.RawX, p.RawY), digi, input, output, false, false);
            float mx = float.NaN, my = float.NaN;
            if (measuredPx is { } m) { mx = m.X - ox; my = m.Y - oy; }
            points.Add(new CalibrationReportPoint(
                p.TargetDesktopX - ox, p.TargetDesktopY - oy, p.RawX, p.RawY, mx, my, p.Samples, p.TiltX, p.TiltY));
        }
        var name = $"{c.Context.Display.Name} ({c.Context.Display.Width}×{c.Context.Display.Height})";
        return new CalibrationReport(name, c.CapturedAt, points);
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) <= 0.5f;

    private static string FormatMatrix(Matrix3x2 m) =>
        string.Join(",", new[] { m.M11, m.M12, m.M21, m.M22, m.M31, m.M32 }
            .Select(f => f.ToString("R", CultureInfo.InvariantCulture)));

    private static bool TryParseMatrix(string? csv, out Matrix3x2 m)
    {
        m = Matrix3x2.Identity;
        if (string.IsNullOrWhiteSpace(csv)) return false;
        var p = csv.Split(',');
        if (p.Length != 6) return false;
        var v = new float[6];
        for (int i = 0; i < 6; i++)
            if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
        m = new Matrix3x2(v[0], v[1], v[2], v[3], v[4], v[5]);
        return true;
    }
}
