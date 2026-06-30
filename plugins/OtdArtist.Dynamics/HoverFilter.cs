using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace OtdArtist.Dynamics;

/// <summary>
/// OpenTabletDriver filter that limits the pen's hover height (#188), modeled on Kuuube's
/// Hover Distance Limiter. When the pen hovers farther than <see cref="MaxHoverDistance"/> from the
/// surface, the report is dropped so the cursor stops tracking — useful to keep a lifted pen from
/// dragging the cursor around. Drawing is unaffected: in contact the hover distance is ~0, which is
/// always within the limit.
///
/// <para>Hover distance is a 0-255 value in the tablet report. Not all tablets report it — if the
/// Diagnostics page shows no HoverDistance for your tablet, this filter has no effect.</para>
/// </summary>
[PluginName("OTD Artist - Hover Limit")]
public class HoverFilter : IPositionedPipelineElement<IDeviceReport>
{
    [Property("Max Hover Distance"), DefaultPropertyValue(255),
     ToolTip("Drop pen reports when the hover distance (0-255) exceeds this, so the cursor stops " +
             "tracking once the pen lifts past it. 255 = no limit.")]
    public int MaxHoverDistance { get; set; } = 255;

    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport>? Emit;

    public void Consume(IDeviceReport value)
    {
        // Hovering past the limit → drop the report so the cursor holds its last position.
        if (value is IProximityReport proximity && proximity.HoverDistance > MaxHoverDistance)
            return;

        Emit?.Invoke(value);
    }
}
