using System.Collections.Generic;
using System.Numerics;

namespace OtdWindowsHelper.Domain;

/// <summary>
/// Turns a set of calibration taps into the affine correction (#127). For each on-screen target we
/// know where it is (desktop px) and the raw tablet position the pen actually reported there. The
/// correction maps each measured raw position to the raw position that <em>should</em> map to that
/// target under the current absolute mapping (<see cref="AbsolutePositionMapper.MapFromDesktop"/>),
/// solved as a least-squares affine in normalized tablet space.
/// </summary>
public static class CalibrationSolver
{
    /// <summary>Solve the correction, or null if inputs are mismatched/degenerate/non-invertible.</summary>
    public static Matrix3x2? Solve(
        IReadOnlyList<Vector2> targetsDesktop,
        IReadOnlyList<Vector2> measuredRaw,
        TabletDigitizerSpec digitizer,
        MappingArea input,
        MappingArea output)
    {
        if (targetsDesktop.Count != measuredRaw.Count || targetsDesktop.Count < 3) return null;

        var from = new List<Vector2>(targetsDesktop.Count);
        var to = new List<Vector2>(targetsDesktop.Count);
        for (int i = 0; i < targetsDesktop.Count; i++)
        {
            var expectedRaw = AbsolutePositionMapper.MapFromDesktop(targetsDesktop[i], digitizer, input, output);
            if (expectedRaw is null) return null;
            from.Add(CalibrationMath.ToNormalized(measuredRaw[i], digitizer.MaxX, digitizer.MaxY));
            to.Add(CalibrationMath.ToNormalized(expectedRaw.Value, digitizer.MaxX, digitizer.MaxY));
        }
        return CalibrationMath.SolveAffine(from, to);
    }
}
