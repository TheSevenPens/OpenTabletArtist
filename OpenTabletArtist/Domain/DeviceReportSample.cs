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
            HoverDistance: hover);
        return true;
    }

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
