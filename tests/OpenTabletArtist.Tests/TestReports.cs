using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Tests;

/// <summary>Builds the daemon's <c>DeviceReport</c> JSON, the shape <see cref="DeviceReportSample"/> parses.</summary>
internal static class TestReports
{
    public const string Tablet = "Wacom PTK-470";

    public static JObject Json(
        PenSample s,
        string tablet = Tablet,
        double maxX = 15200,
        double maxY = 9500,
        double maxPressure = 1023,
        bool tilt = true,
        bool pressure = true)
    {
        var data = new JObject
        {
            ["Position"] = new JObject { ["X"] = s.RawX, ["Y"] = s.RawY },
        };

        if (pressure) data["Pressure"] = s.RawPressure;
        if (tilt) data["Tilt"] = new JObject { ["X"] = s.TiltX, ["Y"] = s.TiltY };
        if (s.HoverDistance is { } h) data["HoverDistance"] = h;

        return new JObject
        {
            ["Tablet"] = new JObject
            {
                ["Properties"] = new JObject
                {
                    ["Name"] = tablet,
                    ["Specifications"] = new JObject
                    {
                        ["Digitizer"] = new JObject { ["Width"] = 152.0, ["Height"] = 95.0, ["MaxX"] = maxX, ["MaxY"] = maxY },
                        ["Pen"] = new JObject { ["MaxPressure"] = maxPressure },
                    },
                },
            },
            ["Data"] = data,
        };
    }
}

/// <summary>Reads numbers off the review dialog's results text, which has one line per place a report can go.</summary>
internal static class LedgerReader
{
    /// <summary>The number on the line that says <paramref name="what"/>.</summary>
    public static int Of(string ledger, string what)
    {
        var line = ledger.Split('\n').Single(l => l.Contains(what));
        return int.Parse(line.TrimStart().Split(' ')[0], System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.CurrentCulture);
    }
}
