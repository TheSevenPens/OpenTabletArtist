using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class DaemonExePathsTests
{
    private static string Exe => DaemonExePaths.DaemonExeName;

    [Fact]
    public void BundledLocationIsCheckedFirst()
    {
        var baseDir = Path.Combine("C:", "app");
        var candidates = DaemonExePaths.Candidates(baseDir).ToList();

        Assert.Equal(
            Path.GetFullPath(Path.Combine(baseDir, "Daemon", Exe)),
            candidates[0]);
    }

    [Fact]
    public void IncludesDevBuildTreePaths()
    {
        var candidates = DaemonExePaths.Candidates(Path.Combine("C:", "repo", "OpenTabletArtist", "bin", "Debug", "net10.0")).ToList();

        // Bundled + Debug + Release dev candidates.
        Assert.Equal(3, candidates.Count);
        Assert.All(candidates, c => Assert.EndsWith(Exe, c));
        Assert.Contains(candidates, c => c.Contains(Path.Combine("bin", "Debug", "net8.0")));
        Assert.Contains(candidates, c => c.Contains(Path.Combine("bin", "Release", "net8.0")));
    }

    // --- The adoption ladder (docs/design/official-otd-release.md) ---

    [Fact]
    public void UserChosenPathOutranksEverything()
    {
        var chosen = Path.Combine("C:", "elsewhere", "OTD", Exe);
        var candidates = DaemonExePaths.Candidates(
            Path.Combine("C:", "app"),
            userPath: chosen,
            installed: [Path.Combine("C:", "installed", Exe)]).ToList();

        Assert.Equal(Path.GetFullPath(chosen), candidates[0]);
    }

    // The point of the tier: on macOS a granted, already-installed OTD must beat a freshly built daemon
    // that has never held an Input Monitoring grant.
    [Fact]
    public void InstalledOtdComesBeforeTheDevBuildTree()
    {
        var installed = Path.Combine("/", "Applications", "OpenTabletDriver.app", "Contents", "MacOS", "OpenTabletDriver.Daemon");
        var candidates = DaemonExePaths.Candidates(
            Path.Combine("C:", "repo", "OpenTabletArtist", "bin", "Debug", "net10.0"),
            installed: [installed]).ToList();

        var installedAt = candidates.FindIndex(c => c == Path.GetFullPath(installed));
        var devAt = candidates.FindIndex(c => c.Contains(Path.Combine("bin", "Debug", "net8.0")));

        Assert.True(installedAt >= 0, "the installed OTD should be a candidate");
        Assert.True(installedAt < devAt, "an installed OTD should outrank the dev build tree");
    }

    [Fact]
    public void BundledStillOutranksAnInstalledOtd()
    {
        var baseDir = Path.Combine("C:", "app");
        var candidates = DaemonExePaths.Candidates(
            baseDir,
            installed: [Path.Combine("C:", "installed", Exe)]).ToList();

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "Daemon", Exe)), candidates[0]);
    }

    [Fact]
    public void NoInstalledPathsLeavesTheOriginalOrderUnchanged()
    {
        var baseDir = Path.Combine("C:", "repo", "OpenTabletArtist", "bin", "Debug", "net10.0");

        Assert.Equal(
            DaemonExePaths.Candidates(baseDir).ToList(),
            DaemonExePaths.Candidates(baseDir, userPath: null, installed: null).ToList());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankUserPathIsIgnored(string? userPath)
    {
        var baseDir = Path.Combine("C:", "app");
        var candidates = DaemonExePaths.Candidates(baseDir, userPath).ToList();

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "Daemon", Exe)), candidates[0]);
    }

    // --- What the user can point at ---

    [Fact]
    public void UserPathAcceptsAnAppBundle()
    {
        var normalized = DaemonExePaths.NormalizeUserPath(Path.Combine("/", "Applications", "OpenTabletDriver.app"));

        Assert.NotNull(normalized);
        Assert.EndsWith(Path.Combine("Contents", "MacOS", "OpenTabletDriver.Daemon"), normalized);
    }

    [Fact]
    public void UserPathAcceptsTheExeItself()
    {
        var exe = Path.GetFullPath(Path.Combine("C:", "otd", Exe));

        Assert.Equal(exe, DaemonExePaths.NormalizeUserPath(exe));
    }

    [Fact]
    public void UserPathAcceptsAContainingDirectory()
    {
        var dir = Path.Combine("C:", "otd");

        Assert.Equal(
            Path.Combine(Path.GetFullPath(dir), Exe),
            DaemonExePaths.NormalizeUserPath(dir));
    }

    // --- macOS install locations ---

    [Fact]
    public void MacPathsCoverSystemAndUserApplications()
    {
        var paths = DaemonExePaths.InstalledMacPaths(Path.Combine("/", "Users", "someone")).ToList();

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.EndsWith(
            Path.Combine("OpenTabletDriver.app", "Contents", "MacOS", "OpenTabletDriver.Daemon"), p));
        Assert.StartsWith(Path.GetFullPath(Path.Combine("/", "Applications")), paths[0]);
        Assert.Contains(Path.Combine("Users", "someone"), paths[1]);
    }

    [Fact]
    public void MacPathsSkipTheUserFolderWhenHomeIsUnknown()
    {
        Assert.Single(DaemonExePaths.InstalledMacPaths(null));
    }

    // --- Provenance: adopted installs are not "ours" ---

    [Fact]
    public void OwnBuildRecognizesTheBundledCopy()
    {
        var baseDir = Path.Combine("C:", "app");

        Assert.True(DaemonExePaths.IsOwnBuild(baseDir, Path.Combine(baseDir, "Daemon", Exe)));
    }

    // An adopted install must not read as ours, or destructive actions on the user's own daemon would
    // stop asking for confirmation.
    [Fact]
    public void OwnBuildRejectsAnInstalledOtd()
    {
        var installed = Path.Combine("/", "Applications", "OpenTabletDriver.app", "Contents", "MacOS", "OpenTabletDriver.Daemon");

        Assert.False(DaemonExePaths.IsOwnBuild(Path.Combine("C:", "app"), installed));
        Assert.False(DaemonExePaths.IsOwnBuild(Path.Combine("C:", "app"), null));
    }
}
