using Newtonsoft.Json.Linq;

namespace OtdWindowsHelper.Domain;

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

        sample = new PenSample(
            X: Clamp01(x / maxX),
            Y: Clamp01(y / maxY),
            RawX: x,
            RawY: y,
            Pressure: Clamp01(pressure),
            TiltX: tiltX,
            TiltY: tiltY,
            Twist: 0, // OTD device reports don't carry barrel twist
            IsDown: pressure > 0);
        return true;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
