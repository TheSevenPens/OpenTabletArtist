using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Newtonsoft.Json;

namespace OpenTabletArtist.Services;

/// <summary>One radial glow in the code-generated Sakura backdrop (#556): a soft blob anchored to the
/// bottom of the window. <see cref="CenterX"/>/<see cref="Width"/> are relative (0..1 across the window);
/// <see cref="HeightPx"/> is absolute (measured against the fixed glow band). Serialized to JSON so the
/// Developer gradient editor can round-trip and share settings.</summary>
public sealed class GradientGlow
{
    public double CenterX { get; set; } = 0.5;       // 0..1 across the window
    public double Width { get; set; } = 0.5;         // relative radius X (1 ≈ half the window wide)
    public double HeightPx { get; set; } = 150;      // absolute vertical reach, px
    public string Color { get; set; } = "#FFD3AE";   // #RRGGBB
    public double CenterOpacity { get; set; } = 0.9; // 0..1 alpha at the centre
    public bool Top { get; set; }                    // false = anchored to the bottom edge; true = the top edge
}

/// <summary>Builds + persists the code-generated Sakura backdrop's glow layer (#556). The glows live in a
/// fixed-height band at the bottom of the window (see the AppBackdropGlowBrush Border in MainWindow), so a
/// glow's height stays constant regardless of window height. Shared by the theme (which applies it) and the
/// Developer gradient editor (which tunes it live).</summary>
public static class GradientBackground
{
    /// <summary>Height of the bottom glow band, px. Glow heights are measured against this, so it MUST
    /// match the Height of the glow-band Border in MainWindow.axaml.</summary>
    public const double BandHeight = 600;

    /// <summary>Default flat base colour that fills the whole window behind the glows.</summary>
    public const string DefaultBaseColor = "#FCE7EE";

    private const string Key = "Sakura:CodeGenGlows";
    private const string BaseColorKey = "Sakura:CodeGenBaseColor";

    /// <summary>The persisted flat base colour (editable in Developer → Gradients), or the default.</summary>
    public static string LoadBaseColor() => AppSettings.Get(BaseColorKey) ?? DefaultBaseColor;

    public static void SaveBaseColor(string hex) => AppSettings.Set(BaseColorKey, hex);

    public static List<GradientGlow> Defaults() => new()
    {
        // Bottom edge: two warm peach glows anchored left / right, a soft magenta core and a small
        // hot-pink accent centred low. Top edge: a broad faint magenta wash and a blue corner accent.
        // Tuned in the Developer → Gradients editor (#556).
        new() { CenterX = 0.10, Width = 0.833, HeightPx = 99, Color = "#FFD3AE", CenterOpacity = 0.905 },
        new() { CenterX = 0.90, Width = 0.843, HeightPx = 102, Color = "#FFD3AE", CenterOpacity = 0.90 },
        new() { CenterX = 0.58, Width = 0.645, HeightPx = 44, Color = "#EE5FA7", CenterOpacity = 0.313 },
        new() { CenterX = 0.581, Width = 0.246, HeightPx = 34, Color = "#FF2E70", CenterOpacity = 0.630 },
        new() { CenterX = 0.50, Width = 1.5, HeightPx = 150, Color = "#FF00EE", CenterOpacity = 0.136, Top = true },
        new() { CenterX = 1.00, Width = 0.465, HeightPx = 46, Color = "#007BFF", CenterOpacity = 0.255, Top = true },
    };

    public static List<GradientGlow> Load() => Parse(AppSettings.Get(Key));

    /// <summary>Parse persisted glow JSON, falling back to <see cref="Defaults"/> for null/blank/invalid
    /// input. Pure (no settings access) so the fallback behaviour is unit-testable.</summary>
    public static List<GradientGlow> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults();
        try { return JsonConvert.DeserializeObject<List<GradientGlow>>(json) ?? Defaults(); }
        catch { return Defaults(); }
    }

    public static void Save(IEnumerable<GradientGlow> glows) =>
        AppSettings.Set(Key, JsonConvert.SerializeObject(glows.ToList()));

    /// <summary>Human-readable JSON for the editor's copy box (and for pasting back into Defaults()).
    /// Emits the whole background — base colour + glows — as one object so it round-trips as a unit.</summary>
    public static string Serialize(string baseColor, IEnumerable<GradientGlow> glows) =>
        JsonConvert.SerializeObject(new { BaseColor = baseColor, Glows = glows.ToList() }, Formatting.Indented);

    /// <summary>The glow layer for one edge as a DrawingBrush, sized to fill that edge's fixed-height band.
    /// Pass <paramref name="top"/> = false for the bottom band, true for the top band; only glows anchored to
    /// that edge are drawn, so a window can host both a bottom and a top glow brush from the same glow list.</summary>
    public static DrawingBrush BuildGlowBrush(IEnumerable<GradientGlow> glows, bool top = false)
    {
        var group = new DrawingGroup();
        foreach (var g in glows.Where(g => g.Top == top))
        {
            var c = ParseColor(g.Color);
            var alpha = (byte)Math.Clamp(g.CenterOpacity * 255, 0, 255);
            group.Children.Add(GlowLayer(Color.FromArgb(alpha, c.R, c.G, c.B),
                cx: g.CenterX, cy: top ? 0.0 : 1.0, rx: g.Width, ry: g.HeightPx / BandHeight));
        }
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }

    // One soft radial glow (colour at the centre → transparent at the radius) over the full 0..1 rect,
    // anchored to an edge (cy = 1 bottom, 0 top). rx is relative to the window width, ry to the band height.
    private static GeometryDrawing GlowLayer(Color color, double cx, double cy, double rx, double ry) => new()
    {
        Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1)),
        Brush = new RadialGradientBrush
        {
            Center = new RelativePoint(cx, cy, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(cx, cy, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(rx, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(ry, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(color, 0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        },
    };

    private static Color ParseColor(string hex)
    {
        try { return Color.Parse(hex); } catch { return Colors.Magenta; }
    }
}
