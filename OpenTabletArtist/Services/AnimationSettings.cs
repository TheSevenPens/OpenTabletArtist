using System;
using System.Globalization;

namespace OpenTabletArtist.Services;

/// <summary>
/// Decorative-animation preferences (#207). Currently just the falling-petal effect of the
/// Sakura/Anime skin. Persisted via <see cref="AppSettings"/> and surfaced through a static
/// <see cref="Changed"/> event so the overlay control can react live to the Settings toggle
/// (mirrors how <see cref="ThemeService"/> centralizes theme application).
/// </summary>
public static class AnimationSettings
{
    private const string PetalsKey = "AnimePetals";
    private const string PetalsOpacityKey = "AnimePetalsOpacity";

    /// <summary>Raised when a preference changes, so live consumers (the petal overlay) can update.</summary>
    public static event Action? Changed;

    /// <summary>Whether the Sakura skin's falling petals are enabled. Defaults to on.</summary>
    public static bool PetalsEnabled
    {
        get => AppSettings.Get(PetalsKey) is not "false"; // default true (only an explicit "false" disables)
        set
        {
            AppSettings.Set(PetalsKey, value ? "true" : "false");
            Changed?.Invoke();
        }
    }

    /// <summary>Overall opacity of the falling petals (0..1). Defaults to fully opaque (1).</summary>
    public static double PetalsOpacity
    {
        get => double.TryParse(AppSettings.Get(PetalsOpacityKey), NumberStyles.Float,
                   CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v, 0, 1)
            : 1.0;
        set
        {
            AppSettings.Set(PetalsOpacityKey, Math.Clamp(value, 0, 1).ToString(CultureInfo.InvariantCulture));
            Changed?.Invoke();
        }
    }
}
