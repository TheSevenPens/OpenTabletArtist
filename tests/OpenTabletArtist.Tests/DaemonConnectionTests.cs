using System;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Pure helpers behind the DAEMON CONNECTION card: version-release matching (the daemon binary reports
/// "0.6.7" while OTA's assembly version is "0.6.7.0") and the compact uptime formatter.
/// </summary>
public class DaemonConnectionTests
{
    [Theory]
    [InlineData("0.6.7.0", "0.6.7")]      // assembly (4-part) vs daemon ProductVersion (3-part)
    [InlineData("0.6.7", "0.6.7")]        // identical
    [InlineData("0.6.7.0", "0.6.7.9")]    // 4th component ignored
    [InlineData("0.6.7+abc", "0.6.7")]    // trailing suffix ignored
    public void SameRelease_True_ForMatchingReleases(string built, string connected)
    {
        Assert.True(DaemonVersion.SameRelease(built, connected));
    }

    [Theory]
    [InlineData("0.6.7", "0.6.8")]        // patch differs
    [InlineData("0.6.7", "0.7.0")]        // minor differs
    [InlineData("0.6.7", "")]             // no connected version
    [InlineData("", "0.6.7")]             // no built version
    [InlineData("0.6.7", "unknown")]      // unparseable
    public void SameRelease_False_ForDifferingOrUnparseable(string built, string connected)
    {
        Assert.False(DaemonVersion.SameRelease(built, connected));
    }

    [Theory]
    [InlineData(0, 0, 8, "8s")]
    [InlineData(0, 3, 12, "3m 12s")]
    [InlineData(1, 4, 5, "1h 04m")]
    [InlineData(25, 0, 0, "25h 00m")]
    public void Compact_IsShort(int hours, int minutes, int seconds, string expected)
    {
        var d = new TimeSpan(hours, minutes, seconds);
        Assert.Equal(expected, DurationFormat.Compact(d));
    }

    [Fact]
    public void Compact_ClampsNegativeToZero()
    {
        Assert.Equal("0s", DurationFormat.Compact(TimeSpan.FromSeconds(-5)));
    }
}
