using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Newtonsoft.Json;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The code-generated Sakura backdrop (#556). Parsing falls back to the baked defaults for any bad input;
/// serialization round-trips; brush composition filters by edge and colours the glows.
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
            new() { CenterX = 0.25, Width = 0.7, HeightPx = 120, Color = "#AABBCC", CenterOpacity = 0.5, Top = true },
            new() { CenterX = 0.75, Width = 0.3, HeightPx = 40, Color = "#112233", CenterOpacity = 0.9, Top = false },
        };
        var json = JsonConvert.SerializeObject(glows);

        var parsed = GradientBackground.Parse(json);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(0.25, parsed[0].CenterX);
        Assert.Equal("#AABBCC", parsed[0].Color);
        Assert.True(parsed[0].Top);
        Assert.Equal("#112233", parsed[1].Color);
        Assert.False(parsed[1].Top);
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
            new() { CenterX = 0.1, Width = 0.5, HeightPx = 100, Color = "#FFD3AE", CenterOpacity = 0.9 },
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
            new() { Color = "#FFD3AE", Top = false },
            new() { Color = "#EE5FA7", Top = false },
            new() { Color = "#007BFF", Top = true },
        };

        var bottom = GradientBackground.BuildGlowBrush(glows, top: false);
        var top = GradientBackground.BuildGlowBrush(glows, top: true);

        Assert.Equal(2, LayerCount(bottom));
        Assert.Equal(1, LayerCount(top));
        Assert.Equal(Stretch.Fill, bottom.Stretch);
    }

    [Fact]
    public void BuildGlowBrush_AppliesCenterOpacityToTheStartStop()
    {
        var glows = new List<GradientGlow> { new() { Color = "#FF0000", CenterOpacity = 0.5, Top = false } };

        var brush = GradientBackground.BuildGlowBrush(glows, top: false);

        var layer = ((DrawingGroup)brush.Drawing!).Children.OfType<GeometryDrawing>().Single();
        var radial = Assert.IsType<RadialGradientBrush>(layer.Brush);
        var center = radial.GradientStops[0];
        Assert.Equal((byte)127, center.Color.A); // (byte)(0.5 * 255) truncates to 127
        Assert.Equal((byte)0xFF, center.Color.R);
        // Fades to fully transparent at the edge.
        Assert.Equal(0, radial.GradientStops[^1].Color.A);
    }

    [Fact]
    public void BadColour_FallsBackToMagenta_WithoutThrowing()
    {
        var glows = new List<GradientGlow> { new() { Color = "not-a-colour", Top = false } };

        var brush = GradientBackground.BuildGlowBrush(glows, top: false); // must not throw

        var layer = ((DrawingGroup)brush.Drawing!).Children.OfType<GeometryDrawing>().Single();
        var radial = Assert.IsType<RadialGradientBrush>(layer.Brush);
        var c = radial.GradientStops[0].Color;
        Assert.Equal(Colors.Magenta.R, c.R);
        Assert.Equal(Colors.Magenta.B, c.B);
    }

    private static int LayerCount(DrawingBrush brush) =>
        ((DrawingGroup)brush.Drawing!).Children.OfType<GeometryDrawing>().Count();
}
