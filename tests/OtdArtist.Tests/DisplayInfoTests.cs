using OtdArtist.Domain;
using Xunit;

namespace OtdArtist.Tests;

public class DisplayInfoTests
{
    private static DisplayInfo Display(int hz) =>
        new(Number: 1, Name: "Dell", Width: 2560, Height: 1440, X: 0, Y: 0, IsPrimary: true, RefreshHz: hz);

    [Fact]
    public void KnownRefreshRate_IsFormattedAndAppended()
    {
        var d = Display(144);
        Assert.True(d.HasRefreshRate);
        Assert.Equal("144 Hz", d.RefreshRateText);
        Assert.Equal("2560×1440 · 144 Hz", d.ResolutionWithRefresh);
    }

    [Theory]
    [InlineData(0)]   // Windows reports 0 for "default/unknown"
    [InlineData(1)]   // ...and sometimes 1
    public void UnknownRefreshRate_IsHidden(int hz)
    {
        var d = Display(hz);
        Assert.False(d.HasRefreshRate);
        Assert.Equal("", d.RefreshRateText);
        Assert.Equal("2560×1440", d.ResolutionWithRefresh);
    }

    [Fact]
    public void DefaultRefreshHz_IsZero_WhenOmitted()
    {
        var d = new DisplayInfo(1, "", 1920, 1080, 0, 0, IsPrimary: false);
        Assert.False(d.HasRefreshRate);
    }
}
