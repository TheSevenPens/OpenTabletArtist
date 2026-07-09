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

        // Honour any active-area rotation so re-applying a mapping keeps the rotated fit (#199).
        int rot = (((int)System.Math.Round(abs.Tablet.Rotation) % 360) + 360) % 360;
        var area = AreaMappingCalculator.FitForRotation(fullWidth, fullHeight, display.Width, display.Height, rot);
        abs.Tablet.Width = area.Width;
        abs.Tablet.Height = area.Height;
        abs.Tablet.X = area.X;
        abs.Tablet.Y = area.Y;
        abs.LockAspectRatio = true;
        return true;
    }

    /// <summary>Set the active-area <paramref name="rotationDeg"/> (0/90/180/270) and re-fit the tablet
    /// area so it still fills the currently-mapped display without distortion — for 90/270 the area
    /// shrinks to the largest that fits the tablet once rotated (#199). Returns true if a rotation was
    /// written (the area is only re-fitted when the profile maps cleanly to a single monitor).</summary>
    public static bool ApplyRotation(Profile profile, (float Width, float Height)? digitizer,
        int rotationDeg, IReadOnlyList<DisplayInfo> displays)
    {
        var abs = profile.AbsoluteModeSettings;
        if (abs?.Tablet == null) return false;

        abs.Tablet.Rotation = rotationDeg;

        float fullWidth = digitizer?.Width ?? abs.Tablet.Width;
        float fullHeight = digitizer?.Height ?? abs.Tablet.Height;
        var display = CurrentlyMapped(profile, displays);
        if (display == null || fullWidth <= 0 || fullHeight <= 0 || display.Width <= 0 || display.Height <= 0)
            return true; // rotation applied; no clean single-display mapping to re-fit against

        var area = AreaMappingCalculator.FitForRotation(fullWidth, fullHeight, display.Width, display.Height, rotationDeg);
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
    /// Classify a profile's stored Absolute-mode display mapping for predictability, so the UI can flag
    /// an unusual or broken mapping instead of silently rendering it. The stored Display area is the
    /// output region in 0-based virtual-desktop coordinates (X/Y is its <em>centre</em>; see
    /// <see cref="MappedCenter"/>). Coverage is measured against the union of connected monitors:
    /// <list type="bullet">
    /// <item><see cref="DisplayMappingValidity.Clean"/> — the area matches one whole monitor.</item>
    /// <item><see cref="DisplayMappingValidity.Custom"/> — fully on-screen but not a single monitor
    /// (a sub-region, or spanning several).</item>
    /// <item><see cref="DisplayMappingValidity.OffScreen"/> — part of the area falls outside every
    /// monitor, so the pen maps to space with no screen there.</item>
    /// <item><see cref="DisplayMappingValidity.None"/> — no (or degenerate) mapping / no displays.</item>
    /// </list>
    /// </summary>
    public static DisplayMappingValidity ClassifyMapping(Profile profile, IReadOnlyList<DisplayInfo> displays)
    {
        var disp = profile.AbsoluteModeSettings?.Display;
        if (disp == null || disp.Width <= 0 || disp.Height <= 0 || displays is not { Count: > 0 })
            return DisplayMappingValidity.None;

        if (CurrentlyMapped(profile, displays) != null) return DisplayMappingValidity.Clean;

        // Output rectangle in 0-based desktop coords (disp.X/Y is the centre).
        float minX = displays.Min(d => d.X), minY = displays.Min(d => d.Y);
        float left = disp.X - disp.Width / 2f, top = disp.Y - disp.Height / 2f;
        float right = left + disp.Width, bottom = top + disp.Height;
        float outputArea = disp.Width * disp.Height;

        // How much of the output rectangle is covered by some monitor (monitors don't overlap in a
        // normal extended desktop, so summing per-monitor intersection is exact enough).
        float covered = 0f;
        foreach (var d in displays)
        {
            float dl = d.X - minX, dt = d.Y - minY;
            float ix = System.Math.Max(0f, System.Math.Min(right, dl + d.Width) - System.Math.Max(left, dl));
            float iy = System.Math.Max(0f, System.Math.Min(bottom, dt + d.Height) - System.Math.Max(top, dt));
            covered += ix * iy;
        }

        // Tolerate ~1% rounding before calling part of it off-screen.
        bool fullyOnScreen = covered >= outputArea - System.Math.Max(1f, outputArea * 0.01f);
        return fullyOnScreen ? DisplayMappingValidity.Custom : DisplayMappingValidity.OffScreen;
    }

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

/// <summary>How a tablet's stored Absolute-mode display mapping relates to the connected monitors
/// (see <see cref="DisplayMappingApplier.ClassifyMapping"/>).</summary>
public enum DisplayMappingValidity
{
    /// <summary>No mapping to assess (no Absolute settings, a degenerate area, or no displays).</summary>
    None,
    /// <summary>Maps to exactly one whole monitor — the standard case.</summary>
    Clean,
    /// <summary>Fully on-screen but not a single monitor (a sub-region or spanning several).</summary>
    Custom,
    /// <summary>Part of the mapped area falls outside every monitor — the pen hits off-screen dead zones.</summary>
    OffScreen,
}
