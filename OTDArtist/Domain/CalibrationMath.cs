using System;
using System.Collections.Generic;
using System.Numerics;

namespace OtdArtist.Domain;

/// <summary>
/// Pure math for pointer calibration (#127). A calibration is a 2D affine in <em>normalized tablet
/// space</em> (each axis in -1..1, matching OTD/Kuuuube conventions) stored as a
/// <see cref="Matrix3x2"/>. The filter normalizes each report's raw position, applies the affine,
/// and denormalizes — so the correction is resolution-independent.
///
/// The transform is fit by least squares from the captured taps: for each on-screen target we have
/// the <em>measured</em> normalized position (where the pen actually was) and the <em>expected</em>
/// normalized position (where it should have been). Source-shared with the OTD filter so the daemon
/// and the app apply identical math; unit-tested here.
/// </summary>
public static class CalibrationMath
{
    /// <summary>Raw tablet units → normalized -1..1 (per axis). Requires positive maxima.</summary>
    public static Vector2 ToNormalized(Vector2 raw, float maxX, float maxY) =>
        new(raw.X / maxX * 2f - 1f, raw.Y / maxY * 2f - 1f);

    /// <summary>Normalized -1..1 → raw tablet units (inverse of <see cref="ToNormalized"/>).</summary>
    public static Vector2 FromNormalized(Vector2 n, float maxX, float maxY) =>
        new((n.X + 1f) / 2f * maxX, (n.Y + 1f) / 2f * maxY);

    /// <summary>
    /// Least-squares affine mapping <paramref name="from"/>[i] → <paramref name="to"/>[i]. With
    /// <see cref="Matrix3x2"/>'s convention the model is
    /// <c>x' = M11·x + M21·y + M31</c>, <c>y' = M12·x + M22·y + M32</c>, so X and Y each reduce to an
    /// independent 3-unknown normal-equation system over the same design matrix.
    /// Returns null when there are fewer than 3 pairs or the points are degenerate (collinear /
    /// duplicated → singular normal matrix), so callers reject the capture instead of applying garbage.
    /// </summary>
    public static Matrix3x2? SolveAffine(IReadOnlyList<Vector2> from, IReadOnlyList<Vector2> to)
    {
        if (from.Count != to.Count || from.Count < 3) return null;

        // Normal matrix N = Aᵀ·A for the design rows [x, y, 1]; symmetric 3×3.
        double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = from.Count;
        double txX = 0, tyX = 0, t1X = 0; // Aᵀ·xTarget
        double txY = 0, tyY = 0, t1Y = 0; // Aᵀ·yTarget
        for (int i = 0; i < from.Count; i++)
        {
            double x = from[i].X, y = from[i].Y;
            sxx += x * x; sxy += x * y; sx += x; syy += y * y; sy += y;
            double xt = to[i].X, yt = to[i].Y;
            txX += x * xt; tyX += y * xt; t1X += xt;
            txY += x * yt; tyY += y * yt; t1Y += yt;
        }

        // N = [[sxx, sxy, sx], [sxy, syy, sy], [sx, sy, n]]
        double det =
            sxx * (syy * n - sy * sy)
            - sxy * (sxy * n - sy * sx)
            + sx * (sxy * sy - syy * sx);
        if (Math.Abs(det) < 1e-9) return null; // degenerate (collinear / duplicate taps)

        // Inverse of the symmetric 3×3 (cofactors / det).
        double i00 = (syy * n - sy * sy) / det;
        double i01 = (sx * sy - sxy * n) / det;
        double i02 = (sxy * sy - sx * syy) / det;
        double i11 = (sxx * n - sx * sx) / det;
        double i12 = (sx * sxy - sxx * sy) / det;
        double i22 = (sxx * syy - sxy * sxy) / det;
        // (symmetric: i10=i01, i20=i02, i21=i12)

        // Solve for X coefficients (a=M11, b=M21, c=M31) and Y coefficients (M12, M22, M32).
        double m11 = i00 * txX + i01 * tyX + i02 * t1X;
        double m21 = i01 * txX + i11 * tyX + i12 * t1X;
        double m31 = i02 * txX + i12 * tyX + i22 * t1X;
        double m12 = i00 * txY + i01 * tyY + i02 * t1Y;
        double m22 = i01 * txY + i11 * tyY + i12 * t1Y;
        double m32 = i02 * txY + i12 * tyY + i22 * t1Y;

        return new Matrix3x2((float)m11, (float)m12, (float)m21, (float)m22, (float)m31, (float)m32);
    }
}
