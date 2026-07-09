using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class AreaMappingCalculatorTests
{
    // Tolerance for float comparisons (the calc casts double->float internally).
    private const int Precision = 3;

    [Fact]
    public void WiderDisplay_UsesFullWidth_AndReducesHeight()
    {
        // Tablet ~1.6 aspect (152x95), 16:9 display is wider → keep full width, shrink height.
        var area = AreaMappingCalculator.FitToDisplayAspect(152f, 95f, 1920, 1080);

        Assert.Equal(152f, area.Width, Precision);
        Assert.Equal(152f * 1080f / 1920f, area.Height, Precision); // 85.5
    }

    [Fact]
    public void TallerDisplay_UsesFullHeight_AndReducesWidth()
    {
        // Portrait 9:16 display is taller than the tablet → keep full height, shrink width.
        var area = AreaMappingCalculator.FitToDisplayAspect(152f, 95f, 1080, 1920);

        Assert.Equal(95f, area.Height, Precision);
        Assert.Equal(95f * 1080f / 1920f, area.Width, Precision); // 53.4375
    }

    [Fact]
    public void ExactAspectMatch_ReturnsFullArea()
    {
        // Tablet and display share 16:9 → the whole digitizer is used, undistorted.
        var area = AreaMappingCalculator.FitToDisplayAspect(160f, 90f, 1920, 1080);

        Assert.Equal(160f, area.Width, Precision);
        Assert.Equal(90f, area.Height, Precision);
    }

    [Fact]
    public void FitForRotation_0And180_MatchUnrotatedFit()
    {
        var expected = AreaMappingCalculator.FitToDisplayAspect(300f, 170f, 1920, 1080);
        foreach (var rot in new[] { 0, 180, 360, -180 })
        {
            var area = AreaMappingCalculator.FitForRotation(300f, 170f, 1920, 1080, rot);
            Assert.Equal(expected.Width, area.Width, Precision);
            Assert.Equal(expected.Height, area.Height, Precision);
            Assert.Equal(150f, area.X, Precision);   // centred on the real tablet
            Assert.Equal(85f, area.Y, Precision);
        }
    }

    [Fact]
    public void ClampArea_KeepsCentre_WhenAreaFitsInBounds()
    {
        // A 100x56 area centred in a 300x170 tablet, no rotation → unchanged.
        var a = AreaMappingCalculator.ClampArea(100f, 56f, 150f, 85f, 0, 300f, 170f, 10f);
        Assert.Equal(100f, a.Width, Precision);
        Assert.Equal(56f, a.Height, Precision);
        Assert.Equal(150f, a.X, Precision);
        Assert.Equal(85f, a.Y, Precision);
    }

    [Fact]
    public void ClampArea_ClampsCentre_SoTheFootprintStaysInBounds()
    {
        // 100x56 area pushed to the far right — centre clamps so the right edge sits on the tablet edge.
        var a = AreaMappingCalculator.ClampArea(100f, 56f, 999f, 85f, 0, 300f, 170f, 10f);
        Assert.Equal(250f, a.X, Precision);   // 300 - 100/2
        Assert.Equal(85f, a.Y, Precision);
    }

    [Fact]
    public void ClampArea_ShrinksToFit_WhenTooBig_PreservingAspect()
    {
        // 600x336 (16:9-ish) into a 300x170 tablet → shrink to fit, aspect preserved.
        var a = AreaMappingCalculator.ClampArea(600f, 336f, 150f, 85f, 0, 300f, 170f, 10f);
        Assert.True(a.Width <= 300f && a.Height <= 170f);
        Assert.Equal(600f / 336f, a.Width / a.Height, Precision);   // aspect kept
    }

    [Fact]
    public void ClampArea_90_UsesTheRotatedFootprint()
    {
        // At 90°, a 170x96 area's footprint is 96 wide x 170 tall → it fits a 300x170 tablet, and the
        // centre clamps against the SWAPPED extents.
        var a = AreaMappingCalculator.ClampArea(170f, 96f, 999f, 999f, 90, 300f, 170f, 10f);
        Assert.Equal(170f, a.Width, Precision);
        Assert.Equal(96f, a.Height, Precision);
        Assert.Equal(300f - 96f / 2f, a.X, Precision);   // footprint width = height (96)
        Assert.Equal(170f - 170f / 2f, a.Y, Precision);  // footprint height = width (170)
    }

    [Fact]
    public void FitForRotation_90And270_FitTheRotatedBoundingBox_ToTheTablet()
    {
        // 300x170 tablet, 16:9 display, rotated 90°: the area keeps display aspect (Width/Height = 16/9)
        // but its 90°-rotated bounding box (Height x Width) must fit the tablet — i.e. fit 16:9 into the
        // SWAPPED tablet (170 wide x 300 tall) → width-limited to 170.
        foreach (var rot in new[] { 90, 270 })
        {
            var area = AreaMappingCalculator.FitForRotation(300f, 170f, 1920, 1080, rot);

            Assert.Equal(170f, area.Width, Precision);
            Assert.Equal(170f * 1080f / 1920f, area.Height, Precision); // 95.625
            // display aspect preserved → uniform scale → no distortion
            Assert.Equal(1920f / 1080f, area.Width / area.Height, Precision);
            // rotated bounding box (Height across X, Width across Y) fits the 300x170 tablet
            Assert.True(area.Height <= 300f && area.Width <= 170f);
            Assert.Equal(150f, area.X, Precision);   // still centred on the real tablet
            Assert.Equal(85f, area.Y, Precision);
        }
    }

    [Fact]
    public void SquareDisplay_ShrinksToSquareWithinTablet()
    {
        // 1:1 display is "taller" than a 1.6 tablet → height kept, width = height.
        var area = AreaMappingCalculator.FitToDisplayAspect(152f, 95f, 1000, 1000);

        Assert.Equal(95f, area.Height, Precision);
        Assert.Equal(95f, area.Width, Precision);
    }

    [Fact]
    public void Result_IsAlwaysCenteredOnDigitizer()
    {
        var area = AreaMappingCalculator.FitToDisplayAspect(152f, 95f, 2560, 1440);

        Assert.Equal(76f, area.X, Precision);   // fullWidth / 2
        Assert.Equal(47.5f, area.Y, Precision); // fullHeight / 2
    }

    [Fact]
    public void FittedArea_NeverExceedsDigitizer()
    {
        // Sweep a range of display aspects; the result must always fit inside the digitizer.
        const float fw = 152f, fh = 95f;
        (int W, int H)[] displays =
        {
            (1920, 1080), (1080, 1920), (2560, 1440), (1024, 768), (3440, 1440), (800, 1280),
        };

        foreach (var (w, h) in displays)
        {
            var area = AreaMappingCalculator.FitToDisplayAspect(fw, fh, w, h);
            Assert.True(area.Width <= fw + 0.001f, $"width {area.Width} > {fw} for {w}x{h}");
            Assert.True(area.Height <= fh + 0.001f, $"height {area.Height} > {fh} for {w}x{h}");
            // Aspect of the fitted area should match the display aspect.
            Assert.Equal((double)w / h, area.Width / area.Height, 3);
        }
    }

    [Theory]
    [InlineData(0f, 95f, 1920, 1080)]
    [InlineData(152f, 0f, 1920, 1080)]
    [InlineData(-1f, 95f, 1920, 1080)]
    [InlineData(152f, 95f, 0, 1080)]
    [InlineData(152f, 95f, 1920, 0)]
    public void NonPositiveDimensions_Throw(float fullW, float fullH, int dispW, int dispH)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AreaMappingCalculator.FitToDisplayAspect(fullW, fullH, dispW, dispH));
    }
}
