using System;

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
}
