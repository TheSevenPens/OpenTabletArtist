namespace OpenTabletArtist.Services;

/// <summary>
/// Per-skin colour choices for the translucent skins (Sakura + Custom): the frosted-card tint each skin
/// uses, and the Custom skin's base (background) colour. Persisted via <see cref="AppSettings"/>. The
/// defaults reproduce the skins' original hard-coded looks, so an untouched install is unchanged, and
/// the Theme page's "Reset to defaults" restores them.
/// </summary>
public static class SkinColorSettings
{
    private const string SakuraCardKey = "Sakura:CardColor";
    private const string DarkSakuraCardKey = "DarkSakura:CardColor";
    private const string CustomCardKey = "Custom:CardColor";
    private const string CustomBaseKey = "Custom:BaseColor";

    public const string SakuraCardDefault = "#FDF1F7";     // soft sakura white
    public const string DarkSakuraCardDefault = "#261C30"; // dark plum glass (matches the theme's GlassBg)
    public const string CustomCardDefault = "#202430";     // neutral dark panel
    public const string CustomBaseDefault = "#181820";     // near-black background behind the panels

    /// <summary>Frosted-card tint for the Sakura skin, as "#AARRGGBB"/"#RRGGBB".</summary>
    public static string SakuraCardHex
    {
        get => AppSettings.Get(SakuraCardKey) ?? SakuraCardDefault;
        set => AppSettings.Set(SakuraCardKey, value);
    }

    /// <summary>Frosted-card tint for the Dark Sakura skin — kept separate from Sakura's (#241).</summary>
    public static string DarkSakuraCardHex
    {
        get => AppSettings.Get(DarkSakuraCardKey) ?? DarkSakuraCardDefault;
        set => AppSettings.Set(DarkSakuraCardKey, value);
    }

    // Highlight/accent tint per blossom skin (#557). Default reproduces each skin's original pink accent;
    // (Custom keeps its accent in CustomThemeSettings.AccentHex.)
    private const string SakuraAccentKey = "Sakura:AccentColor";
    private const string DarkSakuraAccentKey = "DarkSakura:AccentColor";
    public const string SakuraAccentDefault = "#E0218A";
    public const string DarkSakuraAccentDefault = "#E0218A";

    public static string SakuraAccentHex
    {
        get => AppSettings.Get(SakuraAccentKey) ?? SakuraAccentDefault;
        set => AppSettings.Set(SakuraAccentKey, value);
    }

    public static string DarkSakuraAccentHex
    {
        get => AppSettings.Get(DarkSakuraAccentKey) ?? DarkSakuraAccentDefault;
        set => AppSettings.Set(DarkSakuraAccentKey, value);
    }

    /// <summary>Frosted-card tint for the Custom skin.</summary>
    public static string CustomCardHex
    {
        get => AppSettings.Get(CustomCardKey) ?? CustomCardDefault;
        set => AppSettings.Set(CustomCardKey, value);
    }

    /// <summary>The Custom skin's base/background colour (fills the window behind the panels and drives
    /// the left-pane gradient) when no background image is set.</summary>
    public static string CustomBaseHex
    {
        get => AppSettings.Get(CustomBaseKey) ?? CustomBaseDefault;
        set => AppSettings.Set(CustomBaseKey, value);
    }

    // Sakura background: a code-generated gradient (default) or a flat colour.
    private const string SakuraBackgroundKey = "Sakura:Background";
    /// <summary>The flat colour the Sakura skin uses for the "solid" background mode.</summary>
    public const string SakuraSolidBgColor = "#FDE4E8";

    /// <summary>Sakura background mode: "codegen" (default) or "solid". The retired "image" mode migrates
    /// to "codegen" so existing users with it saved get the gradient backdrop.</summary>
    public static string SakuraBackground
    {
        get
        {
            var v = AppSettings.Get(SakuraBackgroundKey) ?? "codegen";
            return v == "image" ? "codegen" : v;
        }
        set => AppSettings.Set(SakuraBackgroundKey, value);
    }
}
