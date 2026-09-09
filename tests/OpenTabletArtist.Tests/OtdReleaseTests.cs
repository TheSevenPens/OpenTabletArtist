using System.IO;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The assisted install is pinned rather than "latest" (docs/design/official-otd-release.md), so these
/// guard the two things that would break quietly: the pin drifting away from the submodule, and the
/// asset/URL naming not matching what OpenTabletDriver actually publishes.
/// </summary>
public class OtdReleaseTests
{
    // The tag is "v0.6.7" while the assemblies inside report "0.6.7.0". If these ever disagree about the
    // release, an assisted install would hand the user a driver that immediately raises
    // otd.versionMismatch — the exact outcome pinning exists to prevent.
    [Fact]
    public void ThePinnedReleaseIsTheOneOtaIsBuiltAgainst()
    {
        Assert.True(DaemonVersion.SameRelease(OtdRelease.AssetVersion, "0.6.7.0"));
        Assert.Equal($"v{OtdRelease.AssetVersion}", OtdRelease.Tag);
    }

    // Matches the real published artifact: OpenTabletDriver-0.6.7_osx-x64.tar.gz.
    [Fact]
    public void TheMacAssetIsNamedTheWayOtdPublishesIt()
    {
        Assert.Equal($"OpenTabletDriver-{OtdRelease.AssetVersion}_osx-x64.tar.gz", OtdRelease.MacAssetName);
    }

    [Fact]
    public void TheDownloadUrlPointsAtThePinnedTagOnOtdsOwnRepo()
    {
        var url = OtdRelease.MacDownloadUrl;

        Assert.StartsWith("https://github.com/OpenTabletDriver/OpenTabletDriver/releases/download/", url);
        Assert.Contains($"/{OtdRelease.Tag}/", url);
        Assert.EndsWith(OtdRelease.MacAssetName, url);
    }

    // ~/Applications, not /Applications: no authorization needed, and the search ladder already looks
    // there, so a successful install is found without storing a path.
    [Fact]
    public void InstallsIntoTheUsersOwnApplicationsFolder()
    {
        var home = Path.Combine("/", "Users", "someone");

        Assert.Equal(Path.Combine(home, "Applications"), OtdRelease.InstallDirectory(home));
        Assert.Equal(
            Path.Combine(home, "Applications", "OpenTabletDriver.app"),
            OtdRelease.InstalledBundlePath(home));
    }

    // The install location has to be a place the ladder searches, or an installed driver would sit there
    // undiscovered.
    [Fact]
    public void TheInstalledDaemonIsOnTheSearchLadder()
    {
        var home = Path.Combine("/", "Users", "someone");

        Assert.Contains(
            OtdRelease.InstalledDaemonPath(home),
            DaemonExePaths.InstalledMacPaths(home));
    }
}
