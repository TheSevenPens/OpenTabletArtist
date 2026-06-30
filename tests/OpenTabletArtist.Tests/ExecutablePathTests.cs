using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ExecutablePathTests
{
    [Fact]
    public void IdenticalPaths_AreSame()
    {
        Assert.True(ExecutablePath.SameFile(@"C:\a\b\daemon.exe", @"C:\a\b\daemon.exe"));
    }

    [Fact]
    public void CaseDifferentPaths_AreSame()
    {
        Assert.True(ExecutablePath.SameFile(@"C:\A\B\Daemon.exe", @"c:\a\b\daemon.exe"));
    }

    [Fact]
    public void NormalizedRelativeSegments_AreSame()
    {
        Assert.True(ExecutablePath.SameFile(@"C:\a\x\..\b\daemon.exe", @"C:\a\b\daemon.exe"));
    }

    [Fact]
    public void DifferentPaths_AreNotSame()
    {
        Assert.False(ExecutablePath.SameFile(@"C:\a\b\daemon.exe", @"C:\other\daemon.exe"));
    }

    [Theory]
    [InlineData(null, @"C:\a\daemon.exe")]
    [InlineData(@"C:\a\daemon.exe", null)]
    [InlineData("", @"C:\a\daemon.exe")]
    [InlineData(null, null)]
    public void NullOrEmpty_IsNotSame(string? a, string? b)
    {
        Assert.False(ExecutablePath.SameFile(a, b));
    }
}
