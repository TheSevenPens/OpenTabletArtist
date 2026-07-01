using OpenTabletArtist.Domain;
using OpenTabletDriver.Plugin;
using Xunit;

namespace OpenTabletArtist.Tests;

public class LogFilterTests
{
    [Theory]
    [InlineData(LogLevel.Info, LogLevel.Info, "tablet connected", null, true)]
    [InlineData(LogLevel.Debug, LogLevel.Info, "tablet connected", null, false)]
    [InlineData(LogLevel.Info, LogLevel.Info, "tablet connected", "TABLET", true)]
    [InlineData(LogLevel.Info, LogLevel.Info, "tablet connected", "missing", false)]
    [InlineData(LogLevel.Info, LogLevel.Info, "Tablet Connected", "tablet", true)]
    [InlineData(LogLevel.Info, LogLevel.Info, "hello", "  HEL  ", true)]
    [InlineData(LogLevel.Info, LogLevel.Info, "hello", "", true)]
    [InlineData(LogLevel.Info, LogLevel.Info, "hello", "   ", true)]
    public void Matches_LevelAndSearch(LogLevel level, LogLevel min, string message, string? search, bool expected)
        => Assert.Equal(expected, LogFilter.Matches(level, min, message, search));
}
