using System.Numerics;
using OtdWindowsHelper.Domain;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class AbsolutePositionMapperTests
{
    // 100x100mm tablet at 1000x1000 units; full-tablet input area; 1920x1080 display centred at (960,540).
    private static readonly TabletDigitizerSpec Digi = new(Width: 100, Height: 100, MaxX: 1000, MaxY: 1000);
    private static readonly MappingArea FullTablet = new(CenterX: 50, CenterY: 50, Width: 100, Height: 100);
    private static readonly MappingArea Display = new(CenterX: 960, CenterY: 540, Width: 1920, Height: 1080);

    private static Vector2 Map(float x, float y) =>
        AbsolutePositionMapper.MapToDesktop(new Vector2(x, y), Digi, FullTablet, Display, false, false)!.Value;

    [Fact]
    public void Center_MapsToDisplayCenter()
    {
        var p = Map(500, 500);
        Assert.Equal(960, p.X, 3);
        Assert.Equal(540, p.Y, 3);
    }

    [Fact]
    public void Origin_MapsToDisplayTopLeft()
    {
        var p = Map(0, 0);
        Assert.Equal(0, p.X, 3);
        Assert.Equal(0, p.Y, 3);
    }

    [Fact]
    public void MaxCorner_MapsToDisplayBottomRight()
    {
        var p = Map(1000, 1000);
        Assert.Equal(1920, p.X, 3);
        Assert.Equal(1080, p.Y, 3);
    }

    [Fact]
    public void HalfTabletArea_ReachesFullDisplay()
    {
        // Input area = left half of the tablet (centre 25, width 50). Raw x=500 (its right edge) → display right.
        var input = new MappingArea(CenterX: 25, CenterY: 50, Width: 50, Height: 100);
        var p = AbsolutePositionMapper.MapToDesktop(new Vector2(500, 500), Digi, input, Display, false, false)!.Value;
        Assert.Equal(1920, p.X, 3);
        Assert.Equal(540, p.Y, 3);
    }

    [Fact]
    public void Rotation90_RotatesMapping()
    {
        var input = FullTablet with { Rotation = 90 };
        // Right-centre of tablet (1000,500) under -90° → top-centre of display.
        var p = AbsolutePositionMapper.MapToDesktop(new Vector2(1000, 500), Digi, input, Display, false, false)!.Value;
        Assert.Equal(960, p.X, 2);
        Assert.Equal(0, p.Y, 2);
    }

    [Fact]
    public void AreaLimiting_DropsPointOutsideOutput()
    {
        // Tiny input area so most raw points map outside the display; limiting → null.
        var input = new MappingArea(CenterX: 50, CenterY: 50, Width: 10, Height: 10);
        var outside = AbsolutePositionMapper.MapToDesktop(new Vector2(1000, 1000), Digi, input, Display, false, true);
        Assert.Null(outside);
    }

    [Fact]
    public void AreaClipping_ClampsToOutputRect()
    {
        var input = new MappingArea(CenterX: 50, CenterY: 50, Width: 10, Height: 10);
        var clamped = AbsolutePositionMapper.MapToDesktop(new Vector2(1000, 1000), Digi, input, Display, true, false)!.Value;
        Assert.InRange(clamped.X, 0, 1920);
        Assert.InRange(clamped.Y, 0, 1080);
        Assert.Equal(1920, clamped.X, 3); // clamped to the right/bottom edge
        Assert.Equal(1080, clamped.Y, 3);
    }

    // #127: MapFromDesktop is the inverse of MapToDesktop (no clip/limit).
    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(250, 750)]
    [InlineData(1000, 1000)]
    public void MapFromDesktop_RoundTrips(float rawX, float rawY)
    {
        var raw = new Vector2(rawX, rawY);
        var screen = AbsolutePositionMapper.MapToDesktop(raw, Digi, FullTablet, Display, false, false)!.Value;
        var back = AbsolutePositionMapper.MapFromDesktop(screen, Digi, FullTablet, Display)!.Value;
        Assert.Equal(raw.X, back.X, 2);
        Assert.Equal(raw.Y, back.Y, 2);
    }

    [Fact]
    public void MapFromDesktop_RoundTrips_WithRotationAndPartialArea()
    {
        var input = new MappingArea(CenterX: 40, CenterY: 60, Width: 70, Height: 80, Rotation: 17);
        var raw = new Vector2(300, 650);
        var screen = AbsolutePositionMapper.MapToDesktop(raw, Digi, input, Display, false, false)!.Value;
        var back = AbsolutePositionMapper.MapFromDesktop(screen, Digi, input, Display)!.Value;
        Assert.Equal(raw.X, back.X, 2);
        Assert.Equal(raw.Y, back.Y, 2);
    }

    [Fact]
    public void MapFromDesktop_DegenerateInput_ReturnsNull()
    {
        var bad = new TabletDigitizerSpec(Width: 100, Height: 100, MaxX: 0, MaxY: 1000);
        Assert.Null(AbsolutePositionMapper.MapFromDesktop(new Vector2(960, 540), bad, FullTablet, Display));
    }
}
