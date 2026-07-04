namespace OpenTabletArtist.Services;

/// <summary>
/// User-controlled settings for the "Custom" theme — parallel to Sakura, but with a user-chosen accent
/// colour that derives the whole scheme and an optional background image. Persisted via
/// <see cref="AppSettings"/> so a look survives restarts. Card translucency is shared with Sakura via
/// <see cref="AcrylicSettings.MaterialOpacity"/>; ThemeViewModel turns these into live resource overrides.
/// </summary>
public static class CustomThemeSettings
{
    private const string AccentKey = "Custom:Accent";
    private const string ImageKey = "Custom:BgImage";

    /// <summary>Default accent when the user hasn't picked one — the app's shared indigo.</summary>
    public const string DefaultAccentHex = "#6366F1";

    /// <summary>The accent colour that drives the Custom scheme, as "#AARRGGBB"/"#RRGGBB".</summary>
    public static string AccentHex
    {
        get => AppSettings.Get(AccentKey) ?? DefaultAccentHex;
        set => AppSettings.Set(AccentKey, value);
    }

    /// <summary>Absolute path to the chosen background image, or null when none is set.</summary>
    public static string? BackgroundImagePath
    {
        get
        {
            var p = AppSettings.Get(ImageKey);
            return string.IsNullOrWhiteSpace(p) ? null : p;
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value)) AppSettings.Remove(ImageKey);
            else AppSettings.Set(ImageKey, value);
        }
    }
}
