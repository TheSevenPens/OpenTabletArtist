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
