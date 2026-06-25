using OtdWindowsHelper.Domain;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class PenDynamicsProcessorTests
{
    private static PenDynamicsProcessor Proc(PenDynamicsSettings s) => new() { Settings = s };

    [Fact]
    public void NoSmoothing_IdentityCurve_IsPassthrough()
    {
        var p = Proc(PenDynamicsSettings.Default);
        Assert.Equal(0.5, p.ProcessPressure(0.5), 6);
    }

    [Fact]
    public void FirstSample_NotSmoothed_ThenLags()
    {
        var p = Proc(PenDynamicsSettings.Default with { PressureSmoothing = 0.75 });
        Assert.Equal(1.0, p.ProcessPressure(1.0), 6);   // stroke start: no previous
        // factor 0.75 -> alpha 0.25 -> next = 1.0 + 0.25*(0 - 1.0) = 0.75 (stays near the old value)
        Assert.Equal(0.75, p.ProcessPressure(0.0), 6);
    }

    [Fact]
    public void Reset_ClearsState_SoNextStrokeStartsCrisp()
    {
        var p = Proc(PenDynamicsSettings.Default with { PressureSmoothing = 0.75 });
        p.ProcessPressure(1.0);
        p.Reset();
        Assert.Equal(0.2, p.ProcessPressure(0.2), 6);   // fresh: returns the new value, no lag
    }

    [Fact]
    public void ResetPressure_DoesNotClearPosition()
    {
        var p = Proc(PenDynamicsSettings.Default with { PressureSmoothing = 0.75, PositionSmoothing = 0.75 });
        p.ProcessPosition(0, 0);
        p.ResetPressure();
        var (x, _) = p.ProcessPosition(1.0, 0);   // position state survived -> still lags
        Assert.Equal(0.25, x, 6);
    }

    // softness 0.5 -> exponent 0.5 -> the curve is sqrt(x); nonlinear so the two orders differ.
    [Fact]
    public void Order_CurveThenSmooth_SmoothsTheCurvedValue()
    {
        var s = PenDynamicsSettings.Default with
        {
            Curve = PressureCurveSettings.Default with { Softness = 0.5 },
            PressureSmoothing = 0.75,
            SmoothAfterCurve = true,
        };
        var p = Proc(s);
        Assert.Equal(1.0, p.ProcessPressure(1.0), 6);   // sqrt(1)=1, no prev
        // curved sqrt(0)=0; smooth: 1.0 + 0.25*(0 - 1.0) = 0.75
        Assert.Equal(0.75, p.ProcessPressure(0.0), 6);
    }

    [Fact]
    public void Order_SmoothThenCurve_SmoothsTheRawInput()
    {
        var s = PenDynamicsSettings.Default with
        {
            Curve = PressureCurveSettings.Default with { Softness = 0.5 },
            PressureSmoothing = 0.75,
            SmoothAfterCurve = false,
        };
        var p = Proc(s);
        Assert.Equal(1.0, p.ProcessPressure(1.0), 6);   // smooth 1.0, then sqrt(1)=1
        // smooth raw: 1.0 + 0.25*(0 - 1.0) = 0.75; then sqrt(0.75) -> differs from curve-then-smooth
        Assert.Equal(System.Math.Sqrt(0.75), p.ProcessPressure(0.0), 6);
    }

    [Fact]
    public void Position_SmoothsXandYIndependently()
    {
        var p = Proc(PenDynamicsSettings.Default with { PositionSmoothing = 0.75 });
        p.ProcessPosition(0, 0);
        var (x, y) = p.ProcessPosition(1.0, 2.0);
        Assert.Equal(0.25, x, 6);
        Assert.Equal(0.5, y, 6);
    }

    [Fact]
    public void Output_IsClampedToUnitRange()
    {
        var s = PenDynamicsSettings.Default with { Curve = PressureCurveSettings.Default with { Maximum = 1.5 } };
        Assert.InRange(Proc(s).ProcessPressure(1.0), 0, 1);
    }
}
