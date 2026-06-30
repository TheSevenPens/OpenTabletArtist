using System;
using Avalonia;
using Avalonia.Styling;

namespace OpenTabletArtist.Services;

/// <summary>
/// Appearance selection (#139): Light / Dark / System, plus the experimental "Anime" skin.
/// The choice is persisted via <see cref="AppSettings"/> and applied by setting
/// <see cref="Application.RequestedThemeVariant"/> — "System" maps to <see cref="ThemeVariant.Default"/>
/// (follows the OS). Brush resources switch automatically because consumers bind them with
/// DynamicResource (see Themes/Colors.axaml).
///
/// "Anime" is a custom <see cref="ThemeVariant"/> that <em>inherits from Light</em>: any key the
/// Anime theme dictionary doesn't define falls back to Light, so Fluent's native control resources
/// keep resolving and only our app brushes (backgrounds, glass, text, accent) + the backdrop image
/// are re-skinned. No runtime dictionary swapping needed.
/// </summary>
public static class ThemeService
{
    private const string Key = "Theme";

    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";
    public const string Anime = "Anime";

    /// <summary>The "Anime" skin variant. Inherits Light so unspecified keys (incl. all Fluent
    /// control resources) fall back to the Light theme rather than rendering unstyled.</summary>
    public static readonly ThemeVariant AnimeVariant = new(Anime, ThemeVariant.Light);

    /// <summary>The saved choice, or "System" if none has been set.</summary>
    public static string SavedChoice => AppSettings.Get(Key) ?? System;

    /// <summary>Applies a choice to the running app and persists it.</summary>
    public static void Apply(string choice)
    {
        AppSettings.Set(Key, choice);
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ToVariant(choice);
    }

    /// <summary>Applies the persisted choice (call once at startup, before showing the window).</summary>
    public static void ApplySaved()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ToVariant(SavedChoice);
    }

    private static ThemeVariant ToVariant(string choice) => choice switch
    {
        Light => ThemeVariant.Light,
        Dark => ThemeVariant.Dark,
        Anime => AnimeVariant,
        _ => ThemeVariant.Default, // System / unknown → follow the OS
    };
}
