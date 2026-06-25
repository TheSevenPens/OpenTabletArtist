using OtdWindowsHelper.Domain;
using Xunit;

namespace OtdWindowsHelper.Tests;

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
}
