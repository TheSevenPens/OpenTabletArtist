using System;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PenDynamicsProcessorTests
{
    private static PenDynamicsProcessor Proc(PenDynamicsSettings s) => new() { Settings = s };

    // Smoothing settings are slider amounts; the EMA factor is the perceptual mapping of that amount.
    private static double F(double amount) => PenSmoothing.FactorFromAmount(amount);

    [Fact]
    public void NoSmoothing_IdentityCurve_IsPassthrough()
    {
        var p = Proc(PenDynamicsSettings.Default);
        Assert.Equal(0.5, p.ProcessPressure(0.5), 6);
    }

    [Fact]
    public void FirstSample_NotSmoothed_ThenLags()
    {
        var p = Proc(PenDynamicsSettings.Default with { PressureSmoothing = 0.5 });
        Assert.Equal(1.0, p.ProcessPressure(1.0), 6);     // stroke start: no previous
        // Ema(0, prev 1.0, f) = 1 + (1-f)*(0-1) = f  -> output stays near the old value
        Assert.Equal(F(0.5), p.ProcessPressure(0.0), 6);
    }

    [Fact]
    public void Reset_ClearsState_SoNextStrokeStartsCrisp()
    {
        var p = Proc(PenDynamicsSettings.Default with { PressureSmoothing = 0.5 });
        p.ProcessPressure(1.0);
        p.Reset();
        Assert.Equal(0.2, p.ProcessPressure(0.2), 6);     // fresh: returns the new value, no lag
    }

    [Fact]
    public void ResetPressure_DoesNotClearPosition()
    {
        var p = Proc(PenDynamicsSettings.Default with { PressureSmoothing = 0.5, PositionSmoothing = 0.5 });
        p.ProcessPosition(0, 0);
        p.ResetPressure();
        var (x, _) = p.ProcessPosition(1.0, 0);           // position state survived -> still lags
        Assert.Equal(1 - F(0.5), x, 6);
    }

    // softness 0.5 -> exponent 0.5 -> the curve is sqrt(x); nonlinear so the two orders differ.
    [Fact]
    public void Order_CurveThenSmooth_SmoothsTheCurvedValue()
    {
        var s = PenDynamicsSettings.Default with
        {
            Curve = PressureCurveSettings.Default with { Softness = 0.5 },
            PressureSmoothing = 0.5,
            SmoothAfterCurve = true,
        };
        var p = Proc(s);
        Assert.Equal(1.0, p.ProcessPressure(1.0), 6);     // sqrt(1)=1, no prev
        // curved sqrt(0)=0; Ema(0, prev 1.0, f) = f
        Assert.Equal(F(0.5), p.ProcessPressure(0.0), 6);
    }

    [Fact]
    public void Order_SmoothThenCurve_SmoothsTheRawInput()
    {
        var s = PenDynamicsSettings.Default with
        {
            Curve = PressureCurveSettings.Default with { Softness = 0.5 },
            PressureSmoothing = 0.5,
            SmoothAfterCurve = false,
        };
        var p = Proc(s);
        Assert.Equal(1.0, p.ProcessPressure(1.0), 6);     // smooth 1.0, then sqrt(1)=1
        // smooth raw -> f; then sqrt(f), which differs from curve-then-smooth's f
        Assert.Equal(Math.Sqrt(F(0.5)), p.ProcessPressure(0.0), 6);
    }

    [Fact]
    public void Position_SmoothsXandYIndependently()
    {
        var p = Proc(PenDynamicsSettings.Default with { PositionSmoothing = 0.5 });
        p.ProcessPosition(0, 0);
        var (x, y) = p.ProcessPosition(1.0, 2.0);
        Assert.Equal(1 - F(0.5), x, 6);
        Assert.Equal(2 * (1 - F(0.5)), y, 6);
    }

    [Fact]
    public void Output_IsClampedToUnitRange()
    {
        var s = PenDynamicsSettings.Default with { Curve = PressureCurveSettings.Default with { Maximum = 1.5 } };
        Assert.InRange(Proc(s).ProcessPressure(1.0), 0, 1);
    }

    // --- "what's affecting the pen" flags (#184) ---

    [Fact]
    public void Default_IsNoOp_AndNothingActive()
    {
        var s = PenDynamicsSettings.Default;
        Assert.True(s.IsNoOp);
        Assert.False(s.CurveShapesPressure);
        Assert.False(s.HasPressureSmoothing);
        Assert.False(s.HasPositionSmoothing);
    }

    [Fact]
    public void NonLinearCurve_CountsAsShapingPressure()
    {
        var s = PenDynamicsSettings.Default with { Curve = PressureCurveSettings.Default with { Softness = 0.3 } };
        Assert.True(s.CurveShapesPressure);
        Assert.False(s.IsNoOp);
    }

    [Fact]
    public void RemappedCurve_CountsAsShapingPressure()
    {
        var s = PenDynamicsSettings.Default with { Curve = PressureCurveSettings.Default with { Maximum = 0.8 } };
        Assert.True(s.CurveShapesPressure);
    }

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(0, 0.5)]
    public void SmoothingAmounts_AreReportedActive(double pressure, double position)
    {
        var s = PenDynamicsSettings.Default with { PressureSmoothing = pressure, PositionSmoothing = position };
        Assert.Equal(pressure > 0, s.HasPressureSmoothing);
        Assert.Equal(position > 0, s.HasPositionSmoothing);
        Assert.False(s.IsNoOp);
    }
}
