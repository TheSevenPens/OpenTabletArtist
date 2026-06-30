namespace OpenTabletArtist.Domain;

/// <summary>
/// Geometry for the active-area diagram (#250/#252): the tablet's full physical area, the effective
/// (mapped) sub-area within it, and the display it's mapped to. All tablet figures are in the
/// digitizer's units (mm); <see cref="EffCenterX"/>/<see cref="EffCenterY"/> are the effective area's
/// centre offset from the full area's top-left.
/// </summary>
public sealed record TabletAreaInfo(
    double FullWidth, double FullHeight,
    double EffWidth, double EffHeight, double EffCenterX, double EffCenterY,
    bool HasDisplay, int DisplayNumber, string DisplayName, double DisplayWidth, double DisplayHeight);
