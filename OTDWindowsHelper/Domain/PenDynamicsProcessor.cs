namespace OtdWindowsHelper.Domain;

/// <summary>
/// Full pen-dynamics configuration: the pressure <see cref="Curve"/> plus EMA smoothing amounts and
/// the curve/smooth ordering. This is what a tablet profile stores and the editor edits; the pure
/// curve math (<see cref="PressureCurveSettings"/>) is just the <see cref="Curve"/> part.
/// <para><see cref="PressureSmoothing"/> / <see cref="PositionSmoothing"/> are slider <em>amounts</em>
/// in [0,1] (0 = off), mapped to an EMA factor by <see cref="PenSmoothing.FactorFromAmount"/>.</para>
/// </summary>
public readonly record struct PenDynamicsSettings(
    PressureCurveSettings Curve,
    double PressureSmoothing,
    double PositionSmoothing,
    bool SmoothAfterCurve)
{
    public static PenDynamicsSettings Default { get; } =
        new(PressureCurveSettings.Default, 0, 0, SmoothAfterCurve: true);

    /// <summary>The pressure curve is non-linear/remapped (not the identity), so it bends how hard
    /// the pen registers. Used to tell the user the curve is actually affecting pressure (#184).</summary>
    public bool CurveShapesPressure => Curve != PressureCurveSettings.Default;

    /// <summary>Pressure smoothing is on (evens out pressure jitter).</summary>
    public bool HasPressureSmoothing => PressureSmoothing > 0;

    /// <summary>Position smoothing is on (steadies wobbly lines).</summary>
    public bool HasPositionSmoothing => PositionSmoothing > 0;

    /// <summary>Nothing actually alters the pen: linear curve and no smoothing.</summary>
    public bool IsNoOp => !CurveShapesPressure && !HasPressureSmoothing && !HasPositionSmoothing;
}

/// <summary>
/// Stateful per-stroke processor that applies the pressure curve + smoothing and position smoothing,
/// mirroring PenDynamicsLab's pipeline. Lives in Domain (source-shared with the OTD plugin) so the
/// ordering / reset / EMA behavior is unit-tested without the daemon. The plugin's filter owns one
/// instance and feeds it reports; the app could reuse it for a live preview.
///
/// State is reset on pen-out (<see cref="Reset"/>) — the OTD analogue of PenDynamicsLab resetting
/// when the pen leaves proximity — so a new stroke doesn't lerp in from the previous one's end.
/// </summary>
public sealed class PenDynamicsProcessor
{
    private double? _smoothedPressure;
    private double? _smoothedX;
    private double? _smoothedY;

    public PenDynamicsSettings Settings { get; set; } = PenDynamicsSettings.Default;

    /// <summary>Clear all smoothing state (call on pen-out / proximity-out).</summary>
    public void Reset()
    {
        _smoothedPressure = null;
        _smoothedX = null;
        _smoothedY = null;
    }

    /// <summary>Clear only the pressure smoothing state (call when the pen lifts to zero pressure,
    /// so the next press starts crisp).</summary>
    public void ResetPressure() => _smoothedPressure = null;

    /// <summary>Map a normalized input pressure [0,1] through curve + smoothing (honoring
    /// <see cref="PenDynamicsSettings.SmoothAfterCurve"/>). Returns a normalized output [0,1].</summary>
    public double ProcessPressure(double normalized)
    {
        double factor = PenSmoothing.FactorFromAmount(Settings.PressureSmoothing);
        double result;
        if (Settings.SmoothAfterCurve)
        {
            double curved = PressureCurve.Apply(normalized, Settings.Curve);
            result = PenSmoothing.Ema(curved, _smoothedPressure, factor);
            _smoothedPressure = result;
        }
        else
        {
            double smoothed = PenSmoothing.Ema(normalized, _smoothedPressure, factor);
            _smoothedPressure = smoothed;
            result = PressureCurve.Apply(smoothed, Settings.Curve);
        }
        return result < 0 ? 0 : result > 1 ? 1 : result;
    }

    /// <summary>Smooth a raw position (tablet units). Returns the smoothed coordinates.</summary>
    public (double X, double Y) ProcessPosition(double x, double y)
    {
        double factor = PenSmoothing.FactorFromAmount(Settings.PositionSmoothing);
        double sx = PenSmoothing.Ema(x, _smoothedX, factor);
        double sy = PenSmoothing.Ema(y, _smoothedY, factor);
        _smoothedX = sx;
        _smoothedY = sy;
        return (sx, sy);
    }
}
