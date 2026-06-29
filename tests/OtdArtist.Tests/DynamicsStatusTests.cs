using OtdArtist.Domain;
using Xunit;

namespace OtdArtist.Tests;

public class DynamicsStatusTests
{
    private static PenDynamicsSettings WithCurve =>
        PenDynamicsSettings.Default with
        {
            Curve = PressureCurveSettings.Default with { Softness = 0.4 }
        };

    [Fact]
    public void Disabled_SaysOff_RegardlessOfDynamics()
    {
        Assert.Equal("Pen dynamics: off", DynamicsStatus.Describe(false, PenDynamicsSettings.Default));
        // Even non-default dynamics read as "off" when the filter isn't enabled.
        Assert.Equal("Pen dynamics: off", DynamicsStatus.Describe(false, WithCurve));
    }

    [Fact]
    public void EnabledButNoOp_SaysLinear()
    {
        Assert.Equal("Pen dynamics: on (behaves linear)",
            DynamicsStatus.Describe(true, PenDynamicsSettings.Default));
    }

    [Fact]
    public void Curve_OnlyCurveListed()
    {
        Assert.Equal("Affecting your pen: Pressure curve", DynamicsStatus.Describe(true, WithCurve));
    }

    [Fact]
    public void PressureSmoothing_OnlyListed()
    {
        var d = PenDynamicsSettings.Default with { PressureSmoothing = 0.5 };
        Assert.Equal("Affecting your pen: Pressure smoothing", DynamicsStatus.Describe(true, d));
    }

    [Fact]
    public void PositionSmoothing_OnlyListed()
    {
        var d = PenDynamicsSettings.Default with { PositionSmoothing = 0.5 };
        Assert.Equal("Affecting your pen: Position smoothing", DynamicsStatus.Describe(true, d));
    }

    [Fact]
    public void AllActive_ListedInCurvePressurePositionOrder()
    {
        var d = WithCurve with { PressureSmoothing = 0.3, PositionSmoothing = 0.6 };
        Assert.Equal("Affecting your pen: Pressure curve, Pressure smoothing, Position smoothing",
            DynamicsStatus.Describe(true, d));
    }
}
