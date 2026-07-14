using System.Collections.Generic;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A curated fixed palette for the theme colour pickers (#555) — Material Design hues at a few shades
/// each, plus a neutral ramp, spanning light → dark so every theme need (a saturated accent, a pale card
/// tint, a dark base) has a suitable choice. Users pick from these instead of a freeform colour wheel.
/// </summary>
public static class ColorPalette
{
    private static Color C(string hex) => Color.Parse(hex);

    /// <summary>The swatches, row-major by hue (light → dark within each hue). A WrapPanel lays them out.</summary>
    public static readonly IReadOnlyList<Color> All = new[]
    {
        // Neutrals (white → black)
        C("#FFFFFF"), C("#F5F5F5"), C("#E0E0E0"), C("#9E9E9E"), C("#616161"), C("#303030"), C("#000000"),
        // Red
        C("#FFCDD2"), C("#E57373"), C("#F44336"), C("#D32F2F"), C("#8E1B1B"),
        // Pink
        C("#F8BBD0"), C("#F06292"), C("#E91E63"), C("#C2185B"), C("#7A0F3D"),
        // Purple
        C("#E1BEE7"), C("#BA68C8"), C("#9C27B0"), C("#7B1FA2"), C("#4A148C"),
        // Indigo / Blue
        C("#C5CAE9"), C("#7986CB"), C("#3F51B5"), C("#303F9F"), C("#1A237E"),
        C("#BBDEFB"), C("#64B5F6"), C("#2196F3"), C("#1976D2"), C("#0D47A1"),
        // Teal / Green
        C("#B2DFDB"), C("#4DB6AC"), C("#009688"), C("#00796B"), C("#004D40"),
        C("#C8E6C9"), C("#81C784"), C("#4CAF50"), C("#388E3C"), C("#1B5E20"),
        // Amber / Orange
        C("#FFECB3"), C("#FFD54F"), C("#FFC107"), C("#FFA000"), C("#FF6F00"),
        C("#FFCCBC"), C("#FF8A65"), C("#FF5722"), C("#E64A19"), C("#BF360C"),
        // Brown / Blue-grey
        C("#D7CCC8"), C("#A1887F"), C("#795548"), C("#5D4037"), C("#3E2723"),
        C("#CFD8DC"), C("#90A4AE"), C("#607D8B"), C("#455A64"), C("#263238"),
    };
}
