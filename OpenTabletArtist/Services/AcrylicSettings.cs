using System.Globalization;

namespace OpenTabletArtist.Services;

/// <summary>
/// Experimental frosted-glass (acrylic) tuning for the cards. Tint + material opacity (+ sidebar
/// opacity) are persisted via <see cref="AppSettings"/> and exposed on the Theme page.
/// </summary>
public static class AcrylicSettings
{
    private const string TintKey = "Acrylic:TintOpacity";

    /// <summary>Defaults, also used by the Theme page's "Reset to defaults".</summary>
    public const double DefaultTintOpacity = 0.35;
    public const double DefaultMaterialOpacity = 0.75;
    public const double DefaultSidebarOpacity = 0.85;

    /// <summary>0..1 — how strongly the tint color colors the frosted surface.</summary>
    public static double TintOpacity
    {
        get => GetDouble(TintKey, DefaultTintOpacity);
        set => AppSettings.Set(TintKey, value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    // Card + sidebar opacity are stored PER SKIN (keyed by the skin name — "Sakura", "DarkSakura",
    // "Custom") so each translucent skin keeps its own translucency; changing one never touches another
    // (#241). Each getter takes the skin's own default so an untouched skin looks as designed.

    /// <summary>0..1 — how opaque the given skin's cards are (lower = more backdrop shows through).</summary>
    public static double MaterialOpacity(string skin, double fallback) =>
        GetDouble($"{skin}:MaterialOpacity", fallback);
    public static void SetMaterialOpacity(string skin, double value) =>
        AppSettings.Set($"{skin}:MaterialOpacity", value.ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>0..1 — opacity of the given skin's sidebar (left pane) background.</summary>
    public static double SidebarOpacity(string skin, double fallback) =>
        GetDouble($"{skin}:SidebarOpacity", fallback);
    public static void SetSidebarOpacity(string skin, double value) =>
        AppSettings.Set($"{skin}:SidebarOpacity", value.ToString("0.###", CultureInfo.InvariantCulture));

    private static double GetDouble(string key, double fallback) =>
        double.TryParse(AppSettings.Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
