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

    /// <summary>Default flat base colours behind the glows, one per blossom skin. Dark Sakura's is the
    /// field colour of the artwork this backdrop replaced, as the eye saw it — #23000D through that skin's
    /// #C4160910 scrim — so the skin opens looking as it did.</summary>
    public const string SakuraBaseColor = "#FCE7EE";
    public const string DarkSakuraBaseColor = "#19070F";

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

    // Both blossom skins keep their own glows and base colour — a set tuned for a pale pink page is
    // meaningless on a near-black one. Sakura's keys are unchanged from when it was the only one.
    private static string Key(string skin) => $"{skin}:CodeGenGlows";
    private static string BaseColorKey(string skin) => $"{skin}:CodeGenBaseColor";

    /// <summary>The blossom skin whose backdrop is on screen, or null if the current skin has none. The
    /// light skin persists under "Sakura" though its theme id is "Anime", for historical reasons.</summary>
    public static string? ActiveSkin => ThemeService.SavedChoice switch
    {
        ThemeService.Anime => SkinColorSettings.SakuraSkin,
        ThemeService.DarkSakura => SkinColorSettings.DarkSakuraSkin,
        _ => null,
    };

    /// <summary>Whether the code-generated backdrop is the thing currently on screen: a blossom skin, with
    /// its background set to codegen rather than to a flat colour.
    ///
    /// Both halves matter. The live editor used to test only the background mode, which is not a skin
    /// check, so editing a glow under any other skin wrote the glow brushes anyway.
    ///
    /// `ThemeViewModel.RefreshSkin` gates on its own selection rather than this, deliberately: it is
    /// deciding what to WRITE for the skin being applied, where this asks what is SHOWING.</summary>
    public static bool IsShowing =>
        ActiveSkin is { } skin && SkinColorSettings.Background(skin) == "codegen";

    /// <summary>The flat colour that fills the window behind <paramref name="skin"/>'s glows.</summary>
    public static string DefaultBaseColor(string skin) =>
        skin == SkinColorSettings.DarkSakuraSkin ? DarkSakuraBaseColor : SakuraBaseColor;

    /// <summary>The persisted flat base colour (editable on Appearance), or the skin's default.</summary>
    public static string LoadBaseColor(string skin) =>
        AppSettings.Get(BaseColorKey(skin)) ?? DefaultBaseColor(skin);

    /// <summary>The current skin's base colour — for the editor's previews, which are drawn outside the
    /// theme applier and have no skin of their own. Falls back to Sakura's under a skin with no
    /// generated backdrop, where the editor has nothing to show anyway.</summary>
    public static string ActiveBaseColor => LoadBaseColor(ActiveSkin ?? SkinColorSettings.SakuraSkin);

    public static void SaveBaseColor(string skin, string hex) => AppSettings.Set(BaseColorKey(skin), hex);

    /// <summary>The baked-in glows for <paramref name="skin"/>.</summary>
    public static List<GradientGlow> Defaults(string skin) =>
        skin == SkinColorSettings.DarkSakuraSkin ? DarkSakuraDefaults() : SakuraDefaults();

    // Dark Sakura's, standing in for the backdrop artwork it replaced (#glow-darksakura): a burnt-orange
    // wash along the bottom with a red-magenta one over its middle, which is what that image had painted
    // into its bottom edge. Sampled from it through the skin's scrim rather than invented, so switching to
    // the tunable backdrop is not also a redesign — but unlike the image, these can now be turned off.
    private static List<GradientGlow> DarkSakuraDefaults() => new()
    {
        // The opacities are solved, not eyeballed: the artwork's brightest bottom pixel was #842D25, which
        // through the #C4160910 scrim reached the eye as ~#301115. These two over #19070F land there.
        new()
        {
            ReachPx = 150, Color = "#B5502A", CenterOpacity = 0.15,
            Style = GlowStyle.Linear, Falloff = 0.22,
        },
        new()
        {
            ReachPx = 88, Color = "#B0245A", CenterOpacity = 0.10,
            Style = GlowStyle.Linear, Falloff = 0.18,
        },
    };

    private static List<GradientGlow> SakuraDefaults() => new()
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

    public static List<GradientGlow> Load(string skin) => Parse(AppSettings.Get(Key(skin)), skin);

    /// <summary>Parse persisted glow JSON, falling back to <paramref name="skin"/>'s defaults for
    /// null/blank/invalid input. Pure (no settings access) so the fallback behaviour is unit-testable.</summary>
    public static List<GradientGlow> Parse(string? json, string skin = SkinColorSettings.SakuraSkin)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults(skin);
        try { return JsonConvert.DeserializeObject<List<GradientGlow>>(json) ?? Defaults(skin); }
        catch { return Defaults(skin); }
    }

    public static void Save(string skin, IEnumerable<GradientGlow> glows) =>
        AppSettings.Set(Key(skin), JsonConvert.SerializeObject(glows.ToList()));

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

    /// <summary>The window the backdrop preview stands in for. A glow's reach is absolute pixels, so a
    /// preview can only be to scale against SOME window size; these are the numbers it assumes, and the
    /// preview's own aspect ratio should match them (#appearance-merge).</summary>
    public const double PreviewWindowWidth = 1280;
    public const double PreviewWindowHeight = 800;

    /// <summary>The whole backdrop — base colour with every glow over it — as a window this size would
    /// draw it. This is what makes a base colour judgeable against the glows sitting on it, which is the
    /// pairing that used to be split across two tabs.</summary>
    public static DrawingBrush BuildBackdropPreview(string baseColor, IEnumerable<GradientGlow> glows)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1)),
            Brush = new SolidColorBrush(ParseColor(baseColor)),
        });
        foreach (var g in glows)
        {
            // Against the window, not the band: a 143px reach is a sixth of the 800px-tall preview, where
            // in its own 600px band it would be a quarter.
            var span = g.Edge is GlowEdge.Top or GlowEdge.Bottom ? PreviewWindowHeight : PreviewWindowWidth;
            group.Children.Add(GlowLayer(g, g.ReachPx / span));
        }
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
