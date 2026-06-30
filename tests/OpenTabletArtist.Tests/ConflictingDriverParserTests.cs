using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ConflictingDriverParserTests
{
    private static LogMessage Msg(string group, string text) =>
        new() { Group = group, Message = text };

    [Fact]
    public void Parses_Name_Impact_Processes_AndWiki()
    {
        var d = ConflictingDriverParser.TryParse(Msg("Detect",
            "'Wacom Tablet' driver is detected. It will block detection of tablets. " +
            "Processes found: [Wacom_Tablet.exe, Wacom_TabletUser.exe]. " +
            "If any problems arise, visit 'https://opentabletdriver.net/Wiki/FAQ/Windows'."));

        Assert.NotNull(d);
        Assert.Equal("Wacom Tablet", d!.Name);
        Assert.True(d.Blocking);
        Assert.Equal("Blocks OpenTabletDriver from detecting tablets", d.Impact);
        Assert.Equal("Wacom_Tablet.exe, Wacom_TabletUser.exe", d.Processes);
        Assert.True(d.HasProcesses);
        Assert.Equal("https://opentabletdriver.net/Wiki/FAQ/Windows", d.WikiUrl);
        Assert.True(d.HasWiki);
    }

    [Fact]
    public void Parses_FlakyAndUncertain()
    {
        var flaky = ConflictingDriverParser.TryParse(Msg("Detect",
            "'Huion Tablet' driver is detected. It will cause flaky support to tablets."));
        Assert.True(flaky!.Flaky);
        Assert.Equal("Can cause flaky tablet support", flaky.Impact);

        var uncertain = ConflictingDriverParser.TryParse(Msg("Detect",
            "'Something' driver is detected. It may be a false positive."));
        Assert.True(uncertain!.Uncertain);
        Assert.Equal("Possible conflict — may be a false positive", uncertain.Impact);
    }

    [Theory]
    [InlineData("Detect", "No known tablets added, skipping detect")] // not a driver-detected line
    [InlineData("Driver", "'Wacom' driver is detected.")]            // wrong group
    [InlineData("Detect", "driver is detected but no quoted name")]   // no name
    public void ReturnsNull_ForNonDetections(string group, string text)
    {
        Assert.Null(ConflictingDriverParser.TryParse(Msg(group, text)));
    }

    [Fact]
    public void ReturnsNull_ForNullOrEmpty()
    {
        Assert.Null(ConflictingDriverParser.TryParse(null));
        Assert.Null(ConflictingDriverParser.TryParse(Msg("Detect", "")));
    }

    // Our own app trips OTD's "Pentablet" heuristic ("OpenTabletArtist" contains "penTablet").
    [Fact]
    public void IsSelfMatch_WhenOnlyProcessIsOpenTabletArtist()
    {
        var self = ConflictingDriverParser.TryParse(Msg("Detect",
            "'XP-Pen' driver is detected. Processes found: [OpenTabletArtist: C:\\x\\OpenTabletArtist.exe]."));
        Assert.True(self!.IsSelfMatch);

        // A real conflict (a genuine XP-Pen process present) is not a self-match.
        var real = ConflictingDriverParser.TryParse(Msg("Detect",
            "'XP-Pen' driver is detected. Processes found: [PentabletService: C:\\xp\\PentabletService.exe]."));
        Assert.False(real!.IsSelfMatch);
    }
}
