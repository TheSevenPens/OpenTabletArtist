using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Measurements carry both units, everywhere they are printed (#mapping-units).
/// </summary>
///
/// <remarks>
/// <para>
/// The mapping tab used to have a metric/imperial toggle: the artist set a switch in order to read a
/// number, and the answer then depended on how they had left that switch on some other visit. A
/// tablet's spec sheet is quoted in one unit and the display it maps to in the other, so whichever way
/// the toggle sat, half the comparisons needed it flipped.
/// </para>
/// <para>
/// The About tab had always printed both. This is here because those were two separate
/// implementations of the same sentence, and had already diverged — one showed inches to two decimals,
/// the other to one — which is what a shared formatter and a test are for.
/// </para>
/// </remarks>
public class MeasurementFormatTests
{
    /// <summary>A length reads in millimetres, with inches after it.</summary>
    [Theory]
    [InlineData(25.4, "25.4 mm  (1.0 in)")]
    [InlineData(269, "269 mm  (10.6 in)")]
    [InlineData(0, "0 mm  (0.0 in)")]
    public void ALengthCarriesBothUnits(double mm, string expected) =>
        Assert.Equal(expected, TabletAboutInfo.FormatLength(mm));

    /// <summary>And a size does the same for the pair, without repeating the units.</summary>
    /// <remarks>
    /// One "mm" and one "in" for two numbers: "269 mm × 168 mm (10.6 in × 6.6 in)" is the same fact
    /// said four times.
    /// </remarks>
    [Fact]
    public void ASizeCarriesBothUnitsOnce()
    {
        Assert.Equal("269 × 168 mm  (10.6 × 6.6 in)", TabletAboutInfo.FormatSize(269, 168));
    }

    /// <summary>
    /// Millimetres keep a tenth; inches keep a tenth too, which is coarser.
    /// </summary>
    /// <remarks>
    /// A tenth of an inch is about two and a half millimetres, so the bracketed figure is the rougher
    /// of the two. That is the About tab's long-standing choice and the mapping tab now matches it;
    /// worth knowing before anyone reads the inch value as the precise one.
    /// </remarks>
    [Fact]
    public void TheInchFigureIsTheRougherOfTheTwo()
    {
        Assert.Equal("100.1 mm  (3.9 in)", TabletAboutInfo.FormatLength(100.14));
        Assert.Equal("100.2 mm  (3.9 in)", TabletAboutInfo.FormatLength(100.16));
    }
}
