using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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

    // --- The OTD version must not come from a linked assembly ---------------------------

    /// <summary>
    /// <see cref="OtdRelease.Version"/> is the four-part shape the OTD assemblies report, so the
    /// comparisons built on it (plugin manifests, daemon version) are unchanged by the switch away from
    /// reading assembly metadata.
    /// </summary>
    [Fact]
    public void Version_IsTheFourPartFormOfThePinnedRelease()
    {
        Assert.Equal(new System.Version(OtdRelease.AssetVersion + ".0"), OtdRelease.Version);
        Assert.Equal(OtdRelease.Tag, "v" + OtdRelease.AssetVersion);
    }

    /// <summary>
    /// The guard for a bug that dev builds cannot show.
    ///
    /// Publishing passes <c>-p:Version=&lt;OTA version&gt;</c>, and MSBuild applies it to every project in
    /// the graph — including the OTD submodule projects OTA references. So in a <b>release</b> build,
    /// <c>typeof(AppInfo).Assembly.GetName().Version</c> returns OTA's version rather than OTD's, and
    /// everything derived from it is wrong: Windows Ink plugin compatibility finds no matching manifest,
    /// the health card reports the wrong "built against", and every connected daemon looks mismatched.
    /// Dev builds pass no version, keep 0.6.7.0, and look perfect.
    ///
    /// A behavioural test cannot catch that — it would have to run against a published build with a
    /// version stamp. So this reads the source instead: nothing in the app may ask a linked OTD type's
    /// assembly for its version. Use <see cref="OtdRelease.Version"/>.
    /// </summary>
    [Fact]
    public void NoProductionCodeReadsTheOtdVersionFromALinkedAssembly()
    {
        var appDir = Path.Combine(RepoRoot(), "OpenTabletArtist");
        Assert.True(Directory.Exists(appDir), $"Couldn't find the app sources (looked at {appDir}).");

        // The OTD types whose assemblies carry the stamped version. Asking any of them for a version is
        // the mistake; reading them for anything else is fine.
        var offenders = Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => (File: f, Text: StripComments(File.ReadAllText(f))))
            .SelectMany(f => Regex.Matches(f.Text,
                    @"typeof\(\s*(?:OpenTabletDriver[\w.]*\.)?(AppInfo|Settings|PluginMetadata)\s*\)\s*\.Assembly\s*\.GetName\(\)\s*\.Version")
                .Select(m => $"{Path.GetFileName(f.File)}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These read the OTD version from an assembly that gets stamped with OTA's version at publish "
            + "time, so they are wrong in every release build. Use OtdRelease.Version instead:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>Comments are prose, not code — and the doc comment on <c>OtdRelease.Version</c> quotes the
    /// very pattern this forbids, in order to explain it. Scanning raw text would flag the explanation.</summary>
    private static string StripComments(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//[^
]*", "");

    /// <summary>
    /// Derived from this file's own compile-time path: the test binaries build to a redirected output
    /// directory with no repository above it.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
