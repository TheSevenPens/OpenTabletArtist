using System.IO;
using System.Linq;
using OtdWindowsHelper.Domain;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class DaemonExePathsTests
{
    [Fact]
    public void BundledLocationIsCheckedFirst()
    {
        var baseDir = Path.Combine("C:", "app");
        var candidates = DaemonExePaths.Candidates(baseDir).ToList();

        Assert.Equal(
            Path.GetFullPath(Path.Combine(baseDir, "Daemon", "OpenTabletDriver.Daemon.exe")),
            candidates[0]);
    }

    [Fact]
    public void IncludesDevBuildTreePaths()
    {
        var candidates = DaemonExePaths.Candidates(Path.Combine("C:", "repo", "OTDWindowsHelper", "bin", "Debug", "net10.0")).ToList();

        // Bundled + Debug + Release dev candidates.
        Assert.Equal(3, candidates.Count);
        Assert.All(candidates, c => Assert.EndsWith("OpenTabletDriver.Daemon.exe", c));
        Assert.Contains(candidates, c => c.Contains(Path.Combine("bin", "Debug", "net8.0")));
        Assert.Contains(candidates, c => c.Contains(Path.Combine("bin", "Release", "net8.0")));
    }
}
