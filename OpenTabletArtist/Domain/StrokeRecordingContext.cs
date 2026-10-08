using System;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>What a recording needs to know about the tablet and driver that the pen reports do not carry.</summary>
public sealed record StrokeRecordingContext(string Tablet, string Driver, int FullScalePressure, TabletSpace Space)
{
    /// <summary>
    /// Reads the named tablet's digitizer and pen specifications out of the daemon's tablets array. Null when
    /// the tablet is not currently reported, or its specifications are incomplete: a recording of a tablet whose
    /// units cannot be stated would be a file nobody could interpret, so there is none.
    /// </summary>
    public static StrokeRecordingContext? From(JToken? tablets, string? tabletName, string? daemonVersion)
    {
        if (tablets is not JArray all || string.IsNullOrWhiteSpace(tabletName)) return null;

        foreach (var tablet in all)
        {
            var props = tablet["Properties"] ?? tablet;
            if (!string.Equals(props["Name"]?.ToString(), tabletName, StringComparison.OrdinalIgnoreCase)) continue;

            var specs = props["Specifications"];
            var digitizer = specs?["Digitizer"];
            var maxX = digitizer?["MaxX"]?.Value<double>() ?? 0;
            var maxY = digitizer?["MaxY"]?.Value<double>() ?? 0;
            var widthMm = digitizer?["Width"]?.Value<double>() ?? 0;
            var heightMm = digitizer?["Height"]?.Value<double>() ?? 0;
            var maxPressure = specs?["Pen"]?["MaxPressure"]?.Value<double>() ?? 0;

            if (maxX <= 0 || maxY <= 0 || widthMm <= 0 || heightMm <= 0 || maxPressure <= 0) return null;

            var driver = string.IsNullOrWhiteSpace(daemonVersion)
                ? "OpenTabletDriver"
                : $"OpenTabletDriver {daemonVersion.Trim()}";

            return new StrokeRecordingContext(
                props["Name"]!.ToString(), driver, (int)maxPressure, new TabletSpace(maxX, maxY, widthMm, heightMm));
        }

        return null;
    }
}
