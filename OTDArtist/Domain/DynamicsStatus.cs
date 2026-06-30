namespace OtdArtist.Domain;

/// <summary>
/// Pure description of what the bundled Pen Dynamics filter is currently doing to the pen, as a
/// single human-readable line. Shared by the Test page's "Affecting your pen" indicator (#184) and
/// the system-tray reveal (#186), so both phrase it the same way and the logic is unit-tested once.
/// </summary>
public static class DynamicsStatus
{
    /// <summary>
    /// One line summarizing the dynamics state for the given profile's settings:
    /// <list type="bullet">
    /// <item>not enabled → "Pen dynamics: off"</item>
    /// <item>enabled but a linear curve with no smoothing → "Pen dynamics: on (behaves linear)"</item>
    /// <item>enabled and altering the stroke → "Affecting your pen: Pressure curve, Pressure smoothing, …"</item>
    /// </list>
    /// </summary>
    public static string Describe(bool enabled, PenDynamicsSettings dynamics)
    {
        if (!enabled)
            return "Pen dynamics: off";

        if (dynamics.IsNoOp)
            return "Pen dynamics: on (behaves linear)";

        var parts = new List<string>(3);
        if (dynamics.CurveShapesPressure) parts.Add("Pressure curve");
        if (dynamics.HasPressureSmoothing) parts.Add("Pressure smoothing");
        if (dynamics.HasPositionSmoothing) parts.Add("Position smoothing");
        return "Affecting your pen: " + string.Join(", ", parts);
    }
}
