using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class CalibrationSolverTests
{
    // 100x100mm tablet @ 1000x1000 units; full-tablet input; 1920x1080 display centred at (960,540).
    private static readonly TabletDigitizerSpec Digi = new(100, 100, 1000, 1000);
    private static readonly MappingArea Input = new(50, 50, 100, 100);
    private static readonly MappingArea Output = new(960, 540, 1920, 1080);

    // Four inset-corner targets on the display (10% in).
    private static readonly Vector2[] Targets =
    {
        new(192, 108), new(1728, 108), new(1728, 972), new(192, 972),
    };

    [Fact]
    public void Solve_RecoversCorrection_SoCorrectedTapsHitTargets()
    {
        // Model a physical error: aiming at target i, the pen reports a raw position offset/scaled
        // from the "true" expected raw (in normalized space) by a known affine E.
        var error = new Matrix3x2(1.03f, 0.01f, -0.015f, 0.97f, 0.04f, -0.03f);
        var measuredRaw = Targets.Select(t =>
        {
            var expected = AbsolutePositionMapper.MapFromDesktop(t, Digi, Input, Output)!.Value;
            var en = CalibrationMath.ToNormalized(expected, Digi.MaxX, Digi.MaxY);
            var measuredN = Vector2.Transform(en, error); // what the pen actually reports
            return CalibrationMath.FromNormalized(measuredN, Digi.MaxX, Digi.MaxY);
        }).ToList();

        var correction = CalibrationSolver.Solve(Targets, measuredRaw, Digi, Input, Output);
        Assert.NotNull(correction);

        // End-to-end: applying the correction to each measured raw, then OTD's absolute map, should
        // land within a pixel of the target the user aimed at.
        for (int i = 0; i < Targets.Length; i++)
        {
            var n = CalibrationMath.ToNormalized(measuredRaw[i], Digi.MaxX, Digi.MaxY);
            var correctedRaw = CalibrationMath.FromNormalized(Vector2.Transform(n, correction!.Value), Digi.MaxX, Digi.MaxY);
            var screen = AbsolutePositionMapper.MapToDesktop(correctedRaw, Digi, Input, Output, false, false)!.Value;
            Assert.True((screen - Targets[i]).Length() < 1.0f, $"target {i}: landed at {screen}, wanted {Targets[i]}");
        }
    }

    [Fact]
    public void Solve_NoError_YieldsIdentityLikeCorrection()
    {
        var measuredRaw = Targets.Select(t => AbsolutePositionMapper.MapFromDesktop(t, Digi, Input, Output)!.Value).ToList();
        var correction = CalibrationSolver.Solve(Targets, measuredRaw, Digi, Input, Output);
        Assert.NotNull(correction);
        // Corrected taps still hit their targets (correction ≈ identity).
        foreach (var (t, raw) in Targets.Zip(measuredRaw))
        {
            var n = CalibrationMath.ToNormalized(raw, Digi.MaxX, Digi.MaxY);
            var correctedRaw = CalibrationMath.FromNormalized(Vector2.Transform(n, correction!.Value), Digi.MaxX, Digi.MaxY);
            var screen = AbsolutePositionMapper.MapToDesktop(correctedRaw, Digi, Input, Output, false, false)!.Value;
            Assert.True((screen - t).Length() < 1.0f);
        }
    }

    [Fact]
    public void Solve_MismatchedOrTooFew_ReturnsNull()
    {
        Assert.Null(CalibrationSolver.Solve(Targets, new List<Vector2> { new(1, 1) }, Digi, Input, Output));
        Assert.Null(CalibrationSolver.Solve(Targets.Take(2).ToList(), Targets.Take(2).Select(_ => Vector2.Zero).ToList(), Digi, Input, Output));
    }

    [Fact]
    public void Solve_DegenerateDigitizer_ReturnsNull()
    {
        var bad = new TabletDigitizerSpec(100, 100, 0, 1000);
        var measured = Targets.Select(_ => Vector2.Zero).ToList();
        Assert.Null(CalibrationSolver.Solve(Targets, measured, bad, Input, Output));
    }
}
