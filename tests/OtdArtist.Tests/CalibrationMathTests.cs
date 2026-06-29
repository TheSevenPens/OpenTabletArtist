using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OtdArtist.Domain;
using Xunit;

namespace OtdArtist.Tests;

public class CalibrationMathTests
{
    // Four inset-corner targets in normalized tablet space.
    private static readonly Vector2[] Corners =
    {
        new(-0.8f, -0.8f), new(0.8f, -0.8f), new(0.8f, 0.8f), new(-0.8f, 0.8f),
    };

    private static void AssertMapsAllNear(Matrix3x2 m, IReadOnlyList<Vector2> from, IReadOnlyList<Vector2> to, int precision = 3)
    {
        for (int i = 0; i < from.Count; i++)
        {
            var p = Vector2.Transform(from[i], m);
            Assert.Equal(to[i].X, p.X, precision);
            Assert.Equal(to[i].Y, p.Y, precision);
        }
    }

    [Fact]
    public void Normalize_RoundTrips()
    {
        var raw = new Vector2(250, 750);
        var n = CalibrationMath.ToNormalized(raw, 1000, 1000);
        var back = CalibrationMath.FromNormalized(n, 1000, 1000);
        Assert.Equal(raw.X, back.X, 3);
        Assert.Equal(raw.Y, back.Y, 3);
    }

    [Fact]
    public void SolveAffine_Identity()
    {
        var m = CalibrationMath.SolveAffine(Corners, Corners);
        Assert.NotNull(m);
        AssertMapsAllNear(m!.Value, Corners, Corners);
    }

    [Fact]
    public void SolveAffine_PureTranslation()
    {
        var offset = new Vector2(0.1f, -0.05f);
        var to = Corners.Select(c => c + offset).ToArray();
        var m = CalibrationMath.SolveAffine(Corners, to);
        Assert.NotNull(m);
        AssertMapsAllNear(m!.Value, Corners, to);
    }

    [Fact]
    public void SolveAffine_NonUniformScale()
    {
        var to = Corners.Select(c => new Vector2(c.X * 1.2f, c.Y * 0.9f)).ToArray();
        var m = CalibrationMath.SolveAffine(Corners, to);
        Assert.NotNull(m);
        AssertMapsAllNear(m!.Value, Corners, to);
    }

    [Fact]
    public void SolveAffine_Rotation()
    {
        var rot = Matrix3x2.CreateRotation((float)(5 * Math.PI / 180)); // 5°
        var to = Corners.Select(c => Vector2.Transform(c, rot)).ToArray();
        var m = CalibrationMath.SolveAffine(Corners, to);
        Assert.NotNull(m);
        AssertMapsAllNear(m!.Value, Corners, to, precision: 3);
    }

    [Fact]
    public void SolveAffine_LeastSquares_OnNoisyPoints_FitsClosely()
    {
        // Target is a small offset+scale; each tap is perturbed slightly. The fit should map the
        // measured points near their targets (residuals small), proving the least-squares averaging.
        var offset = new Vector2(0.03f, -0.02f);
        var to = Corners.Select(c => new Vector2(c.X * 1.05f + offset.X, c.Y * 1.05f + offset.Y)).ToArray();
        var noisy = new[]
        {
            Corners[0] + new Vector2(0.01f, -0.01f),
            Corners[1] + new Vector2(-0.008f, 0.012f),
            Corners[2] + new Vector2(0.006f, 0.004f),
            Corners[3] + new Vector2(-0.005f, -0.007f),
        };
        var m = CalibrationMath.SolveAffine(noisy, to);
        Assert.NotNull(m);
        for (int i = 0; i < noisy.Length; i++)
        {
            var p = Vector2.Transform(noisy[i], m!.Value);
            Assert.True((p - to[i]).Length() < 0.03f, $"residual too large at {i}");
        }
    }

    [Fact]
    public void SolveAffine_TooFewPoints_ReturnsNull()
        => Assert.Null(CalibrationMath.SolveAffine(new[] { Corners[0], Corners[1] }, new[] { Corners[0], Corners[1] }));

    [Fact]
    public void SolveAffine_CollinearPoints_ReturnsNull()
    {
        var from = new[] { new Vector2(0, 0), new Vector2(1, 1), new Vector2(2, 2), new Vector2(3, 3) };
        var to = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 0), new Vector2(3, 0) };
        Assert.Null(CalibrationMath.SolveAffine(from, to));
    }

    [Fact]
    public void SolveAffine_DuplicatePoints_ReturnsNull()
    {
        var p = new Vector2(0.5f, 0.5f);
        var from = new[] { p, p, p, p };
        Assert.Null(CalibrationMath.SolveAffine(from, Corners));
    }
}
