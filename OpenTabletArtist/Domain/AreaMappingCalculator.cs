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
}
