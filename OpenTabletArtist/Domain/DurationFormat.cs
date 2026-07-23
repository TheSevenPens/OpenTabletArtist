using System;

namespace OpenTabletArtist.Domain;

/// <summary>Compact human formatting for elapsed durations (connection age, daemon uptime).</summary>
public static class DurationFormat
{
    /// <summary>Format an elapsed span compactly: "1h 04m", "3m 12s", "8s". Negative clamps to "0s".</summary>
    public static string Compact(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalHours >= 1) return $"{(int)d.TotalHours}h {d.Minutes:D2}m";
        if (d.TotalMinutes >= 1) return $"{d.Minutes}m {d.Seconds:D2}s";
        return $"{d.Seconds}s";
    }
}
