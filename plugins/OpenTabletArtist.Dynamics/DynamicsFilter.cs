using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// OpenTabletDriver filter implementing the OpenTabletArtist pen dynamics (#92): the "Extended"
/// pressure curve plus EMA pressure/position smoothing. Because it runs in the daemon's pipeline,
/// drawing apps (Krita, CSP, …) see the remapped, smoothed input.
///
/// The math lives in <see cref="Domain.PressureCurve"/> + <see cref="PenDynamicsProcessor"/>
/// (source-shared with the app, so the editor preview matches and the logic is unit-tested there).
/// </summary>
[PluginName("OpenTabletArtist - Pen Dynamics")]
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

        // Skip entirely when MaxPressure is unknown (some custom configs) — normalizing needs it, and
        // we must not zero pressure just because we can't process it (Codex #106). Pass through.
        if (value is ITabletReport report && (Tablet?.Properties?.Specifications?.Pen?.MaxPressure ?? 0) > 0)
        {
            var max = Tablet!.Properties!.Specifications!.Pen!.MaxPressure;
            var settings = _processor.Settings;
            double norm = report.Pressure / (double)max;

            // "Drawing" means the curve actually emits pressure. Both hover (raw 0) and the Cut/Clamp
            // dead zone (raw > 0 but mapped to 0) count as not-drawing: zero the output and reset
            // smoothing so the first real sample starts crisp — no lag/fly-in carried over from the
            // pre-stroke approach (Codex #106). Position is smoothed only while drawing, so a pen-out
            // coordinate is never folded into the next stroke either.
            bool drawing = report.Pressure > 0
                           && Domain.PressureCurve.Apply(norm, settings.Curve) > 0;

            if (!drawing)
            {
                report.Pressure = 0;
                _processor.Reset();
            }
            else
            {
                if (PositionSmoothing > 0)
                {
                    var (x, y) = _processor.ProcessPosition(report.Position.X, report.Position.Y);
                    report.Position = new Vector2((float)x, (float)y);
                }

                var outPressure = _processor.ProcessPressure(norm);
                report.Pressure = (uint)Math.Round(outPressure * max);
            }
        }

        Emit?.Invoke(value);
    }
}
