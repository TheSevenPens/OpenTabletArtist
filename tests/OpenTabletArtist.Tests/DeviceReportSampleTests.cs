using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class DeviceReportSampleTests
{
    private static JObject Report() => JObject.Parse("""
    {
      "Tablet": { "Properties": { "Specifications": {
        "Digitizer": { "MaxX": 1000, "MaxY": 500 },
        "Pen": { "MaxPressure": 8192 }
      }}},
      "Data": {
        "Position": { "X": 500, "Y": 250 },
        "Pressure": 4096,
        "Tilt": { "X": 10, "Y": 20 }
      }
    }
    """);

    [Fact]
    public void TryParse_NormalizesPositionAndPressure()
    {
        Assert.True(DeviceReportSample.TryParse(Report(), out var s));

        Assert.Equal(0.5, s.X, 3);
        Assert.Equal(0.5, s.Y, 3);
        Assert.Equal(500, s.RawX, 3);   // raw, pre-normalization
        Assert.Equal(250, s.RawY, 3);
        Assert.Equal(0.5, s.Pressure, 3);
        Assert.Equal(10, s.TiltX, 3);
        Assert.Equal(20, s.TiltY, 3);
        Assert.True(s.IsDown);
    }

    [Fact]
    public void TryParse_ClampsToUnitRange()
    {
        var r = Report();
        r["Data"]!["Position"]!["X"] = 5000; // beyond MaxX
        r["Data"]!["Pressure"] = 99999;      // beyond MaxPressure

        Assert.True(DeviceReportSample.TryParse(r, out var s));
        Assert.Equal(1.0, s.X, 3);          // normalized X clamped to 1
        Assert.Equal(5000, s.RawX, 3);      // raw X left unclamped for debugging
        Assert.Equal(1.0, s.Pressure, 3);
    }

    [Fact]
    public void TryParse_ZeroPressure_IsNotDown()
    {
        var r = Report();
        r["Data"]!["Pressure"] = 0;

        Assert.True(DeviceReportSample.TryParse(r, out var s));
        Assert.False(s.IsDown);
    }

    [Fact]
    public void TryParse_WithoutDigitizerSpecs_ReturnsFalse()
    {
        var r = JObject.Parse("""{ "Data": { "Position": { "X": 1, "Y": 1 } } }""");
        Assert.False(DeviceReportSample.TryParse(r, out _));
    }

    [Fact]
    public void TryParse_WithoutPosition_ReturnsFalse()
    {
        var r = Report();
        ((JObject)r["Data"]!).Remove("Position");
        Assert.False(DeviceReportSample.TryParse(r, out _));
    }
}
