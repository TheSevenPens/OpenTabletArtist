using OpenTabletDriver.Desktop.Profiles;

namespace OtdArtist.Domain;

/// <summary>
/// Writes a tablet profile's Absolute-mode area mapping to a chosen display, aspect-locked, and finds
/// which display a profile is currently mapped to. Extracted from the tablet settings dialog so the
/// same mapping is applied whether the user picks a display in the dialog or from the system tray's
/// "Switch display" menu (#187). The geometry itself lives in <see cref="AreaMappingCalculator"/>;
/// this just reads/writes OTD's <c>AbsoluteModeSettings</c>.
/// </summary>
public static class DisplayMappingApplier
{
    // Tolerance (px) for matching a stored display area back to an enumerated monitor.
    private const float MatchTolerance = 1.5f;

    /// <summary>
    /// Maps <paramref name="profile"/>'s active area to <paramref name="display"/>: the display area
    /// becomes the whole monitor and the tablet area becomes the largest centered sub-area matching
    /// the monitor's aspect ratio (so the 1:1 mapping is undistorted). Uses <paramref name="digitizer"/>
    /// as the full tablet size, falling back to the profile's current tablet width/height.
    /// </summary>
    /// <returns>true if a full mapping was written; false if the profile has no Absolute settings or
    /// the dimensions are degenerate (in which case only the display area may have been set).</returns>
    public static bool ApplyToProfile(Profile profile, (float Width, float Height)? digitizer, DisplayInfo display)
    {
        var abs = profile.AbsoluteModeSettings;
        if (abs == null) return false;

        // Display area = the full selected monitor (centre-positioned, matching how OTD stores it).
        abs.Display.Width = display.Width;
        abs.Display.Height = display.Height;
        abs.Display.X = display.X + display.Width / 2f;
        abs.Display.Y = display.Y + display.Height / 2f;

        // Start from the full tablet digitizer area (fall back to the profile's stored area).
        float fullWidth = digitizer?.Width ?? abs.Tablet.Width;
        float fullHeight = digitizer?.Height ?? abs.Tablet.Height;
        if (fullWidth <= 0 || fullHeight <= 0 || display.Width <= 0 || display.Height <= 0)
            return false;

        var area = AreaMappingCalculator.FitToDisplayAspect(fullWidth, fullHeight, display.Width, display.Height);
        abs.Tablet.Width = area.Width;
        abs.Tablet.Height = area.Height;
        abs.Tablet.X = area.X;
        abs.Tablet.Y = area.Y;
        abs.LockAspectRatio = true;
        return true;
    }

    /// <summary>The display this profile is currently mapped to (a full-monitor match against the
    /// stored display area), or null if it isn't mapped to a whole monitor / has no Absolute settings.</summary>
    public static DisplayInfo? CurrentlyMapped(Profile profile, IReadOnlyList<DisplayInfo> displays)
    {
        var disp = profile.AbsoluteModeSettings?.Display;
        if (disp == null) return null;
        foreach (var d in displays)
            // ApplyToProfile stores the display area as centre = monitor centre, size = monitor size.
            if (Approx(disp.Width, d.Width) && Approx(disp.Height, d.Height)
                && Approx(disp.X, d.X + d.Width / 2f) && Approx(disp.Y, d.Y + d.Height / 2f))
                return d;
        return null;
    }

    private static bool Approx(float a, float b) => System.Math.Abs(a - b) <= MatchTolerance;
}
