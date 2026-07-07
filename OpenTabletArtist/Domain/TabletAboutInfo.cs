using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// User-facing tablet facts parsed from the daemon's <c>TabletReference</c> JSON (the same token
/// <see cref="Services.IDeviceData.Tablets"/> exposes), for the tablet ABOUT tab. Every spec is optional —
/// a field is null/zero when the config doesn't declare it — so the tab can show only what's known.
/// Pure and unit-tested; see <see cref="From"/>.
/// </summary>
public sealed record TabletAboutInfo
{
    public string Name { get; init; } = "";
    public float WidthMm { get; init; }
    public float HeightMm { get; init; }
    /// <summary>Reported digitizer resolution in lines per millimetre (MaxX over the width), when computable.</summary>
    public int? LpMm { get; init; }
    /// <summary>The same resolution in lines per inch (LP/mm × 25.4), when computable.</summary>
    public int? Lpi { get; init; }
    public uint? MaxPressure { get; init; }
    public uint? PenButtons { get; init; }
    public uint? ExpressKeys { get; init; }
    public uint? MouseButtons { get; init; }
    public int WheelCount { get; init; }
    public int StripCount { get; init; }
    public bool HasTouch { get; init; }
    public int? VendorId { get; init; }
    public int? ProductId { get; init; }

    /// <summary>Find <paramref name="tabletName"/> in the daemon's tablets array and parse its facts, or
    /// null if the array is empty / the tablet isn't currently reported (specs only exist while detected).</summary>
    public static TabletAboutInfo? From(JToken? tablets, string tabletName)
    {
        if (tablets is not JArray arr) return null;
        foreach (var t in arr)
        {
            var props = t["Properties"] ?? t;
            if (string.Equals(props["Name"]?.ToString(), tabletName, StringComparison.OrdinalIgnoreCase))
                return Parse(t);
        }
        return null;
    }

    internal static TabletAboutInfo Parse(JToken t)
    {
        var props = t["Properties"] ?? t;
        var specs = props["Specifications"];
        var digi = specs?["Digitizer"];
        var pen = specs?["Pen"];

        float w = digi?["Width"]?.Value<float>() ?? 0;
        float h = digi?["Height"]?.Value<float>() ?? 0;
        float maxX = digi?["MaxX"]?.Value<float>() ?? 0;

        // The active DeviceIdentifier carries the live VID/PID; fall back to the config's digitizer id.
        var id = (t["Identifiers"] as JArray)?.FirstOrDefault()
                 ?? (props["DigitizerIdentifiers"] as JArray)?.FirstOrDefault();

        uint? pressure = pen?["MaxPressure"]?.Value<uint>();

        return new TabletAboutInfo
        {
            Name = props["Name"]?.ToString() ?? "",
            WidthMm = w,
            HeightMm = h,
            LpMm = w > 0 && maxX > 0 ? (int)Math.Round(maxX / w) : null,
            Lpi = w > 0 && maxX > 0 ? (int)Math.Round(maxX * 25.4 / w) : null,
            MaxPressure = pressure is > 0 ? pressure : null,
            PenButtons = pen?["ButtonCount"]?.Value<uint>(),
            ExpressKeys = specs?["AuxiliaryButtons"]?["ButtonCount"]?.Value<uint>(),
            MouseButtons = specs?["MouseButtons"]?["ButtonCount"]?.Value<uint>(),
            WheelCount = (specs?["Wheels"] as JArray)?.Count ?? 0,
            StripCount = (specs?["Strips"] as JArray)?.Count ?? 0,
            HasTouch = specs?["Touch"] is { Type: not JTokenType.Null },
            VendorId = id?["VendorID"]?.Value<int>(),
            ProductId = id?["ProductID"]?.Value<int>(),
        };
    }
}
