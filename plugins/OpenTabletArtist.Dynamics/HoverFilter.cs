using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Dynamics;

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
[PluginName("OpenTabletArtist - Hover Limit")]
public class HoverFilter : IPositionedPipelineElement<IDeviceReport>
{
    [Property("Max Hover Distance"), DefaultPropertyValue(255),
     ToolTip("Drop pen reports when the hover distance (0-255) exceeds this, so the cursor stops " +
             "tracking once the pen lifts past it. 255 = no limit.")]
    public int MaxHoverDistance { get; set; } = 255;

    [BooleanProperty("Near proximity only",
         "Only track while the pen is in the tablet's near-proximity band (close to the surface), " +
         "matching Wacom's default hover range. Tablets that don't report proximity ignore this."),
     DefaultPropertyValue(false)]
    public bool NearProximityOnly { get; set; }

    // #318: the hover-distance limiter is temporarily disabled while we validate its behavior — early
    // user feedback suggested it wasn't clearly helping and could cause more confusion than it solves.
    // While this is false the filter passes every report through unchanged, so it has no effect even
    // on a profile that still has it enabled; the Hover tab in the UI is hidden to match. To bring the
    // feature back, set this to true and unhide the Hover tab (TabletDetailView.axaml). A field (not a
    // const) so the gating logic below stays compiled — re-enabling is a one-line change.
    private static readonly bool LimiterEnabled = false;

    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport>? Emit;

    public void Consume(IDeviceReport value)
    {
        if (LimiterEnabled && value is IProximityReport proximity)
        {
            // Hovering past the limit → drop the report so the cursor holds its last position.
            if (proximity.HoverDistance > MaxHoverDistance)
                return;
            // Near-proximity gate: only pass while the pen is in the tablet's close band (Wacom default).
            if (NearProximityOnly && !proximity.NearProximity)
                return;
        }

        Emit?.Invoke(value);
    }
}
