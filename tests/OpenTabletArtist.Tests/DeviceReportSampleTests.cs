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
    public void TryParse_ReadsHoverDistance_WhenPresent()
    {
        var r = Report();
        r["Data"]!["HoverDistance"] = 42;

        Assert.True(DeviceReportSample.TryParse(r, out var s));
        Assert.Equal(42, s.HoverDistance);
    }

    [Fact]
    public void TryParse_HoverDistanceNull_WhenAbsent()
    {
        Assert.True(DeviceReportSample.TryParse(Report(), out var s));
        Assert.Null(s.HoverDistance);
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

    [Fact]
    public void TryParseAuxButtons_ReadsPressedState()
    {
        var r = JObject.Parse("""{ "Data": { "AuxButtons": [false, true, false, true] } }""");

        Assert.True(DeviceReportSample.TryParseAuxButtons(r, out var aux));
        Assert.Equal(new[] { false, true, false, true }, aux);
    }

    [Fact]
    public void TryParseAuxButtons_PenOnlyReport_ReturnsFalse()
    {
        // A pen report carries no AuxButtons → callers leave the last-known press state untouched.
        Assert.False(DeviceReportSample.TryParseAuxButtons(Report(), out _));
    }

    [Fact]
    public void TryParseWheelButtons_ReadsJaggedState()
    {
        var r = JObject.Parse("""{ "Data": { "WheelButtons": [[true, false], [false]] } }""");

        Assert.True(DeviceReportSample.TryParseWheelButtons(r, out var wheels));
        Assert.Equal(2, wheels.Length);
        Assert.Equal(new[] { true, false }, wheels[0]);
        Assert.Equal(new[] { false }, wheels[1]);
    }

    [Fact]
    public void TryParseWheelButtons_PenOnlyReport_ReturnsFalse()
    {
        Assert.False(DeviceReportSample.TryParseWheelButtons(Report(), out _));
    }

    [Fact]
    public void TryParseWheelPositions_ReadsNullableEntries()
    {
        var r = JObject.Parse("""{ "Data": { "AnalogPositions": [42, null, 7] } }""");

        Assert.True(DeviceReportSample.TryParseWheelPositions(r, out var pos));
        Assert.Equal(3, pos.Length);
        Assert.Equal((uint?)42, pos[0]);
        Assert.Null(pos[1]);
        Assert.Equal((uint?)7, pos[2]);
    }

    [Fact]
    public void TryParseWheelDeltas_ReadsSignedSteps()
    {
        var r = JObject.Parse("""{ "Data": { "AnalogDeltas": [-1, 0, 3] } }""");

        Assert.True(DeviceReportSample.TryParseWheelDeltas(r, out var deltas));
        Assert.Equal(new[] { -1, 0, 3 }, deltas);
    }
}
