using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Converts an OTD daemon <c>DeviceReport</c> JObject into a normalized <see cref="PenSample"/>.
/// Pure (JSON in, struct out) so the parsing is unit-testable. Mirrors the JSON paths the
/// Diagnostics page reads. Returns false when the report lacks the position/spec data needed to
/// normalize (e.g. a non-tablet report).
/// </summary>
public static class DeviceReportSample
{
    public static bool TryParse(JObject data, out PenSample sample)
    {
        sample = default;

        var digitizer = data["Tablet"]?["Properties"]?["Specifications"]?["Digitizer"];
        var maxX = digitizer?["MaxX"]?.Value<double>() ?? 0;
        var maxY = digitizer?["MaxY"]?.Value<double>() ?? 0;
        if (maxX <= 0 || maxY <= 0) return false;

        var report = data["Data"];
        var pos = report?["Position"];
        if (pos == null) return false;

        var x = pos["X"]?.Value<double>() ?? 0;
        var y = pos["Y"]?.Value<double>() ?? 0;

        var maxPressure = data["Tablet"]?["Properties"]?["Specifications"]?["Pen"]?["MaxPressure"]?.Value<double>() ?? 0;
        var rawPressure = report?["Pressure"]?.Value<double>() ?? 0;
        var pressure = maxPressure > 0 ? rawPressure / maxPressure : 0;

        var tilt = report?["Tilt"];
        var tiltX = tilt?["X"]?.Value<double>() ?? 0;
        var tiltY = tilt?["Y"]?.Value<double>() ?? 0;

        // Hover height (0–255) is only on proximity-carrying reports; null when absent.
        int? hover = report?["HoverDistance"] is { } h ? h.Value<int>() : null;

        sample = new PenSample(
            X: Clamp01(x / maxX),
            Y: Clamp01(y / maxY),
            RawX: x,
            RawY: y,
            Pressure: Clamp01(pressure),
            TiltX: tiltX,
            TiltY: tiltY,
            Twist: 0, // OTD device reports don't carry barrel twist
            IsDown: pressure > 0,
            HoverDistance: hover,
            RawPressure: rawPressure,
            HasTilt: tilt is { Type: not JTokenType.Null });
        return true;
    }

    /// <summary>Who sent a report and what its digitizer and pen specifications say, so a recording can tell its own
    /// tablet's reports from anyone else's. Null name / zero values when the report doesn't say.</summary>
    public readonly record struct DeviceIdentity(string? Name, double MaxX, double MaxY, double MaxPressure);

    public static DeviceIdentity Identity(JObject data) => new(
        data.SelectToken("Tablet.Properties.Name")?.ToString(),
        Number(data.SelectToken("Tablet.Properties.Specifications.Digitizer.MaxX")),
        Number(data.SelectToken("Tablet.Properties.Specifications.Digitizer.MaxY")),
        Number(data.SelectToken("Tablet.Properties.Specifications.Pen.MaxPressure")));

    /// <summary>
    /// Whether a report is a genuine pen measurement: a position, a pressure, and tilt and hover distance that are
    /// either absent or complete. <see cref="TryParse"/> is deliberately forgiving (a missing pressure reads as 0, so
    /// a mouse-type report that carries a position still moves the readouts), and that is wrong for a recording: it
    /// would write a hovering pen with a pressure nobody measured. A recording admits only what passes this.
    /// </summary>
    public static bool IsPenMeasurement(JObject data)
    {
        if (data["Data"] is not JObject report) return false;
        if (report["Position"] is not JObject pos || !IsNumber(pos["X"]) || !IsNumber(pos["Y"])) return false;
        if (!IsNumber(report["Pressure"])) return false;

        var tiltOk = report["Tilt"] switch
        {
            null or { Type: JTokenType.Null } => true,
            JObject tilt => IsNumber(tilt["X"]) && IsNumber(tilt["Y"]),
            _ => false,
        };
        if (!tiltOk) return false;

        return report["HoverDistance"] is null or { Type: JTokenType.Null } || IsNumber(report["HoverDistance"]);
    }

    private static bool IsNumber(JToken? t) => t is { Type: JTokenType.Integer or JTokenType.Float };

    private static double Number(JToken? t) => IsNumber(t) ? t!.Value<double>() : 0;

    /// <summary>Pulls the auxiliary-button (express key) states out of an OTD <c>DeviceReport</c>.
    /// Only aux reports carry <c>Data.AuxButtons</c>; returns false for pen-only reports so callers
    /// can ignore them and leave the last-known press state untouched.</summary>
    public static bool TryParseAuxButtons(JObject data, out bool[] auxButtons)
    {
        auxButtons = Array.Empty<bool>();
        if (data["Data"]?["AuxButtons"] is not JArray arr) return false;
        auxButtons = arr.Select(t => t.Value<bool>()).ToArray();
        return true;
    }

    /// <summary>Wheel-button state per wheel (OTD's <c>IWheelButtonReport.WheelButtons</c> is a jagged
    /// bool[][]: outer = wheel, inner = that wheel's buttons). Only wheel-button reports carry it.</summary>
    public static bool TryParseWheelButtons(JObject data, out bool[][] wheelButtons)
    {
        wheelButtons = Array.Empty<bool[]>();
        if (data["Data"]?["WheelButtons"] is not JArray arr) return false;
        wheelButtons = arr.Select(w =>
            w is JArray inner ? inner.Select(t => t.Value<bool>()).ToArray() : Array.Empty<bool>()).ToArray();
        return true;
    }

    /// <summary>Absolute-wheel positions per wheel (touch rings). Each entry is a 0..max reading or null
    /// (no touch / no change). From <c>IAbsoluteAnalogReport.AnalogPositions</c>.</summary>
    public static bool TryParseWheelPositions(JObject data, out uint?[] positions)
    {
        positions = Array.Empty<uint?>();
        if (data["Data"]?["AnalogPositions"] is not JArray arr) return false;
        positions = arr.Select(t => t.Type == JTokenType.Null ? (uint?)null : t.Value<uint>()).ToArray();
        return true;
    }

    /// <summary>Relative-wheel step deltas per wheel (scroll-wheel style). From
    /// <c>IRelativeAnalogReport.AnalogDeltas</c> — sign gives direction, 0 means no movement.</summary>
    public static bool TryParseWheelDeltas(JObject data, out int[] deltas)
    {
        deltas = Array.Empty<int>();
        if (data["Data"]?["AnalogDeltas"] is not JArray arr) return false;
        deltas = arr.Select(t => t.Value<int>()).ToArray();
        return true;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
