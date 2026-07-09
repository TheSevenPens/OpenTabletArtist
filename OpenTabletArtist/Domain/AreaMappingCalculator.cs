namespace OpenTabletArtist.Domain;

/// <summary>
/// Pure geometry for mapping a tablet's active area to a display while preserving the
/// display's aspect ratio (proportional 1:1 mapping, no distortion).
///
/// Extracted from <c>TabletDetailViewModel.SetToDisplay</c> so the math can be
/// unit-tested without an Avalonia window or the daemon. The view model is responsible
/// for reading the digitizer/display dimensions and writing the result back to OTD's
/// <c>AbsoluteModeSettings</c>; this class only does the arithmetic.
/// </summary>
public static class AreaMappingCalculator
{
    /// <summary>A tablet active area: size plus the center point it is positioned at.</summary>
    public readonly record struct TabletArea(float Width, float Height, float X, float Y);

    /// <summary>
    /// Computes the largest sub-area of the full tablet digitizer whose aspect ratio matches
    /// the display, centered on the digitizer. With <c>LockAspectRatio</c> this yields an
    /// undistorted 1:1 mapping onto the whole display.
    /// </summary>
    /// <param name="fullWidth">Full tablet digitizer width (e.g. mm). Must be positive.</param>
    /// <param name="fullHeight">Full tablet digitizer height. Must be positive.</param>
    /// <param name="displayWidth">Target display width (e.g. px). Must be positive.</param>
    /// <param name="displayHeight">Target display height. Must be positive.</param>
    /// <returns>The fitted tablet area, centered at (<paramref name="fullWidth"/>/2, <paramref name="fullHeight"/>/2).</returns>
    /// <exception cref="ArgumentOutOfRangeException">If any dimension is not positive (guards against the divide-by-zero the inline code did not).</exception>
    public static TabletArea FitToDisplayAspect(float fullWidth, float fullHeight, float displayWidth, float displayHeight)
    {
        if (fullWidth <= 0) throw new ArgumentOutOfRangeException(nameof(fullWidth), fullWidth, "Tablet dimensions must be positive.");
        if (fullHeight <= 0) throw new ArgumentOutOfRangeException(nameof(fullHeight), fullHeight, "Tablet dimensions must be positive.");
        if (displayWidth <= 0) throw new ArgumentOutOfRangeException(nameof(displayWidth), displayWidth, "Display dimensions must be positive.");
        if (displayHeight <= 0) throw new ArgumentOutOfRangeException(nameof(displayHeight), displayHeight, "Display dimensions must be positive.");

        double displayAspect = (double)displayWidth / displayHeight;
        double tabletAspect = (double)fullWidth / fullHeight;

        float width, height;
        if (displayAspect > tabletAspect)
        {
            // Display is wider than the tablet — use the full tablet width and reduce the height.
            width = fullWidth;
            height = (float)(fullWidth / displayAspect);
        }
        else
        {
            // Display is taller (or equal aspect) — use the full tablet height and reduce the width.
            height = fullHeight;
            width = (float)(fullHeight * displayAspect);
        }

        return new TabletArea(width, height, fullWidth / 2f, fullHeight / 2f);
    }

    /// <summary>
    /// Fit accounting for a 0/90/180/270 <paramref name="rotationDeg"/> (#199). The area's stored
    /// aspect always matches the display (so OTD's scale stays uniform → no distortion); for a
    /// perpendicular rotation (90/270) the largest such rectangle is the one whose <em>rotated</em>
    /// bounding box fits the tablet, i.e. the tablet's width/height are swapped for the fit. The result
    /// is always centred on the real tablet.
    /// </summary>
    public static TabletArea FitForRotation(float fullWidth, float fullHeight, float displayWidth, float displayHeight, int rotationDeg)
    {
        bool perpendicular = (((rotationDeg % 360) + 360) % 360) % 180 != 0;
        var fit = perpendicular
            ? FitToDisplayAspect(fullHeight, fullWidth, displayWidth, displayHeight)  // rotated bbox must fit → swap tablet dims
            : FitToDisplayAspect(fullWidth, fullHeight, displayWidth, displayHeight);
        // The swapped call centres on the swapped dims; re-centre on the real tablet.
        return fit with { X = fullWidth / 2f, Y = fullHeight / 2f };
    }

    /// <summary>
    /// Clamp an aspect-locked active area (for interactive resize/move, #199) so its rotation-aware
    /// footprint stays fully within the tablet and its size stays between <paramref name="minWidth"/> and
    /// the largest that fits. The Width:Height aspect is preserved (only the scale changes); the centre is
    /// then clamped so the (possibly rotated) footprint can't cross a tablet edge.
    /// </summary>
    public static TabletArea ClampArea(float width, float height, float centerX, float centerY,
        int rotationDeg, float fullWidth, float fullHeight, float minWidth)
    {
        width = Math.Max(1e-3f, width);
        height = Math.Max(1e-3f, height);
        bool perp = ((((rotationDeg % 360) + 360) % 360) % 180) != 0;

        // Footprint = the area's bounding box in tablet coords (width/height swap for 90/270).
        float footW = perp ? height : width;
        float footH = perp ? width : height;

        // Shrink to fit the tablet if the footprint overflows (preserve aspect).
        float fitScale = Math.Min(1f, Math.Min(fullWidth / footW, fullHeight / footH));
        width *= fitScale; height *= fitScale;

        // Enforce a minimum width (preserve aspect) — but only if the larger area still fits.
        if (width < minWidth && width > 0)
        {
            float up = minWidth / width;
            float fW = perp ? height * up : width * up;
            float fH = perp ? width * up : height * up;
            if (fW <= fullWidth && fH <= fullHeight) { width *= up; height *= up; }
        }

        footW = perp ? height : width;
        footH = perp ? width : height;
        centerX = Math.Clamp(centerX, footW / 2f, fullWidth - footW / 2f);
        centerY = Math.Clamp(centerY, footH / 2f, fullHeight - footH / 2f);
        return new TabletArea(width, height, centerX, centerY);
    }
}
