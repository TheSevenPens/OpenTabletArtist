using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Keeps the settings OTA writes valid for every consumer of the shared <c>settings.json</c> — notably the
/// OpenTabletDriver UX, whose Save does <c>p.AbsoluteModeSettings.Tablet.Width</c> and NREs (crashing) if a
/// profile has a null <c>AbsoluteModeSettings</c>, <c>Tablet</c>, or <c>Display</c>. Some app-side profile
/// creation (e.g. building a profile outside the daemon process, where the virtual-screen service is absent)
/// can leave those null; this fills them so nothing downstream trips over a null. It only replaces nulls —
/// existing areas are never altered. (#otd-null-areas)
/// </summary>
public static class ProfileSanitizer
{
    /// <summary>Ensure every profile has a non-null <c>AbsoluteModeSettings</c> with non-null <c>Tablet</c>
    /// and <c>Display</c> areas. Returns how many profiles had to be repaired (0 = already valid).</summary>
    public static int EnsureValidAbsoluteAreas(Settings? settings)
    {
        if (settings?.Profiles == null) return 0;

        int repaired = 0;
        foreach (var profile in settings.Profiles)
        {
            if (profile == null) continue;
            bool changed = false;

            var abs = profile.AbsoluteModeSettings;
            if (abs == null)
            {
                abs = new AbsoluteModeSettings();
                profile.AbsoluteModeSettings = abs;
                changed = true;
            }
            if (abs.Tablet == null) { abs.Tablet = new AreaSettings(); changed = true; }
            if (abs.Display == null) { abs.Display = new AreaSettings(); changed = true; }

            if (changed) repaired++;
        }
        return repaired;
    }
}
