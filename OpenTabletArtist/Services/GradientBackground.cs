using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace OpenTabletArtist.Services;

/// <summary>How a glow is painted. <see cref="Radial"/> is a soft blob centred somewhere along its edge;
/// <see cref="Linear"/> is a wash spanning the whole edge and running straight in from it (#glow-linear).</summary>
public enum GlowStyle { Radial, Linear }

/// <summary>The window edge a glow is anchored to; its reach is measured inward from there. Radial glows
/// are offered only on Bottom/Top in the editor — see <see cref="GradientGlow.Style"/>.</summary>
public enum GlowEdge { Bottom, Top, Left, Right }

/// <summary>One glow in the code-generated Sakura backdrop (#556). <see cref="CenterX"/>/<see cref="Width"/>
/// are relative (0..1 along the anchored edge) and radial-only — a linear glow spans its whole edge, so it
/// carries <see cref="Falloff"/> instead. <see cref="ReachPx"/> is absolute (measured against the fixed glow
/// band) and means the same thing for both styles. Serialized to JSON so the Developer gradient editor can
/// round-trip and share settings.</summary>
public sealed class GradientGlow
{
    public double CenterX { get; set; } = 0.5;       // 0..1 along the edge (radial only)
    public double Width { get; set; } = 0.5;         // relative radius along the edge (radial only)
    public double ReachPx { get; set; } = 150;       // absolute reach inward from the edge, px
    public string Color { get; set; } = "#FFD3AE";   // #RRGGBB
    public double CenterOpacity { get; set; } = 0.9; // 0..1 alpha at the edge

    [JsonConverter(typeof(StringEnumConverter))]
    public GlowStyle Style { get; set; } = GlowStyle.Radial;

    [JsonConverter(typeof(StringEnumConverter))]
    public GlowEdge Edge { get; set; } = GlowEdge.Bottom;

    /// <summary>Linear only: where the wash reaches half its opacity, as a fraction of the reach. 0.5 is a
    /// straight fade; lower drops off near the edge, higher holds the colour and falls away late.</summary>
    public double Falloff { get; set; } = 0.5;

    // Settings written before linear glows existed carried `Top: bool` and `HeightPx` instead of Edge and
    // ReachPx. Set-only, so Newtonsoft reads them from an old file but never writes them back out — one
    // save re-emits the file in the new shape. Never assign these from code.
    [JsonProperty("Top")]
    public bool LegacyTop { set => Edge = value ? GlowEdge.Top : GlowEdge.Bottom; }

    [JsonProperty("HeightPx")]
    public double LegacyHeightPx { set => ReachPx = value; }
}

/// <summary>Builds + persists the code-generated Sakura backdrop's glow layer (#556). The glows live in
/// fixed-thickness bands along the window edges (see the glow-band Borders in MainWindow), so a glow's reach
/// stays constant regardless of window size. Shared by the theme (which applies it) and the Developer
/// gradient editor (which tunes it live).</summary>
public static class GradientBackground
{
    /// <summary>Thickness of each glow band, px. Glow reaches are measured against this, so it MUST match
    /// the Height/Width of the glow-band Borders in MainWindow.axaml.</summary>
    public const double BandHeight = 600;

    /// <summary>Default flat base colour that fills the whole window behind the glows.</summary>
    public const string DefaultBaseColor = "#FCE7EE";

    /// <summary>Every edge, in the order the editor lists them.</summary>
    public static readonly IReadOnlyList<GlowEdge> AllEdges =
        new[] { GlowEdge.Bottom, GlowEdge.Top, GlowEdge.Left, GlowEdge.Right };

    /// <summary>The edges a glow of <paramref name="style"/> may be anchored to. Radial stays on the two
    /// horizontal edges: a side-anchored blob is not a shape this backdrop wants, and leaving it out keeps
    /// the two side bands to the linear washes they were added for.</summary>
    public static IReadOnlyList<GlowEdge> EdgesFor(GlowStyle style) =>
        style == GlowStyle.Radial ? new[] { GlowEdge.Bottom, GlowEdge.Top } : AllEdges;

    /// <summary>The application resource key holding the glow brush for <paramref name="edge"/>.</summary>
    public static string BrushKey(GlowEdge edge) => edge switch
    {
        GlowEdge.Bottom => "AppBackdropGlowBrush",
        GlowEdge.Top => "AppBackdropGlowTopBrush",
        GlowEdge.Left => "AppBackdropGlowLeftBrush",
        _ => "AppBackdropGlowRightBrush",
    };

    private const string Key = "Sakura:CodeGenGlows";
    private const string BaseColorKey = "Sakura:CodeGenBaseColor";

    /// <summary>The persisted flat base colour (editable in Developer → Gradients), or the default.</summary>
    public static string LoadBaseColor() => AppSettings.Get(BaseColorKey) ?? DefaultBaseColor;

    public static void SaveBaseColor(string hex) => AppSettings.Set(BaseColorKey, hex);

    public static List<GradientGlow> Defaults() => new()
    {
        // Four full-width washes, two per horizontal edge: a deep peach band along the bottom with a thin
        // magenta line sitting in it, and a broad faint magenta wash down from the top with a hot-pink line
        // at its lip. Tuned in the Developer → Gradients editor (#556).
        //
        // These replaced six radial blobs when linear glows arrived (#glow-linear) — a wash reads as an even
        // band of light along an edge, where four overlapping blobs left visible lobes on a wide window.
        // CenterX and Width are carried but unused (they are radial's), so a glow switched to radial in the
        // editor starts from the placement its blob had.
        new()
        {
            CenterX = 0.0, Width = 0.833, ReachPx = 143.47826086956522, Color = "#FFBC83",
            CenterOpacity = 0.905, Style = GlowStyle.Linear, Falloff = 0.2459875776397517,
        },
        new()
        {
            CenterX = 0.58, Width = 0.9245031055900623, ReachPx = 16.77018633540373, Color = "#D71C7A",
            CenterOpacity = 0.21180124223602498, Style = GlowStyle.Linear, Falloff = 0.15416149068322982,
        },
        new()
        {
            CenterX = 0.5, Width = 1.5, ReachPx = 211.30434782608697, Color = "#FF00EE",
            CenterOpacity = 0.136, Style = GlowStyle.Linear, Edge = GlowEdge.Top,
            Falloff = 0.22332919254658395,
        },
        new()
        {
            CenterX = 1.0, Width = 0.465, ReachPx = 19.75155279503114, Color = "#FF0096",
            CenterOpacity = 0.22670807453416142, Style = GlowStyle.Linear, Edge = GlowEdge.Top,
            Falloff = 0.08499378881987595,
        },
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

    /// <summary>Put every edge's glow brush into <paramref name="resources"/>. Both the theme applier and the
    /// live editor go through here, so an edge is wired up in one place.</summary>
    public static void ApplyGlowBrushes(IResourceDictionary resources, IReadOnlyList<GradientGlow> glows)
    {
        foreach (var edge in AllEdges) resources[BrushKey(edge)] = BuildGlowBrush(glows, edge);
    }

    /// <summary>The glow layer for one edge as a DrawingBrush, sized to fill that edge's fixed-thickness
    /// band. Only glows anchored to <paramref name="edge"/> are drawn, so one glow list feeds all four
    /// bands.</summary>
    public static DrawingBrush BuildGlowBrush(IEnumerable<GradientGlow> glows, GlowEdge edge)
    {
        var group = new DrawingGroup();
        foreach (var g in glows.Where(g => g.Edge == edge))
            group.Children.Add(GlowLayer(g, g.ReachPx / BandHeight));
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }

    /// <summary>One glow painted over <paramref name="baseColor"/>, for the editor's preview strip and list
    /// chips. <paramref name="reachFraction"/> is how far the glow reaches across the preview: pass
    /// <c>ReachPx / BandHeight</c> for a to-scale strip, or a fixed value for a chip far too small to show
    /// the real ratio — there it only has to say which colour is coming from which edge.</summary>
    public static DrawingBrush BuildPreviewBrush(GradientGlow glow, string baseColor, double reachFraction)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1)),
            Brush = new SolidColorBrush(ParseColor(baseColor)),
        });
        group.Children.Add(GlowLayer(glow, reachFraction));
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }

    // One glow over the full 0..1 rect of its band. `reach` is the fraction of the band the glow spans,
    // measured inward from the anchored edge.
    private static GeometryDrawing GlowLayer(GradientGlow g, double reach)
    {
        var c = ParseColor(g.Color);
        var color = Color.FromArgb((byte)Math.Clamp(g.CenterOpacity * 255, 0, 255), c.R, c.G, c.B);
        return new GeometryDrawing
        {
            Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1)),
            Brush = g.Style == GlowStyle.Linear ? LinearLayer(g, color, reach) : RadialLayer(g, color, reach),
        };
    }

    // Colour at the edge → transparent at the radius. The centre sits ON the edge, so half the blob is
    // outside the band and what shows is the inner half — which is what makes it read as a glow rather
    // than a circle.
    private static IBrush RadialLayer(GradientGlow g, Color color, double reach)
    {
        // Along the edge vs. inward: on a side edge the roles of the two axes swap.
        var (cx, cy, rx, ry) = g.Edge switch
        {
            GlowEdge.Top => (g.CenterX, 0.0, g.Width, reach),
            GlowEdge.Bottom => (g.CenterX, 1.0, g.Width, reach),
            GlowEdge.Left => (0.0, g.CenterX, reach, g.Width),
            _ => (1.0, g.CenterX, reach, g.Width),
        };
        return new RadialGradientBrush
        {
            Center = new RelativePoint(cx, cy, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(cx, cy, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(rx, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(ry, RelativeUnit.Relative),
            GradientStops = Stops(color, null),
        };
    }

    // A wash spanning the whole edge, running in along the band's short axis. SpreadMethod defaults to Pad,
    // so past the reach the last (transparent) stop holds and the rest of the band stays clear.
    private static IBrush LinearLayer(GradientGlow g, Color color, double reach)
    {
        var r = Math.Clamp(reach, 0.0001, 1);
        var (start, end) = g.Edge switch
        {
            GlowEdge.Top => ((0.0, 0.0), (0.0, r)),
            GlowEdge.Bottom => ((0.0, 1.0), (0.0, 1 - r)),
            GlowEdge.Left => ((0.0, 0.0), (r, 0.0)),
            _ => ((1.0, 0.0), (1 - r, 0.0)),
        };
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(start.Item1, start.Item2, RelativeUnit.Relative),
            EndPoint = new RelativePoint(end.Item1, end.Item2, RelativeUnit.Relative),
            GradientStops = Stops(color, Math.Clamp(g.Falloff, 0.02, 0.98)),
        };
    }

    // Full alpha at the edge, nothing at the reach. A linear wash also gets a half-alpha stop at `falloff`,
    // which is what bends the fade; a radial glow has no such knob and fades straight.
    private static GradientStops Stops(Color color, double? falloff)
    {
        var stops = new GradientStops { new GradientStop(color, 0) };
        if (falloff is { } f)
            stops.Add(new GradientStop(Color.FromArgb((byte)(color.A / 2), color.R, color.G, color.B), f));
        stops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        return stops;
    }

    private static Color ParseColor(string hex)
    {
        try { return Color.Parse(hex); } catch { return Colors.Magenta; }
    }
}
