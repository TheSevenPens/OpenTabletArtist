using System.Globalization;

namespace OpenTabletArtist.Services;

/// <summary>
/// Experimental frosted-glass (acrylic) tuning for the cards. Just the two material knobs the Theme
/// page exposes for now — tint + material opacity — persisted via <see cref="AppSettings"/> so a
/// tuning session survives restarts. (Rolling the chosen material onto every card is a follow-up.)
/// </summary>
public static class AcrylicSettings
{
    private const string TintKey = "Acrylic:TintOpacity";
    private const string MaterialKey = "Acrylic:MaterialOpacity";

    /// <summary>0..1 — how strongly the tint color colors the frosted surface.</summary>
    public static double TintOpacity
    {
        get => GetDouble(TintKey, 0.35);
        set => AppSettings.Set(TintKey, value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    /// <summary>0..1 — how opaque the cards are (lower = more sakura shows through). Defaults to 0.75
    /// so a little of the backdrop shows through out of the box (#298).</summary>
    public static double MaterialOpacity
    {
        get => GetDouble(MaterialKey, 0.75);
        set => AppSettings.Set(MaterialKey, value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static double GetDouble(string key, double fallback) =>
        double.TryParse(AppSettings.Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
