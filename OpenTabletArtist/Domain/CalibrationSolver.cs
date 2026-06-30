using System.Collections.Generic;
using System.Numerics;

namespace OpenTabletArtist.Domain;

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

    /// <summary>Solve the correction as a perspective homography (#195) from 4+ corner taps; null if
    /// mismatched/degenerate. Same measured→expected normalized correspondences as <see cref="Solve"/>.</summary>
    public static Homography? SolveHomography(
        IReadOnlyList<Vector2> targetsDesktop,
        IReadOnlyList<Vector2> measuredRaw,
        TabletDigitizerSpec digitizer,
        MappingArea input,
        MappingArea output)
    {
        if (!BuildCorrespondences(targetsDesktop, measuredRaw, digitizer, input, output, 4, out var from, out var to))
            return null;
        return CalibrationMath.SolveHomography(from, to);
    }

    /// <summary>Solve a finer-grid correction (#196): per-node offset = expected − measured (normalized),
    /// over the regular grid the targets form in normalized tablet space. <paramref name="measuredRaw"/>
    /// is row-major (cols×rows). Null if mismatched/degenerate.</summary>
    public static CalibrationGrid? SolveGrid(
        IReadOnlyList<Vector2> targetsDesktop,
        IReadOnlyList<Vector2> measuredRaw,
        TabletDigitizerSpec digitizer,
        MappingArea input,
        MappingArea output,
        int cols,
        int rows)
    {
        if (cols < 2 || rows < 2 || targetsDesktop.Count != cols * rows) return null;
        if (!BuildCorrespondences(targetsDesktop, measuredRaw, digitizer, input, output, cols * rows, out var measured, out var expected))
            return null;

        var offsets = new Vector2[cols * rows];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < offsets.Length; i++)
        {
            offsets[i] = expected[i] - measured[i];           // correction at the node
            minX = Math.Min(minX, expected[i].X); maxX = Math.Max(maxX, expected[i].X);
            minY = Math.Min(minY, expected[i].Y); maxY = Math.Max(maxY, expected[i].Y);
        }
        if (maxX <= minX || maxY <= minY) return null;        // collapsed grid
        return new CalibrationGrid(cols, rows, minX, minY, maxX, maxY, offsets);
    }

    // Shared: turn target/measured pairs into normalized measured (from) + expected (to) correspondences.
    private static bool BuildCorrespondences(
        IReadOnlyList<Vector2> targetsDesktop, IReadOnlyList<Vector2> measuredRaw,
        TabletDigitizerSpec digitizer, MappingArea input, MappingArea output, int min,
        out List<Vector2> from, out List<Vector2> to)
    {
        from = new List<Vector2>();
        to = new List<Vector2>();
        if (targetsDesktop.Count != measuredRaw.Count || targetsDesktop.Count < min) return false;
        for (int i = 0; i < targetsDesktop.Count; i++)
        {
            var expectedRaw = AbsolutePositionMapper.MapFromDesktop(targetsDesktop[i], digitizer, input, output);
            if (expectedRaw is null) return false;
            from.Add(CalibrationMath.ToNormalized(measuredRaw[i], digitizer.MaxX, digitizer.MaxY));
            to.Add(CalibrationMath.ToNormalized(expectedRaw.Value, digitizer.MaxX, digitizer.MaxY));
        }
        return true;
    }
}
