using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.Dynamics;

/// <summary>
/// OpenTabletDriver filter implementing the OTD Windows Helper pen dynamics (#92): the "Extended"
/// pressure curve plus EMA pressure/position smoothing. Because it runs in the daemon's pipeline,
/// drawing apps (Krita, CSP, …) see the remapped, smoothed input.
///
/// The math lives in <see cref="Domain.PressureCurve"/> + <see cref="PenDynamicsProcessor"/>
/// (source-shared with the app, so the editor preview matches and the logic is unit-tested there).
/// </summary>
[PluginName("OTD Windows Helper - Pen Dynamics")]
public class DynamicsFilter : IPositionedPipelineElement<IDeviceReport>
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
     ToolTip("EMA smoothing of pressure (amount 0 = off to 1 = max, perceptually scaled). Reduces pressure jitter at the cost of lag. Applies while drawing.")]
    public float PressureSmoothing { get; set; }

    [Property("Position Smoothing"), DefaultPropertyValue(0f),
     ToolTip("EMA smoothing of the pen position (amount 0 = off to 1 = max, perceptually scaled). Steadies wobbly lines at the cost of lag. Applies while drawing.")]
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

        if (value is ITabletReport report)
        {
            if (report.Pressure == 0)
            {
                // Hover / pen up: leave pressure 0 untouched (Output Minimum applies only when the pen
                // is down) and reset smoothing so the next stroke starts crisp. Smoothing only while
                // drawing — and resetting on lift — steadies stroke starts/ends without depending on
                // proximity reports (Slimy Scylla's "apply while drawing" default).
                _processor.Reset();
            }
            else
            {
                if (PositionSmoothing > 0)
                {
                    var (x, y) = _processor.ProcessPosition(report.Position.X, report.Position.Y);
                    report.Position = new Vector2((float)x, (float)y);
                }

                var max = Tablet?.Properties?.Specifications?.Pen?.MaxPressure ?? 0;
                if (max > 0)
                {
                    var outPressure = _processor.ProcessPressure(report.Pressure / (double)max);
                    report.Pressure = (uint)Math.Round(outPressure * max);
                }
            }
        }

        Emit?.Invoke(value);
    }
}
