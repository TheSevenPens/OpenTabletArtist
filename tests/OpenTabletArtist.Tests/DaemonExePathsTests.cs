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
        var candidates = DaemonExePaths.Candidates(baseDir).ToList();

        Assert.Equal(3, candidates.Count);   // bundled, dev Debug, dev Release
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "Daemon", Exe)), candidates[0]);
        Assert.All(candidates.Skip(1), c => Assert.Contains("external", c));
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
