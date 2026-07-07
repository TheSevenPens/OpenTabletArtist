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
    private const string CustomCardKey = "Custom:CardColor";
    private const string CustomBaseKey = "Custom:BaseColor";

    public const string SakuraCardDefault = "#FDF1F7"; // soft sakura white
    public const string CustomCardDefault = "#202430"; // neutral dark panel
    public const string CustomBaseDefault = "#181820"; // near-black background behind the panels

    /// <summary>Frosted-card tint for the Sakura skin, as "#AARRGGBB"/"#RRGGBB".</summary>
    public static string SakuraCardHex
    {
        get => AppSettings.Get(SakuraCardKey) ?? SakuraCardDefault;
        set => AppSettings.Set(SakuraCardKey, value);
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
}
