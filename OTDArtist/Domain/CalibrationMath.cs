using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    /// <summary>
    /// Least-squares projective homography mapping <paramref name="from"/>[i] → <paramref name="to"/>[i]
    /// (8 DOF). Unlike <see cref="SolveAffine"/> this also corrects keystone / perspective parallax,
    /// at the cost of needing the same 4 well-spread correspondences. Returns null with fewer than 4
    /// pairs or when the system is singular (collinear / duplicate points).
    /// </summary>
    public static Homography? SolveHomography(IReadOnlyList<Vector2> from, IReadOnlyList<Vector2> to)
    {
        if (from.Count != to.Count || from.Count < 4) return null;

        // Linearize u = (h11 x + h12 y + h13)/(h31 x + h32 y + 1), likewise v, with h33 fixed to 1.
        // Two rows per correspondence over unknowns [h11,h12,h13,h21,h22,h23,h31,h32].
        int n = from.Count;
        var a = new double[2 * n, 8];
        var b = new double[2 * n];
        for (int i = 0; i < n; i++)
        {
            double x = from[i].X, y = from[i].Y, u = to[i].X, v = to[i].Y;
            int r0 = 2 * i, r1 = r0 + 1;
            a[r0, 0] = x; a[r0, 1] = y; a[r0, 2] = 1; a[r0, 6] = -u * x; a[r0, 7] = -u * y; b[r0] = u;
            a[r1, 3] = x; a[r1, 4] = y; a[r1, 5] = 1; a[r1, 6] = -v * x; a[r1, 7] = -v * y; b[r1] = v;
        }

        // Normal equations: (AᵀA) h = Aᵀb — an 8×8 symmetric solve (exact for 4 points, LS for more).
        var ata = new double[8, 8];
        var atb = new double[8];
        for (int j = 0; j < 8; j++)
        {
            for (int k = 0; k < 8; k++)
            {
                double s = 0;
                for (int i = 0; i < 2 * n; i++) s += a[i, j] * a[i, k];
                ata[j, k] = s;
            }
            double sb = 0;
            for (int i = 0; i < 2 * n; i++) sb += a[i, j] * b[i];
            atb[j] = sb;
        }

        var h = SolveLinearSystem(ata, atb);
        if (h == null) return null;
        return new Homography(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1f);
    }

    /// <summary>Gaussian elimination with partial pivoting for a small dense N×N system; null if singular.</summary>
    public static double[]? SolveLinearSystem(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            if (Math.Abs(m[piv, col]) < 1e-12) return null;
            if (piv != col)
            {
                for (int c = 0; c < n; c++) (m[col, c], m[piv, c]) = (m[piv, c], m[col, c]);
                (x[col], x[piv]) = (x[piv], x[col]);
            }
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r, col] / m[col, col];
                for (int c = col; c < n; c++) m[r, c] -= f * m[col, c];
                x[r] -= f * x[col];
            }
        }
        for (int i = 0; i < n; i++) x[i] /= m[i, i];
        return x;
    }
}

/// <summary>
/// A 3×3 projective transform (row-major) in normalized tablet space. <c>w = H31·x + H32·y + H33</c>;
/// <c>u = (H11·x + H12·y + H13)/w</c>, <c>v = (H21·x + H22·y + H23)/w</c>. Source-shared with the OTD
/// filter (kept in CalibrationMath.cs so it's already linked into the plugin).
/// </summary>
public readonly record struct Homography(
    float H11, float H12, float H13,
    float H21, float H22, float H23,
    float H31, float H32, float H33)
{
    public Vector2 Project(Vector2 p)
    {
        float w = H31 * p.X + H32 * p.Y + H33;
        if (Math.Abs(w) < 1e-9f) return p; // degenerate → leave untouched
        return new Vector2(
            (H11 * p.X + H12 * p.Y + H13) / w,
            (H21 * p.X + H22 * p.Y + H23) / w);
    }

    /// <summary>Round-trippable CSV ("H11,H12,…,H33"), used to store the transform in the filter.</summary>
    public string ToCsv() => string.Join(",", new[] { H11, H12, H13, H21, H22, H23, H31, H32, H33 }
        .Select(f => f.ToString(CultureInfo.InvariantCulture)));

    public static Homography? TryParse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var p = csv.Split(',');
        if (p.Length != 9) return null;
        var v = new float[9];
        for (int i = 0; i < 9; i++)
            if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
        return new Homography(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]);
    }
}

/// <summary>
/// A regular grid of per-node <em>offsets</em> in normalized tablet space, applied by bilinear
/// interpolation — corrects localized / non-uniform warp a single global transform can't (#196,
/// modeled on BetterCalibrator). The grid spans [<see cref="MinX"/>,<see cref="MaxX"/>] ×
/// [<see cref="MinY"/>,<see cref="MaxY"/>] with <see cref="Cols"/>×<see cref="Rows"/> nodes;
/// <see cref="Offsets"/> is row-major (index = row·Cols + col).
/// </summary>
public sealed class CalibrationGrid
{
    public int Cols { get; }
    public int Rows { get; }
    public float MinX { get; }
    public float MinY { get; }
    public float MaxX { get; }
    public float MaxY { get; }
    public Vector2[] Offsets { get; }

    public CalibrationGrid(int cols, int rows, float minX, float minY, float maxX, float maxY, Vector2[] offsets)
    {
        Cols = cols; Rows = rows; MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; Offsets = offsets;
    }

    /// <summary>Add the bilinearly-interpolated offset at <paramref name="p"/> (clamped to the grid edges).</summary>
    public Vector2 Apply(Vector2 p)
    {
        if (Cols < 2 || Rows < 2 || Offsets.Length != Cols * Rows) return p;
        float fx = MaxX > MinX ? (p.X - MinX) / (MaxX - MinX) * (Cols - 1) : 0;
        float fy = MaxY > MinY ? (p.Y - MinY) / (MaxY - MinY) * (Rows - 1) : 0;
        fx = Math.Clamp(fx, 0, Cols - 1);
        fy = Math.Clamp(fy, 0, Rows - 1);
        int c0 = (int)Math.Floor(fx), r0 = (int)Math.Floor(fy);
        int c1 = Math.Min(c0 + 1, Cols - 1), r1 = Math.Min(r0 + 1, Rows - 1);
        float tx = fx - c0, ty = fy - r0;

        Vector2 o00 = Offsets[r0 * Cols + c0], o10 = Offsets[r0 * Cols + c1];
        Vector2 o01 = Offsets[r1 * Cols + c0], o11 = Offsets[r1 * Cols + c1];
        var top = Vector2.Lerp(o00, o10, tx);
        var bot = Vector2.Lerp(o01, o11, tx);
        return p + Vector2.Lerp(top, bot, ty);
    }

    /// <summary>CSV: "cols,rows,minX,minY,maxX,maxY,off0x,off0y,off1x,off1y,…" (round-trippable).</summary>
    public string ToCsv()
    {
        var parts = new List<string>(6 + Offsets.Length * 2)
        {
            Cols.ToString(CultureInfo.InvariantCulture), Rows.ToString(CultureInfo.InvariantCulture),
            MinX.ToString(CultureInfo.InvariantCulture), MinY.ToString(CultureInfo.InvariantCulture),
            MaxX.ToString(CultureInfo.InvariantCulture), MaxY.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var o in Offsets)
        {
            parts.Add(o.X.ToString(CultureInfo.InvariantCulture));
            parts.Add(o.Y.ToString(CultureInfo.InvariantCulture));
        }
        return string.Join(",", parts);
    }

    public static CalibrationGrid? TryParse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var p = csv.Split(',');
        if (p.Length < 6) return null;
        float F(int i) => float.Parse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture);
        try
        {
            int cols = int.Parse(p[0], CultureInfo.InvariantCulture);
            int rows = int.Parse(p[1], CultureInfo.InvariantCulture);
            if (cols < 2 || rows < 2 || p.Length != 6 + cols * rows * 2) return null;
            float minX = F(2), minY = F(3), maxX = F(4), maxY = F(5);
            var offsets = new Vector2[cols * rows];
            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = new Vector2(F(6 + i * 2), F(7 + i * 2));
            return new CalibrationGrid(cols, rows, minX, minY, maxX, maxY, offsets);
        }
        catch (FormatException) { return null; }
        catch (OverflowException) { return null; }
    }
}
