using OtdArtist.Domain;
using Xunit;

namespace OtdArtist.Tests;

public class DiagnosticsMathTests
{
    private const int Precision = 4;

    // --- Report rate EMA ---

    [Fact]
    public void Ema_SeedsOnFirstSample()
        => Assert.Equal(8.0, DiagnosticsMath.UpdateReportPeriodEma(0, 8.0), Precision);

    [Fact]
    public void Ema_SmoothsTowardNewSample()
        // 10 + (20 - 10) * 0.05 = 10.5
        => Assert.Equal(10.5, DiagnosticsMath.UpdateReportPeriodEma(10.0, 20.0), Precision);

    [Theory]
    [InlineData(8.0, 125.0)]    // 1000/8
    [InlineData(3.75, 267.0)]   // 1000/3.75 = 266.67 -> 267
    [InlineData(0.0, 0.0)]
    [InlineData(-5.0, 0.0)]
    public void ReportRateHz_FromPeriod(double periodMs, double expectedHz)
        => Assert.Equal(expectedHz, DiagnosticsMath.ReportRateHz(periodMs), Precision);

    // --- Tilt ---

    [Theory]
    [InlineData(0, 1, 0)]     // straight "up"
    [InlineData(1, 0, 90)]    // to the side
    [InlineData(0, -1, 180)]
    [InlineData(-1, 0, 270)]  // negative normalized into [0,360)
    public void TiltAzimuth_IsNormalizedDegrees(double x, double y, double expected)
        => Assert.Equal(expected, DiagnosticsMath.TiltAzimuthDegrees(x, y), Precision);

    [Fact]
    public void TiltAltitude_IsNinetyMinusMagnitude()
    {
        Assert.Equal(90.0, DiagnosticsMath.TiltAltitudeDegrees(0, 0), Precision);
        Assert.Equal(85.0, DiagnosticsMath.TiltAltitudeDegrees(3, 4), Precision); // 90 - 5
    }

    // --- Pressure ---

    [Theory]
    [InlineData(512, 1024, 50)]
    [InlineData(1024, 1024, 100)]
    [InlineData(0, 1024, 0)]
    [InlineData(500, 0, 0)]     // no max -> 0, not a divide-by-zero
    public void PressurePercent_OfMax(double pressure, double max, double expected)
        => Assert.Equal(expected, DiagnosticsMath.PressurePercent(pressure, max), Precision);

    // --- Raw hex ---

    [Fact]
    public void FormatRawHex_DecodesBase64ToSpacedHex()
        => Assert.Equal("01 02 03", DiagnosticsMath.FormatRawHex("AQID")); // bytes {1,2,3}

    [Fact]
    public void FormatRawHex_InvalidInput_ReturnsInput()
        => Assert.Equal("!!!not-base64", DiagnosticsMath.FormatRawHex("!!!not-base64"));
}
