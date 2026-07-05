using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class StartupServiceTests
{
    [Fact]
    public void FormatRunValue_IncludesBackgroundFlag()
    {
        var value = StartupService.FormatRunValue(@"C:\Apps\OpenTabletArtist.exe");
        Assert.Contains(StartupService.BackgroundArgument, value);
        Assert.Contains("OpenTabletArtist.exe", value);
    }

    [Fact]
    public void TryParseRunValue_ParsesQuotedExeAndFlag()
    {
        Assert.True(StartupService.TryParseRunValue(
            "\"C:\\Apps\\OpenTabletArtist.exe\" --background", out var exe, out var bg));
        Assert.Equal(@"C:\Apps\OpenTabletArtist.exe", exe);
        Assert.True(bg);
    }

    [Fact]
    public void TryParseRunValue_ParsesLegacyUnquotedEntry()
    {
        Assert.True(StartupService.TryParseRunValue(
            "C:\\Apps\\OpenTabletArtist.exe", out var exe, out var bg));
        Assert.Equal(@"C:\Apps\OpenTabletArtist.exe", exe);
        Assert.False(bg);
    }

    [Fact]
    public void RunValueMatchesCurrent_IsFalseForStalePath()
    {
        var current = Environment.ProcessPath;
        Assert.False(string.IsNullOrEmpty(current));
        Assert.False(StartupService.RunValueMatchesCurrent($"\"C:\\Old\\OpenTabletArtist.exe\" {StartupService.BackgroundArgument}"));
        Assert.True(StartupService.RunValueMatchesCurrent(StartupService.FormatRunValue(current!)));
    }
}
