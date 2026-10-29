using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

// The geometry behind the tilt tab's view from above: where the top of the pen sits relative to its tip.
public class TiltGeometryTests
{
    [Fact]
    public void AnUprightPen_SitsOnItsTip_WhicheverWayItIsSaidToLean()
    {
        foreach (var az in new[] { 0.0, 90, 200, 359 })
        {
            var (x, y) = TiltGeometry.Footprint(az, 90);
            Assert.Equal(0, x, 6);
            Assert.Equal(0, y, 6);
        }
    }

    [Theory]
    [InlineData(0, 1, 0)]        // leaning toward +X: the top is to the right
    [InlineData(90, 0, 1)]       // toward +Y: down the page
    [InlineData(180, -1, 0)]
    [InlineData(270, 0, -1)]
    public void APenLyingFlat_ReachesItsFullLength_InTheDirectionItLeans(double azimuth, double x, double y)
    {
        var (fx, fy) = TiltGeometry.Footprint(azimuth, 0);

        Assert.Equal(x, fx, 6);
        Assert.Equal(y, fy, 6);
    }

    [Fact]
    public void TheReach_ShrinksAsThePenStandsUp()
    {
        double Reach(double alt) { var (x, y) = TiltGeometry.Footprint(45, alt); return Math.Sqrt(x * x + y * y); }

        Assert.True(Reach(20) > Reach(50));
        Assert.True(Reach(50) > Reach(80));
        Assert.Equal(Math.Cos(50 * Math.PI / 180), Reach(50), 6);
    }

    [Fact]
    public void TheDiagramAndTheNumbers_AgreeOnWhichWayAPenLeans()
    {
        // The pen leans toward +X and +Y: the numbers say so, and so does the picture.
        float az = TiltMath.Azimuth(30, 30);
        float alt = TiltMath.Altitude(30, 30);
        var (x, y) = TiltGeometry.Footprint(az, alt);

        Assert.InRange(az, 44, 46);
        Assert.True(x > 0 && y > 0);
    }
}
