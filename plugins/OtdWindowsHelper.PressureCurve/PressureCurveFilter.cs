using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.PressureCurve;

/// <summary>
/// OpenTabletDriver filter that remaps pen pressure through the "Extended" curve (#92). Because it
/// runs in the daemon's pipeline, drawing apps (Krita, CSP, …) see the remapped pressure.
///
/// The actual math lives in <see cref="Domain.PressureCurve"/> (source-shared with the app, so the
/// editor preview matches exactly). Stateless for now; a smoothing stage can be added after the
/// curve later (curve-then-smooth).
/// </summary>
[PluginName("OTD Windows Helper - Pressure Curve")]
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

    [TabletReference]
    public TabletReference? Tablet { get; set; }

    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport>? Emit;

    public void Consume(IDeviceReport value)
    {
        // Leave a raw zero (hover / no contact) untouched — an Output Minimum > 0 must only apply
        // once the pen is actually down, or downstream output/bindings would read hover as a press.
        if (value is ITabletReport report && report.Pressure > 0)
        {
            var max = Tablet?.Properties?.Specifications?.Pen?.MaxPressure ?? 0;
            if (max > 0)
            {
                var settings = new PressureCurveSettings(
                    Softness, InputMinimum, InputMaximum, Minimum, Maximum,
                    CutBelowMinimum ? PressureMinApproach.Cut : PressureMinApproach.Clamp);
                var y = Domain.PressureCurve.Apply(report.Pressure / (double)max, settings);
                report.Pressure = (uint)Math.Round(y * max);
            }
        }
        Emit?.Invoke(value);
    }
}
