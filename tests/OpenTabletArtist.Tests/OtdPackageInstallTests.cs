using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The RPM-query classifier is split out from the process spawn so it's testable without rpm. Exit 0 with a
/// version = installed; anything else (rpm exits 1 and prints "package … is not installed") = not installed.
/// </summary>
public class OtdPackageInstallTests
{
    [Fact]
    public void Installed_WhenExitZeroWithVersion()
    {
        var r = OtdPackageInstall.Interpret(0, "0.6.7-1\n");
        Assert.True(r.Installed);
        Assert.Equal("0.6.7-1", r.Version); // trimmed
    }

    [Fact]
    public void NotInstalled_WhenExitNonZero()
    {
        var r = OtdPackageInstall.Interpret(1, "package opentabletdriver is not installed\n");
        Assert.False(r.Installed);
        Assert.Null(r.Version);
    }

    [Fact]
    public void NotInstalled_WhenExitZeroButEmptyOutput()
    {
        // Defensive: a zero exit with no version string shouldn't be reported as installed.
        var r = OtdPackageInstall.Interpret(0, "   \n");
        Assert.False(r.Installed);
        Assert.Null(r.Version);
    }
}
