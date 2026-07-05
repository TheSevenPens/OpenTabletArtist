using Avalonia;
using Avalonia.Media;

namespace OpenTabletArtist.Helpers;

/// <summary>Resolves UI fonts for Skia-rendered controls from theme resources (#392).</summary>
public static class AppFonts
{
    private const string DefaultFamily = "Segoe UI";

    public static Typeface UiTypeface(FontStyle style = FontStyle.Normal, FontWeight weight = FontWeight.Normal)
    {
        var family = ResolveFamily("ContentControlThemeFontFamily") ?? DefaultFamily;
        return new Typeface(family, style, weight);
    }

    private static string? ResolveFamily(string resourceKey)
    {
        if (Application.Current?.Resources.TryGetResource(resourceKey, null, out var value) != true)
            return null;
        return value switch
        {
            FontFamily ff => ff.Name,
            string s => s,
            _ => null,
        };
    }
}
