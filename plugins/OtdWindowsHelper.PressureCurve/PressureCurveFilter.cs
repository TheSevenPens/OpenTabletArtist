using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.PressureCurve;

/// <summary>
/// OpenTabletDriver filter implementing the OTD Windows Helper pen dynamics (#92): the "Extended"
/// pressure curve plus EMA pressure/position smoothing. Because it runs in the daemon's pipeline,
/// drawing apps (Krita, CSP, …) see the remapped, smoothed input.
///
/// The math lives in <see cref="Domain.PressureCurve"/> + <see cref="PenDynamicsProcessor"/>
/// (source-shared with the app, so the editor preview matches and the logic is unit-tested there).
/// The type name is kept stable so existing profiles keep resolving.
/// </summary>
[PluginName("OTD Windows Helper - Pen Dynamics")]
public class PressureCurveFilter : IPositionedPipelineElement<IDeviceReport>
{
    [Property("Softness"), DefaultPropertyValue(0f),
     ToolTip("Curve shape: 0 = linear, positive = lighter touch (concave), negative = heavier (convex). Range -0.9 to 0.9.")]
    public float Softness { get; set; }

    [Property("Input Minimum"), DefaultPropertyValue(0f),
     ToolTip("Pressure (0-1) below this is remapped to Output Minimum — or cut to zero if 'Cut below minimum' is on.")]
    public float InputMinimum { get; set; }

    [Property("Input Maximum"), DefaultPropertyValue(1f),
     ToolTip("Pressure (0-1) above this is clamped to Output Maximum (lets you reach full output before pressing all the way).")]
    public float InputMaximum { get; set; } = 1f;

    [Property("Output Minimum"), DefaultPropertyValue(0f),
     ToolTip("The output pressure (0-1) at the input minimum.")]
    public float Minimum { get; set; }

    [Property("Output Maximum"), DefaultPropertyValue(1f),
     ToolTip("The output pressure (0-1) at full input. Lower it to cap how hard the pen registers.")]
    public float Maximum { get; set; } = 1f;

    [Property("Cut below minimum"), DefaultPropertyValue(false),
     ToolTip("When on, pressure below Input Minimum produces zero output (a dead zone) instead of holding at Output Minimum.")]
    public bool CutBelowMinimum { get; set; }

    [Property("Pressure Smoothing"), DefaultPropertyValue(0f),
     ToolTip("EMA smoothing of pressure (0 = off, up to 0.99 = heavy). Reduces pressure jitter at the cost of lag.")]
    public float PressureSmoothing { get; set; }

    [Property("Position Smoothing"), DefaultPropertyValue(0f),
     ToolTip("EMA smoothing of the pen position (0 = off, up to 0.99 = heavy). Steadies wobbly lines at the cost of lag.")]
    public float PositionSmoothing { get; set; }

    [Property("Smooth after curve"), DefaultPropertyValue(true),
     ToolTip("On: apply the curve, then smooth (default). Off: smooth the raw pressure, then apply the curve.")]
    public bool SmoothAfterCurve { get; set; } = true;

    [TabletReference]
    public TabletReference? Tablet { get; set; }

    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport>? Emit;

    private readonly PenDynamicsProcessor _processor = new();

    public void Consume(IDeviceReport value)
    {
        _processor.Settings = new PenDynamicsSettings(
            new PressureCurveSettings(
                Softness, InputMinimum, InputMaximum, Minimum, Maximum,
                CutBelowMinimum ? PressureMinApproach.Cut : PressureMinApproach.Clamp),
            PressureSmoothing, PositionSmoothing, SmoothAfterCurve);

        // Pen left the sensor — reset smoothing so the next stroke doesn't lerp in from here.
        if (value is IProximityReport { NearProximity: false })
            _processor.Reset();

        // Position smoothing (hover or contact). Skip the no-op when off so we don't disturb reports.
        if (PositionSmoothing > 0 && value is IAbsolutePositionReport posReport)
        {
            var (x, y) = _processor.ProcessPosition(posReport.Position.X, posReport.Position.Y);
            posReport.Position = new Vector2((float)x, (float)y);
        }

        if (value is ITabletReport report)
        {
            // Leave a raw zero (hover / no contact) untouched — an Output Minimum > 0 must only apply
            // once the pen is down — and reset pressure smoothing so the next press starts crisp.
            if (report.Pressure == 0)
            {
                _processor.ResetPressure();
            }
            else
            {
                var max = Tablet?.Properties?.Specifications?.Pen?.MaxPressure ?? 0;
                if (max > 0)
                {
                    var y = _processor.ProcessPressure(report.Pressure / (double)max);
                    report.Pressure = (uint)Math.Round(y * max);
                }
            }
        }

        Emit?.Invoke(value);
    }
}
