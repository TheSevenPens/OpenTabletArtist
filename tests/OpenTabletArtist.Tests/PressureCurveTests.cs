using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PressureCurveTests
{
    private static PressureCurveSettings With(
        double softness = 0, double inMin = 0, double inMax = 1,
        double outMin = 0, double outMax = 1, PressureMinApproach approach = PressureMinApproach.Clamp)
        => new(softness, inMin, inMax, outMin, outMax, approach);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Default_IsIdentity(double x)
        => Assert.Equal(x, PressureCurve.Apply(x, PressureCurveSettings.Default), 6);

    [Fact]
    public void PositiveSoftness_IsConcave_LighterTouch()
        => Assert.True(PressureCurve.Apply(0.5, With(softness: 0.5)) > 0.5);

    [Fact]
    public void NegativeSoftness_IsConvex_HeavierTouch()
        => Assert.True(PressureCurve.Apply(0.5, With(softness: -0.5)) < 0.5);

    [Fact]
    public void OutputMaximum_CapsFullPressure()
        => Assert.Equal(0.5, PressureCurve.Apply(1.0, With(outMax: 0.5)), 6);

    [Fact]
    public void OutputMinimum_Clamp_HoldsFloorBelowInputMinimum()
        => Assert.Equal(0.2, PressureCurve.Apply(0.0, With(inMin: 0.3, outMin: 0.2)), 6);

    [Fact]
    public void Cut_ProducesDeadZoneBelowInputMinimum()
    {
        var s = With(inMin: 0.3, outMin: 0.2, approach: PressureMinApproach.Cut);
        Assert.Equal(0.0, PressureCurve.Apply(0.2, s), 6);     // inside dead zone
        Assert.True(PressureCurve.Apply(0.6, s) > 0.0);        // past it
    }

    [Fact]
    public void InputMaximum_ReachesFullOutputEarly()
    {
        var s = With(inMax: 0.5);
        Assert.Equal(1.0, PressureCurve.Apply(0.5, s), 6);     // half input → full output
        Assert.Equal(0.5, PressureCurve.Apply(0.25, s), 6);    // quarter input → half (linear within range)
    }

    [Fact]
    public void Output_IsAlwaysClampedToUnitRange()
    {
        Assert.InRange(PressureCurve.Apply(1.0, With(outMax: 1.5)), 0, 1);
        Assert.InRange(PressureCurve.Apply(-0.5, With()), 0, 1);
    }
}
