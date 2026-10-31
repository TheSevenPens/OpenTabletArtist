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
        Assert.Contains(candidates, c => c.Contains(Path.Combine("bin", "Debug", "net10.0")));
        Assert.Contains(candidates, c => c.Contains(Path.Combine("bin", "Release", "net10.0")));
    }

    /// <summary>
    /// Only OTA's own copy is ever launched (#daemon-bundled-only).
    /// </summary>
    /// <remarks>
    /// The ladder used to start with a location the artist had chosen, then consider an OpenTabletDriver
    /// found installed on the system. Both are gone: a driver someone else installed is reached by
    /// starting it, because OTA connects to whatever holds the pipe. This is the whole ladder now, and
    /// its length is the assertion — an entry creeping back in is exactly what would go unnoticed.
    /// </remarks>
    [Fact]
    public void TheLadderHoldsNothingButOtasOwnCopy()
    {
        var baseDir = Path.Combine("C:", "app");

        // The whole sequence in one assertion: contents, order and count together. Three overlapping
        // assertions said the same thing less clearly, and a failure named only the half it checked.
        string Dev(string config) => Path.GetFullPath(Path.Combine(
            baseDir, "..", "..", "..", "..",
            "external", "OpenTabletDriver", "OpenTabletDriver.Daemon", "bin", config, "net10.0", Exe));

        Assert.Equal(
            [Path.GetFullPath(Path.Combine(baseDir, "Daemon", Exe)), Dev("Debug"), Dev("Release")],
            DaemonExePaths.Candidates(baseDir));
    }

    /// <summary>Which of OTA's two kinds of own copy a path is. Both are managed; only the submodule's output is a
    /// development build.</summary>
    [Fact]
    public void TheSubmodulesOutputIsADevBuild_AndTheBundledCopyIsNot()
    {
        var baseDir = Path.Combine("C:", "repo", "OpenTabletArtist", "bin", "Debug", "net10.0");

        Assert.All(DaemonExePaths.DevTreeCandidates(baseDir), c => Assert.True(DaemonExePaths.IsDevBuild(baseDir, c)));
        Assert.False(DaemonExePaths.IsDevBuild(baseDir, DaemonExePaths.BundledPath(baseDir)));
        Assert.False(DaemonExePaths.IsDevBuild(baseDir, Path.Combine("/", "Applications", "OpenTabletDriver.app", "Contents", "MacOS", "OpenTabletDriver.Daemon")));
        Assert.False(DaemonExePaths.IsDevBuild(baseDir, null));
        Assert.False(DaemonExePaths.IsDevBuild(baseDir, ""));
    }

    [Fact]
    public void OwnBuildRecognizesTheBundledCopy()
    {
        var baseDir = Path.Combine("C:", "app");

        Assert.True(DaemonExePaths.IsAppManaged(baseDir, Path.Combine(baseDir, "Daemon", Exe)));
    }

    // An adopted install must not read as ours, or destructive actions on the user's own daemon would
    // stop asking for confirmation.
    [Fact]
    public void OwnBuildRejectsAnInstalledOtd()
    {
        var installed = Path.Combine("/", "Applications", "OpenTabletDriver.app", "Contents", "MacOS", "OpenTabletDriver.Daemon");

        Assert.False(DaemonExePaths.IsAppManaged(Path.Combine("C:", "app"), installed));
        Assert.False(DaemonExePaths.IsAppManaged(Path.Combine("C:", "app"), null));
    }
}
