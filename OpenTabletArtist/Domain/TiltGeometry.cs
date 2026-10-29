namespace OpenTabletArtist.Domain;

/// <summary>
/// The shape of a leaning pen, for the tilt tab's view from above. Works from the pen's azimuth (the direction it
/// leans, 0° toward +X and increasing toward +Y, as <see cref="TiltMath.Azimuth"/> reports it) and altitude (the
/// angle above the tablet surface, 90° upright), so the picture and the numbers beside it cannot disagree. The
/// view from the side needs none of this: it draws the altitude alone.
/// </summary>
public static class TiltGeometry
{
    private const double Deg2Rad = Math.PI / 180.0;

    /// <summary>
    /// Where the top of the pen sits relative to its tip, seen from above, as a fraction of the pen's length:
    /// X to the right and Y down (the tablet's own axes). Upright is (0, 0); lying flat is a unit vector.
    /// </summary>
    public static (double X, double Y) Footprint(double azimuthDeg, double altitudeDeg)
    {
        double reach = Math.Cos(altitudeDeg * Deg2Rad);
        return (reach * Math.Cos(azimuthDeg * Deg2Rad), reach * Math.Sin(azimuthDeg * Deg2Rad));
    }
}
