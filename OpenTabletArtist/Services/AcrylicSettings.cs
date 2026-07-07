using System.Globalization;

namespace OpenTabletArtist.Services;

/// <summary>
/// Experimental frosted-glass (acrylic) tuning for the cards. Tint + material opacity (+ sidebar
/// opacity) are persisted via <see cref="AppSettings"/> and exposed on the Theme page.
/// </summary>
public static class AcrylicSettings
{
    private const string TintKey = "Acrylic:TintOpacity";
    private const string MaterialKey = "Acrylic:MaterialOpacity";
    private const string SidebarKey = "Acrylic:SidebarOpacity";

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

    /// <summary>0..1 — how opaque the cards are (lower = more sakura shows through). Defaults to 0.75
    /// so a little of the backdrop shows through out of the box (#298).</summary>
    public static double MaterialOpacity
    {
        get => GetDouble(MaterialKey, DefaultMaterialOpacity);
        set => AppSettings.Set(MaterialKey, value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    /// <summary>0..1 — opacity of the sidebar (left pane) background in the translucent skins
    /// (Sakura + Custom). Lower lets more of the backdrop show through behind the nav.</summary>
    public static double SidebarOpacity
    {
        get => GetDouble(SidebarKey, DefaultSidebarOpacity);
        set => AppSettings.Set(SidebarKey, value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static double GetDouble(string key, double fallback) =>
        double.TryParse(AppSettings.Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
