using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PenSmoothingTests
{
    [Fact]
    public void FactorZero_IsPassthrough()
        => Assert.Equal(0.7, PenSmoothing.Ema(0.7, previous: 0.2, factor: 0), 6);

    [Fact]
    public void NoPrevious_ReturnsCurrent()
        => Assert.Equal(0.7, PenSmoothing.Ema(0.7, previous: null, factor: 0.9), 6);

    [Fact]
    public void Blends_TowardPrevious_ByAlpha()
    {
        // factor 0.75 -> alpha 0.25 -> next = 0 + 0.25*(1-0) = 0.25
        Assert.Equal(0.25, PenSmoothing.Ema(1.0, previous: 0.0, factor: 0.75), 6);
    }

    [Fact]
    public void Factor_IsClampedToMax()
    {
        // factor 5 clamps to 0.99 -> alpha 0.01 -> next = 0 + 0.01*1 = 0.01
        Assert.Equal(0.01, PenSmoothing.Ema(1.0, previous: 0.0, factor: 5.0), 6);
    }

    [Fact]
    public void HeavierFactor_LagsMore()
    {
        double light = PenSmoothing.Ema(1.0, previous: 0.0, factor: 0.2);
        double heavy = PenSmoothing.Ema(1.0, previous: 0.0, factor: 0.9);
        Assert.True(heavy < light); // more smoothing = closer to the old value
    }

    [Fact]
    public void FactorFromAmount_ZeroIsOff_OneIsMax()
    {
        Assert.Equal(0, PenSmoothing.FactorFromAmount(0));
        Assert.Equal(PenSmoothing.MaxFactor, PenSmoothing.FactorFromAmount(1), 6);
        Assert.Equal(0, PenSmoothing.FactorFromAmount(-0.5)); // out of range clamps off
    }

    [Fact]
    public void FactorFromAmount_IsMonotonicAndNeverFreezes()
    {
        double prev = -1;
        for (double a = 0; a <= 1.0001; a += 0.1)
        {
            double f = PenSmoothing.FactorFromAmount(a);
            Assert.InRange(f, 0, PenSmoothing.MaxFactor);
            Assert.True(f >= prev - 1e-9); // non-decreasing
            prev = f;
        }
    }

    [Fact]
    public void FactorFromAmount_MatchesSlimyScyllaCurve()
    {
        // amount^(0.02/amount): 0.1 -> 0.1^0.2 ~ 0.631
        Assert.Equal(System.Math.Pow(0.1, 0.2), PenSmoothing.FactorFromAmount(0.1), 6);
    }

    [Fact]
    public void SmoothingCeilings_AreWithinRange_AndPressureIsHigher()
    {
        // Both sliders stop short of the full range (#487 position 0.25, #496 pressure 0.50); pressure
        // tolerates more smoothing than position, so its ceiling is the higher of the two.
        Assert.Equal(0.25, PenSmoothing.MaxPositionSmoothingAmount, 6);
        Assert.Equal(0.50, PenSmoothing.MaxPressureSmoothingAmount, 6);
        Assert.InRange(PenSmoothing.MaxPositionSmoothingAmount, 0, 1);
        Assert.InRange(PenSmoothing.MaxPressureSmoothingAmount, 0, 1);
        Assert.True(PenSmoothing.MaxPressureSmoothingAmount > PenSmoothing.MaxPositionSmoothingAmount);
    }
}
