using System;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PenDynamicsProcessorTests
{
    private static PenDynamicsProcessor Proc(PenDynamicsSettings s) => new() { Settings = s };

    // Smoothing settings are slider amounts; the EMA factor is the perceptual mapping of that amount.
    private static double F(double amount) => PenSmoothing.FactorFromAmount(amount);

    // IsDrawing is the one definition of "a stroke" shared by the daemon's filter and the live preview.
    [Theory]
    [InlineData(0.0, false)]     // hover with no pressure
    [InlineData(0.0013, false)]  // hover pressure under the dead zone: the curve maps it to 0
    [InlineData(0.06, false)]    // exactly at the input minimum: still 0 out
    [InlineData(0.07, true)]     // just past it
    [InlineData(1.0, true)]
    public void IsDrawing_RespectsTheDeadZone(double pressure, bool expected)
    {
        var curve = PressureCurveSettings.Default with { InputMinimum = 0.06 };
        var p = Proc(PenDynamicsSettings.Default with { Curve = curve });
        Assert.Equal(expected, p.IsDrawing(pressure));
    }

    [Fact]
    public void IsDrawing_RaisedFloor_StillDrawsAtAnyPressure()
    {
        // Clamp with a non-zero output minimum emits pressure for every pressed sample, so none is a dead zone.
        var curve = PressureCurveSettings.Default with { InputMinimum = 0.06, Minimum = 0.1 };
        Assert.True(Proc(PenDynamicsSettings.Default with { Curve = curve }).IsDrawing(0.001));
    }

    // The regression behind the preview's "tail": smoothing leaves a decaying positive value after a stroke.
    // The processor alone keeps emitting it for samples the curve maps to 0; IsDrawing is what lets a
    // caller cut it (and Reset) instead, so the next press starts crisp.
    [Fact]
    public void SmoothingTail_IsPositiveWithoutTheGate_AndResetCutsIt()
    {
        var curve = PressureCurveSettings.Default with { InputMinimum = 0.06 };
        var p = Proc(PenDynamicsSettings.Default with { Curve = curve, PressureSmoothing = 0.5 });
        for (var i = 0; i < 60; i++) p.ProcessPressure(0.6);          // a stroke

        Assert.False(p.IsDrawing(0.0013));                             // pen now hovering with pressure
        Assert.True(p.ProcessPressure(0.0013) > 0);                    // ungated, the EMA tail is still > 0

        p.Reset();                                                     // what a gated caller does instead
        Assert.Equal(0.0, p.ProcessPressure(0.0013), 9);               // and no tail is carried over
    }

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

    // ── ProcessSample: what a drawing app would get for one raw sample ─────────────────────────────

    [Fact]
    public void ProcessSample_WhileDrawing_IsTheCurveAndSmoothing()
    {
        var s = PenDynamicsSettings.Default with { Curve = PressureCurveSettings.Default with { Softness = -0.6 } };
        var a = Proc(s); var b = Proc(s);
        Assert.Equal(b.ProcessPressure(0.5), a.ProcessSample(0.5), 9);
        Assert.NotEqual(0.5, a.ProcessSample(0.5), 3);                  // and the curve really changed it
    }

    [Fact]
    public void ProcessSample_NotDrawing_IsZero_AndTheNextPressStartsCrisp()
    {
        var curve = PressureCurveSettings.Default with { InputMinimum = 0.06 };
        var p = Proc(PenDynamicsSettings.Default with { Curve = curve, PressureSmoothing = 0.5 });
        for (var i = 0; i < 60; i++) p.ProcessSample(0.6);                // a stroke, smoothing now primed at ~0.6

        Assert.Equal(0.0, p.ProcessSample(0.0013));                       // hovering with pressure under the dead zone
        // The press after it must not lerp in from where the last stroke ended.
        var fresh = Proc(PenDynamicsSettings.Default with { Curve = curve, PressureSmoothing = 0.5 });
        Assert.Equal(fresh.ProcessSample(0.2), p.ProcessSample(0.2), 9);
    }

    [Fact]
    public void ProcessSample_WithDefaultSettings_IsThePressureItself()
    {
        var p = Proc(PenDynamicsSettings.Default);
        Assert.Equal(0.37, p.ProcessSample(0.37), 9);
        Assert.Equal(0.0, p.ProcessSample(0.0));
    }
}
