namespace OpenTabletArtist.Domain;

/// <summary>
/// Pure math/formatting for the diagnostics debug-report stream, extracted from
/// <c>MainViewModel.OnDeviceReport</c> (a high-frequency handler that otherwise mixes
/// JToken parsing, UI dispatch, and arithmetic). Keeping these pure makes the report-rate,
/// tilt, pressure, and raw-byte calculations unit-testable.
/// </summary>
public static class DiagnosticsMath
{
    /// <summary>Smoothing factor for the report-period exponential moving average.</summary>
    public const double ReportRateSmoothing = 0.05;

    /// <summary>
    /// Updated EMA of the report period in ms. A <paramref name="currentEma"/> of 0 seeds
    /// the average with <paramref name="deltaMs"/> (first sample).
    /// </summary>
    public static double UpdateReportPeriodEma(double currentEma, double deltaMs)
        => currentEma == 0 ? deltaMs : currentEma + (deltaMs - currentEma) * ReportRateSmoothing;

    /// <summary>Report rate in Hz (rounded) from a period in ms; 0 if the period is not positive.</summary>
    public static double ReportRateHz(double periodMs)
        => periodMs > 0 ? Math.Round(1000.0 / periodMs) : 0;

    /// <summary>Tilt azimuth in degrees, normalized to [0, 360).</summary>
    public static double TiltAzimuthDegrees(double tiltX, double tiltY)
    {
        var deg = Math.Atan2(tiltX, tiltY) * (180.0 / Math.PI);
        return deg < 0 ? deg + 360 : deg;
    }

    /// <summary>Tilt altitude in degrees (90° = perpendicular to the surface).</summary>
    public static double TiltAltitudeDegrees(double tiltX, double tiltY)
        => 90.0 - Math.Sqrt(tiltX * tiltX + tiltY * tiltY);

    /// <summary>Pressure as a percentage of max; 0 if <paramref name="maxPressure"/> is not positive.</summary>
    public static double PressurePercent(double pressure, double maxPressure)
        => maxPressure > 0 ? (pressure / maxPressure) * 100.0 : 0;

    /// <summary>Formats base64 report bytes as space-separated hex ("01 02 03"), or returns the input on failure.</summary>
    public static string FormatRawHex(string base64)
    {
        try
        {
            return BitConverter.ToString(Convert.FromBase64String(base64)).Replace('-', ' ');
        }
        catch
        {
            return base64;
        }
    }
}
