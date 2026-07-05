using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class WinInkAutoOptOutTests
{
    [Fact]
    public void OptOut_AndClear_RoundTrip()
    {
        const string tablet = "Test Tablet OptOut";
        try
        {
            WinInkAutoOptOut.OptOut(tablet);
            Assert.True(WinInkAutoOptOut.IsOptedOut(tablet));
            WinInkAutoOptOut.Clear(tablet);
            Assert.False(WinInkAutoOptOut.IsOptedOut(tablet));
        }
        finally
        {
            WinInkAutoOptOut.Clear(tablet);
        }
    }
}
