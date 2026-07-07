using System;
using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Domain;

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
    /// The stored Display-area centre for <paramref name="display"/>, given the full
    /// <paramref name="displays"/> set. OTD's Display area is in <em>0-based virtual-desktop</em>
    /// coordinates: the desktop's min (top-left-most) corner is the origin, so each monitor's position
    /// is its raw virtual-desktop position shifted by <c>-min</c>. Writing the raw monitor centre
    /// (<c>monitor.X + width/2</c>) only matched when the desktop origin was already (0,0) — i.e. the
    /// top-left-most monitor sat at 0. With a monitor at a negative offset (e.g. the primary not being
    /// top-left, which duplicating displays can produce) the raw centre landed the mapping off to the
    /// left / onto the wrong display. Shifting by <c>-min</c> places it correctly, matching OTD's own
    /// area diagram (which draws the desktop from its min corner). (#displaydup)
    /// </summary>
    public static (float X, float Y) MappedCenter(DisplayInfo display, IReadOnlyList<DisplayInfo> displays)
    {
        float minX = displays is { Count: > 0 } ? displays.Min(d => d.X) : display.X;
        float minY = displays is { Count: > 0 } ? displays.Min(d => d.Y) : display.Y;
        return (
            display.X - minX + display.Width / 2f,
            display.Y - minY + display.Height / 2f);
    }

    /// <summary>
    /// Maps <paramref name="profile"/>'s active area to <paramref name="display"/>: the display area
    /// becomes the whole monitor and the tablet area becomes the largest centered sub-area matching
    /// the monitor's aspect ratio (so the 1:1 mapping is undistorted). Uses <paramref name="digitizer"/>
    /// as the full tablet size, falling back to the profile's current tablet width/height.
    /// <paramref name="displays"/> is the full monitor set, needed to place the display area in OTD's
    /// virtual-screen coordinates (see <see cref="MappedCenter"/>).
    /// </summary>
    /// <returns>true if a full mapping was written; false if the profile has no Absolute settings or
    /// the dimensions are degenerate (in which case only the display area may have been set).</returns>
    public static bool ApplyToProfile(Profile profile, (float Width, float Height)? digitizer,
        DisplayInfo display, IReadOnlyList<DisplayInfo> displays)
    {
        var abs = profile.AbsoluteModeSettings;
        if (abs == null) return false;

        // Display area = the full selected monitor, centre-positioned the way OTD's own UX stores it.
        var (cx, cy) = MappedCenter(display, displays);
        abs.Display.Width = display.Width;
        abs.Display.Height = display.Height;
        abs.Display.X = cx;
        abs.Display.Y = cy;

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
        {
            // Match against the same centre ApplyToProfile would store for this monitor.
            var (cx, cy) = MappedCenter(d, displays);
            if (Approx(disp.Width, d.Width) && Approx(disp.Height, d.Height)
                && Approx(disp.X, cx) && Approx(disp.Y, cy))
                return d;
        }
        return null;
    }

    private static bool Approx(float a, float b) => System.Math.Abs(a - b) <= MatchTolerance;

    /// <summary>
    /// Copy the Absolute-mode area mapping (the Display <em>and</em> Tablet areas — the two are
    /// aspect-locked) from <paramref name="source"/> into <paramref name="target"/> for each matching
    /// tablet profile, leaving every other setting in <paramref name="target"/> untouched. Used so a
    /// per-app profile switch keeps the tablet on the monitor the user currently has it on, instead of
    /// the one frozen into the snapshot when it was saved (#167). Which monitor is governed by the user's
    /// live settings (the tablet page / the cycle-monitor hotkey), not by each snapshot.
    /// </summary>
    public static void PreserveAreaMapping(Settings target, Settings source)
    {
        foreach (var tp in target.Profiles)
        {
            var sp = source.Profiles.FirstOrDefault(p =>
                string.Equals(p.Tablet, tp.Tablet, StringComparison.OrdinalIgnoreCase));
            if (sp?.AbsoluteModeSettings is not { } sAbs || tp.AbsoluteModeSettings is not { } tAbs) continue;
            CopyArea(sAbs.Display, tAbs.Display);
            CopyArea(sAbs.Tablet, tAbs.Tablet);
        }

        static void CopyArea(AreaSettings? from, AreaSettings? to)
        {
            if (from == null || to == null) return;
            to.X = from.X;
            to.Y = from.Y;
            to.Width = from.Width;
            to.Height = from.Height;
            to.Rotation = from.Rotation;
        }
    }
}
