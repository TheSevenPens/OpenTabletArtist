using OpenTabletDriver.Plugin;

namespace OpenTabletArtist.Domain;

/// <summary>Level + free-text predicates for the Log page filter (pure, unit-tested).</summary>
public static class LogFilter
{
    public static bool Matches(LogLevel level, LogLevel minLevel, string message, string? searchText)
    {
        if (level < minLevel) return false;
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return message.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
