using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class TabletAboutInfoTests
{
    // A daemon TabletReference token: Properties = TabletConfiguration, Identifiers = matched devices.
    private static JArray Tablets(string name) => new()
    {
        new JObject
        {
            ["Properties"] = new JObject
            {
                ["Name"] = name,
                ["Specifications"] = new JObject
                {
                    ["Digitizer"] = new JObject { ["Width"] = 152.0, ["Height"] = 95.0, ["MaxX"] = 30400, ["MaxY"] = 19000 },
                    ["Pen"] = new JObject { ["MaxPressure"] = 8192, ["ButtonCount"] = 2 },
                    ["AuxiliaryButtons"] = new JObject { ["ButtonCount"] = 8 },
                    ["Wheels"] = new JArray { new JObject() },
                },
            },
            ["Identifiers"] = new JArray { new JObject { ["VendorID"] = 0x056A, ["ProductID"] = 0x0357 } },
        },
    };

    [Fact]
    public void From_ParsesTheDeclaredSpecs()
    {
        var a = TabletAboutInfo.From(Tablets("Wacom Intuos Pro M"), "Wacom Intuos Pro M");

        Assert.NotNull(a);
        Assert.Equal(152f, a!.WidthMm, 3);
        Assert.Equal(95f, a.HeightMm, 3);
        Assert.Equal(8192u, a.MaxPressure);
        Assert.Equal(2u, a.PenButtons);
        Assert.Equal(8u, a.ExpressKeys);
        Assert.Equal(1, a.WheelCount);
        // Resolution = MaxX / width = 30400 / 152 = 200 LP/mm ⇒ ×25.4 = 5080 LPI.
        Assert.Equal(200, a.LpMm);
        Assert.Equal(5080, a.Lpi);
        Assert.Equal(0x056A, a.VendorId);
        Assert.Equal(0x0357, a.ProductId);
    }

    [Fact]
    public void From_MatchesNameCaseInsensitively()
    {
        Assert.NotNull(TabletAboutInfo.From(Tablets("PTK-670"), "ptk-670"));
    }

    [Fact]
    public void From_NullWhenTabletAbsentOrNoData()
    {
        Assert.Null(TabletAboutInfo.From(Tablets("A"), "Not Connected"));
        Assert.Null(TabletAboutInfo.From(null, "A"));
    }

    [Fact]
    public void Parse_OmitsAbsentOptionalSpecs()
    {
        // A minimal config: only a digitizer, no pen/aux/wheels/identifiers.
        var arr = new JArray
        {
            new JObject
            {
                ["Properties"] = new JObject
                {
                    ["Name"] = "Bare",
                    ["Specifications"] = new JObject
                    {
                        ["Digitizer"] = new JObject { ["Width"] = 100.0, ["Height"] = 50.0 },
                    },
                },
            },
        };
        var a = TabletAboutInfo.From(arr, "Bare");

        Assert.NotNull(a);
        Assert.Null(a!.MaxPressure);
        Assert.Null(a.ExpressKeys);
        Assert.Null(a.LpMm);         // no MaxX → not computable
        Assert.Null(a.Lpi);
        Assert.Equal(0, a.WheelCount);
        Assert.False(a.HasTouch);
        Assert.Null(a.VendorId);
    }
}
