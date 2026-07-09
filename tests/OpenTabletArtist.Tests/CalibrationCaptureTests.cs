using System.Collections.Generic;
using System.Numerics;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class CalibrationCaptureTests
{
    private static readonly TabletDigitizerSpec Digi = new(100, 100, 1000, 1000);
    private static readonly MappingArea Input = new(50, 50, 100, 100);

    // A full-display output mapping centered on the given display (keeps raw taps in range).
    private static MappingArea OutputFor(DisplayInfo d) =>
        new(d.X + d.Width / 2f, d.Y + d.Height / 2f, d.Width, d.Height);

    // Build a real affine calibration + report from 4 corner taps, each a few px off-target.
    private static (CalibrationProfile.CalibrationData cal, MappingArea output, List<Vector2> targets, List<Vector2> raw)
        MakeCalibration(DisplayInfo display)
    {
        var output = OutputFor(display);
        var corners = new (double X, double Y)[] { (0.1, 0.1), (0.9, 0.1), (0.9, 0.9), (0.1, 0.9) };
        var jitter = new (float dx, float dy)[] { (3, -2), (-4, 1), (2, 5), (-1, -3) };

        var targets = new List<Vector2>();
        var raw = new List<Vector2>();
        for (int i = 0; i < 4; i++)
        {
            var target = new Vector2(
                (float)(display.X + corners[i].X * display.Width),
                (float)(display.Y + corners[i].Y * display.Height));
            var jittered = new Vector2(target.X + jitter[i].dx, target.Y + jitter[i].dy);
            targets.Add(target);
            raw.Add(AbsolutePositionMapper.MapFromDesktop(jittered, Digi, Input, output)!.Value);
        }

        var m = CalibrationSolver.Solve(targets, raw, Digi, Input, output)!.Value;
        float ox = display.X, oy = display.Y;
        var pts = new List<CalibrationReportPoint>();
        for (int i = 0; i < 4; i++)
        {
            var mpx = AbsolutePositionMapper.MapToDesktop(raw[i], Digi, Input, output, false, false)!.Value;
            pts.Add(new CalibrationReportPoint(
                targets[i].X - ox, targets[i].Y - oy, raw[i].X, raw[i].Y, mpx.X - ox, mpx.Y - oy, 500, 16f, -8f));
        }
        var report = new CalibrationReport("Main (1920×1080)", "2026-07-09 12:00", pts);
        var cal = new CalibrationProfile.CalibrationData(m, Enabled: true, Fingerprint: "fp") with { Report = report };
        return (cal, output, targets, raw);
    }

    private static void AssertMatrixClose(Matrix3x2 a, Matrix3x2 b, int precision = 3)
    {
        Assert.Equal(a.M11, b.M11, precision); Assert.Equal(a.M12, b.M12, precision);
        Assert.Equal(a.M21, b.M21, precision); Assert.Equal(a.M22, b.M22, precision);
        Assert.Equal(a.M31, b.M31, precision); Assert.Equal(a.M32, b.M32, precision);
    }

    [Fact]
    public void Build_ThenJsonRoundTrip_PreservesPointsContextAndSolvedModel()
    {
        // An offset display exercises the display-relative → desktop target conversion.
        var display = new DisplayInfo(2, "Main", 1920, 1080, 100, 200, false);
        var (cal, output, targets, _) = MakeCalibration(display);

        var capture = CalibrationCaptureService.Build("T", Digi, Input, output, display, cal);
        var round = CalibrationCapture.FromJson(capture.ToJson());

        Assert.NotNull(round);
        Assert.Equal(CalibrationCapture.CurrentSchemaVersion, round!.SchemaVersion);
        Assert.Equal("T", round.Tablet);
        Assert.Equal("Corners", round.Mode);
        Assert.Equal(4, round.Points.Count);
        Assert.Equal("Affine", round.Solved!.Model);

        // Targets are stored as desktop px and survive the round-trip.
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(targets[i].X, round.Points[i].TargetDesktopX, 2);
            Assert.Equal(targets[i].Y, round.Points[i].TargetDesktopY, 2);
        }
        // Context preserved.
        Assert.Equal(Digi.MaxX, round.Context.Digitizer.MaxX);
        Assert.Equal(2, round.Context.Display.Number);
        Assert.Equal(100, round.Context.Display.X);

        // Pen tilt (#481) survives the capture round-trip and reaches a re-solved report.
        Assert.Equal(16f, round.Points[0].TiltX, 3);
        Assert.Equal(-8f, round.Points[0].TiltY, 3);
        var resolved = CalibrationCaptureService.ReSolve(round, "Affine", "fp");
        Assert.True(resolved!.Report!.ComputeTilt() is { Count: 4 });
    }

    [Fact]
    public void ReSolve_Affine_MatchesDirectSolver()
    {
        var display = new DisplayInfo(1, "Main", 1920, 1080, 0, 0, true);
        var (cal, output, targets, raw) = MakeCalibration(display);
        var capture = CalibrationCaptureService.Build("T", Digi, Input, output, display, cal);

        var direct = CalibrationSolver.Solve(targets, raw, Digi, Input, output)!.Value;
        var resolved = CalibrationCaptureService.ReSolve(capture, "Affine", "fp");

        Assert.NotNull(resolved);
        Assert.Equal(CalibrationProfile.CalibrationModel.Affine, resolved!.Model);
        AssertMatrixClose(direct, resolved.Transform);
    }

    [Fact]
    public void ToCalibrationData_RestoresEmbeddedAffineExactly()
    {
        var display = new DisplayInfo(1, "Main", 1920, 1080, 0, 0, true);
        var (cal, output, _, _) = MakeCalibration(display);
        var capture = CalibrationCaptureService.Build("T", Digi, Input, output, display, cal);

        var restored = CalibrationCaptureService.ToCalibrationData(capture, "fp2");

        Assert.NotNull(restored);
        Assert.Equal(CalibrationProfile.CalibrationModel.Affine, restored!.Model);
        AssertMatrixClose(cal.Transform, restored.Transform);
        Assert.Equal("fp2", restored.Fingerprint);            // re-stamped to the current mapping
        Assert.Equal(4, restored.Report!.Points.Count);       // report rebuilt from the taps
    }

    [Fact]
    public void ReSolve_Homography_ProducesHomographyModel()
    {
        var display = new DisplayInfo(1, "Main", 1920, 1080, 0, 0, true);
        var (cal, output, _, _) = MakeCalibration(display);
        var capture = CalibrationCaptureService.Build("T", Digi, Input, output, display, cal);

        var resolved = CalibrationCaptureService.ReSolve(capture, "Homography", "fp");

        Assert.NotNull(resolved);
        Assert.Equal(CalibrationProfile.CalibrationModel.Homography, resolved!.Model);
    }

    [Fact]
    public void ReSolve_Grid_OnCornersCapture_ReturnsNull()
    {
        // A 4-corner capture isn't a grid (Cols/Rows = 0), so a Grid re-solve can't run.
        var display = new DisplayInfo(1, "Main", 1920, 1080, 0, 0, true);
        var (cal, output, _, _) = MakeCalibration(display);
        var capture = CalibrationCaptureService.Build("T", Digi, Input, output, display, cal);

        Assert.Null(CalibrationCaptureService.ReSolve(capture, "Grid", "fp"));
    }

    [Fact]
    public void Matches_SameContext_True_DifferentContext_False()
    {
        var display = new DisplayInfo(1, "Main", 1920, 1080, 0, 0, true);
        var (cal, output, _, _) = MakeCalibration(display);
        var capture = CalibrationCaptureService.Build("T", Digi, Input, output, display, cal);

        Assert.True(CalibrationCaptureService.Matches(capture, Digi, Input, output, display.Number, out _));

        // Different output area → mismatch.
        var otherOutput = new MappingArea(500, 500, 1280, 720);
        Assert.False(CalibrationCaptureService.Matches(capture, Digi, Input, otherOutput, display.Number, out var r1));
        Assert.Contains("mapping", r1);

        // Different digitizer range → mismatch (raw units meaningless).
        var otherDigi = new TabletDigitizerSpec(100, 100, 2000, 2000);
        Assert.False(CalibrationCaptureService.Matches(capture, otherDigi, Input, output, display.Number, out var r2));
        Assert.Contains("digitizer", r2);
    }

    [Fact]
    public void FromJson_Garbage_ReturnsNull()
    {
        Assert.Null(CalibrationCapture.FromJson("not json at all"));
        Assert.Null(CalibrationCapture.FromJson("{ \"unclosed\": "));
    }
}
