using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;

namespace OtdInterop;

/// <summary>
/// Keeps settings valid for every consumer of the shared <c>settings.json</c> — notably the
/// OpenTabletDriver UX, whose Save does <c>p.AbsoluteModeSettings.Tablet.Width</c> and NREs (crashing) if a
/// profile has a null <c>AbsoluteModeSettings</c>, <c>Tablet</c>, or <c>Display</c>. Some app-side profile
/// creation (e.g. building a profile outside the daemon process, where the virtual-screen service is absent)
/// can leave those null; this fills them so nothing downstream trips over a null. It only replaces nulls —
/// existing areas are never altered.
///
/// This is a compatibility guard, not validation: it repairs one specific shape that is known to crash
/// a reader of the file. It makes no claim that settings passing through it are valid in any broader
/// sense. It belongs to this library rather than to any one application, because every writer of that
/// shared file needs it and none of them gets to decide otherwise.
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
