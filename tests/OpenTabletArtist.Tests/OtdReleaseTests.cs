using System.IO;
using System.Linq;
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

    [Fact]
    public void InstallsIntoTheSystemApplicationsFolder()
    {
        // GetFullPath on both sides: a drive-less "/Applications" is not a real path off macOS, and the
        // literal would only match there.
        Assert.Equal(Path.GetFullPath(Path.Combine("/", "Applications")), OtdRelease.InstallDirectory);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("/", "Applications", "OpenTabletDriver.app")),
            OtdRelease.InstalledBundlePath);
    }

    // The install has to land where the ladder looks *first*. Anywhere lower and OTA's own install could
    // be shadowed later by whatever turned up in a higher tier — the reason /Applications was chosen over
    // a per-user location.
    [Fact]
    public void TheInstalledDaemonIsTheLaddersFirstInstalledCandidate()
    {
        var installed = DaemonExePaths.InstalledMacPaths(Path.Combine("/", "Users", "someone")).ToList();

        Assert.Equal(OtdRelease.InstalledDaemonPath, installed.First());
    }
}
