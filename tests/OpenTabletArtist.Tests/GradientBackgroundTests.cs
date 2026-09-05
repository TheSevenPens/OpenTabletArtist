using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Newtonsoft.Json;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The code-generated Sakura backdrop (#556). Parsing falls back to the baked defaults for any bad input and
/// still reads settings written before linear glows existed (#glow-linear); serialization round-trips; brush
/// composition filters by edge and colours the glows.
/// </summary>
public class GradientBackgroundTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{ half")]
    [InlineData("42")]                 // valid JSON, wrong shape
    public void Parse_FallsBackToDefaults_OnBadInput(string? json)
    {
        var parsed = GradientBackground.Parse(json);
        Assert.Equal(GradientBackground.Defaults().Count, parsed.Count);
        // Same content as Defaults (spot-check the first glow's colour).
        Assert.Equal(GradientBackground.Defaults()[0].Color, parsed[0].Color);
    }

    [Fact]
    public void Parse_RoundTripsValidGlows()
    {
        var glows = new List<GradientGlow>
        {
            new() { CenterX = 0.25, Width = 0.7, ReachPx = 120, Color = "#AABBCC", CenterOpacity = 0.5, Edge = GlowEdge.Top },
            new() { CenterX = 0.75, Width = 0.3, ReachPx = 40, Color = "#112233", CenterOpacity = 0.9 },
            new() { ReachPx = 80, Color = "#445566", Style = GlowStyle.Linear, Edge = GlowEdge.Left, Falloff = 0.3 },
        };
        var json = JsonConvert.SerializeObject(glows);

        var parsed = GradientBackground.Parse(json);

        Assert.Equal(3, parsed.Count);
        Assert.Equal(0.25, parsed[0].CenterX);
        Assert.Equal("#AABBCC", parsed[0].Color);
        Assert.Equal(GlowEdge.Top, parsed[0].Edge);
        Assert.Equal(GlowEdge.Bottom, parsed[1].Edge);
        Assert.Equal(GlowStyle.Linear, parsed[2].Style);
        Assert.Equal(GlowEdge.Left, parsed[2].Edge);
        Assert.Equal(0.3, parsed[2].Falloff);
        Assert.Equal(80, parsed[2].ReachPx);
    }

    [Fact]
    public void Serialize_WritesStyleAndEdgeAsNames_NotNumbers()
    {
        // The copy box is meant to be read and hand-edited, and it is pasted back into Defaults() as C#.
        var json = GradientBackground.Serialize("#FCE7EE", new List<GradientGlow>
        {
            new() { Style = GlowStyle.Linear, Edge = GlowEdge.Right },
        });

        Assert.Contains("\"Style\": \"Linear\"", json);
        Assert.Contains("\"Edge\": \"Right\"", json);
    }

    [Fact]
    public void Parse_ReadsLegacyTopAndHeightPx()
    {
        // Settings written before this change carried a bool `Top` and `HeightPx` instead of Edge/ReachPx.
        var parsed = GradientBackground.Parse(
            """
            [{"CenterX":0.1,"Width":0.8,"HeightPx":99,"Color":"#FFD3AE","CenterOpacity":0.9,"Top":true},
             {"CenterX":0.9,"Width":0.4,"HeightPx":46,"Color":"#007BFF","CenterOpacity":0.3,"Top":false}]
            """);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(GlowEdge.Top, parsed[0].Edge);
        Assert.Equal(99, parsed[0].ReachPx);
        Assert.Equal(GlowEdge.Bottom, parsed[1].Edge);
        Assert.Equal(46, parsed[1].ReachPx);
        // Nothing in an old file says "linear", so everything it holds stays radial.
        Assert.All(parsed, g => Assert.Equal(GlowStyle.Radial, g.Style));
    }

    [Fact]
    public void Serialize_DoesNotEmitTheLegacyKeys()
    {
        // Set-only properties: one save rewrites an old file in the new shape rather than carrying both.
        var json = GradientBackground.Serialize("#FCE7EE", new List<GradientGlow> { new() { ReachPx = 99 } });

        Assert.DoesNotContain("\"Top\"", json);
        Assert.DoesNotContain("\"HeightPx\"", json);
        Assert.Contains("\"ReachPx\": 99", json);
    }

    [Fact]
    public void Parse_EmptyArray_IsHonoured_NotDefaulted()
    {
        // An explicit empty list is a valid saved state (all glows removed), distinct from missing/invalid.
        var parsed = GradientBackground.Parse("[]");
        Assert.Empty(parsed);
    }

    [Fact]
    public void Serialize_EmitsBaseColourAndGlows()
    {
        var glows = new List<GradientGlow>
        {
            new() { CenterX = 0.1, Width = 0.5, ReachPx = 100, Color = "#FFD3AE", CenterOpacity = 0.9 },
        };

        var json = GradientBackground.Serialize("#FCE7EE", glows);

        // Round-trips as an object with the base colour + the glow list.
        dynamic obj = JsonConvert.DeserializeObject<dynamic>(json)!;
        Assert.Equal("#FCE7EE", (string)obj.BaseColor);
        Assert.Equal("#FFD3AE", (string)obj.Glows[0].Color);
        Assert.Equal(0.1, (double)obj.Glows[0].CenterX);
    }

    [Fact]
    public void BuildGlowBrush_FiltersByEdge()
    {
        var glows = new List<GradientGlow>
        {
            new() { Color = "#FFD3AE" },
            new() { Color = "#EE5FA7" },
            new() { Color = "#007BFF", Edge = GlowEdge.Top },
            new() { Color = "#FF00EE", Edge = GlowEdge.Left, Style = GlowStyle.Linear },
        };

        Assert.Equal(2, LayerCount(GradientBackground.BuildGlowBrush(glows, GlowEdge.Bottom)));
        Assert.Equal(1, LayerCount(GradientBackground.BuildGlowBrush(glows, GlowEdge.Top)));
        Assert.Equal(1, LayerCount(GradientBackground.BuildGlowBrush(glows, GlowEdge.Left)));
        Assert.Equal(0, LayerCount(GradientBackground.BuildGlowBrush(glows, GlowEdge.Right)));
        Assert.Equal(Stretch.Fill, GradientBackground.BuildGlowBrush(glows, GlowEdge.Bottom).Stretch);
    }

    [Fact]
    public void BuildGlowBrush_AppliesCenterOpacityToTheStartStop()
    {
        var glows = new List<GradientGlow> { new() { Color = "#FF0000", CenterOpacity = 0.5 } };

        var brush = GradientBackground.BuildGlowBrush(glows, GlowEdge.Bottom);

        var radial = Assert.IsType<RadialGradientBrush>(SingleLayer(brush).Brush);
        var center = radial.GradientStops[0];
        Assert.Equal((byte)127, center.Color.A); // (byte)(0.5 * 255) truncates to 127
        Assert.Equal((byte)0xFF, center.Color.R);
        // Fades to fully transparent at the edge, and a radial glow has no falloff stop in between.
        Assert.Equal(2, radial.GradientStops.Count);
        Assert.Equal(0, radial.GradientStops[^1].Color.A);
    }

    [Theory]
    // The wash starts ON its edge and runs inward, ending where the reach does — a third of the band here.
    [InlineData(GlowEdge.Bottom, 0d, 1d, 0d, 0.667)]
    [InlineData(GlowEdge.Top, 0d, 0d, 0d, 0.333)]
    [InlineData(GlowEdge.Left, 0d, 0d, 0.333, 0d)]
    [InlineData(GlowEdge.Right, 1d, 0d, 0.667, 0d)]
    public void LinearGlow_RunsInwardFromItsEdge(GlowEdge edge, double sx, double sy, double ex, double ey)
    {
        var glow = new GradientGlow { Style = GlowStyle.Linear, Edge = edge, ReachPx = 200 }; // 200/600

        var brush = Assert.IsType<LinearGradientBrush>(
            SingleLayer(GradientBackground.BuildGlowBrush(new[] { glow }, edge)).Brush);

        Assert.Equal(sx, brush.StartPoint.Point.X, 3);
        Assert.Equal(sy, brush.StartPoint.Point.Y, 3);
        Assert.Equal(ex, brush.EndPoint.Point.X, 3);
        Assert.Equal(ey, brush.EndPoint.Point.Y, 3);
        Assert.Equal(RelativeUnit.Relative, brush.StartPoint.Unit);
    }

    [Fact]
    public void LinearGlow_PlacesTheHalfOpacityStopAtFalloff()
    {
        var glow = new GradientGlow
        {
            Style = GlowStyle.Linear, Color = "#FF0000", CenterOpacity = 1.0, Falloff = 0.25,
        };

        var brush = Assert.IsType<LinearGradientBrush>(
            SingleLayer(GradientBackground.BuildGlowBrush(new[] { glow }, GlowEdge.Bottom)).Brush);

        Assert.Equal(3, brush.GradientStops.Count);
        Assert.Equal(0.25, brush.GradientStops[1].Offset);
        Assert.Equal((byte)127, brush.GradientStops[1].Color.A); // half of 255
        Assert.Equal(0, brush.GradientStops[^1].Color.A);
    }

    [Fact]
    public void EdgesFor_OffersAllFourToLinear_AndTheHorizontalPairToRadial()
    {
        Assert.Equal(new[] { GlowEdge.Bottom, GlowEdge.Top }, GradientBackground.EdgesFor(GlowStyle.Radial));
        Assert.Equal(GradientBackground.AllEdges, GradientBackground.EdgesFor(GlowStyle.Linear));
    }

    [Fact]
    public void BrushKey_IsDistinctPerEdge()
    {
        var keys = GradientBackground.AllEdges.Select(GradientBackground.BrushKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void BuildPreviewBrush_PaintsTheGlowOverTheBaseColour()
    {
        var brush = GradientBackground.BuildPreviewBrush(
            new GradientGlow { Color = "#FF0000" }, "#FCE7EE", 0.8);

        var layers = ((DrawingGroup)brush.Drawing!).Children.OfType<GeometryDrawing>().ToList();
        Assert.Equal(2, layers.Count);
        var flat = Assert.IsType<SolidColorBrush>(layers[0].Brush);
        Assert.Equal(Color.Parse("#FCE7EE"), flat.Color);
        Assert.IsType<RadialGradientBrush>(layers[1].Brush);
    }

    [Fact]
    public void BadColour_FallsBackToMagenta_WithoutThrowing()
    {
        var glows = new List<GradientGlow> { new() { Color = "not-a-colour" } };

        var brush = GradientBackground.BuildGlowBrush(glows, GlowEdge.Bottom); // must not throw

        var radial = Assert.IsType<RadialGradientBrush>(SingleLayer(brush).Brush);
        var c = radial.GradientStops[0].Color;
        Assert.Equal(Colors.Magenta.R, c.R);
        Assert.Equal(Colors.Magenta.B, c.B);
    }

    private static GeometryDrawing SingleLayer(DrawingBrush brush) =>
        ((DrawingGroup)brush.Drawing!).Children.OfType<GeometryDrawing>().Single();

    private static int LayerCount(DrawingBrush brush) =>
        ((DrawingGroup)brush.Drawing!).Children.OfType<GeometryDrawing>().Count();
}
