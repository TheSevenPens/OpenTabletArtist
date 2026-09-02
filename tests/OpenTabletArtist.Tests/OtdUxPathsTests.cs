using System.IO;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Guards the gate behind the Daemon page's OTD UX card: the card is shown only when the
/// submodule's UX project is actually there, so a published build doesn't offer a button that can
/// silently do nothing. <c>AppSession.CanLaunchOtdUx</c> is <c>Directory.Exists(ProjectPath(baseDir))</c>,
/// so these cover both branches of it against a real directory tree.
/// </summary>
public class OtdUxPathsTests
{
    [Fact]
    public void ResolvesToTheSubmoduleUxProjectFromADevBuildTree()
    {
        var baseDir = Path.Combine("C:", "repo", "OpenTabletArtist", "bin", "Debug", "net10.0");

        Assert.Equal(
            Path.GetFullPath(Path.Combine("C:", "repo", "external", "OpenTabletDriver",
                OtdUxPaths.ProjectFolderName)),
            OtdUxPaths.ProjectPath(baseDir));
    }

    [Fact]
    public void DevTreeLayout_ResolvesToAnExistingDirectory()
    {
        // A repo root with the submodule's UX project, and the app four levels below it.
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "external", "OpenTabletDriver",
                OtdUxPaths.ProjectFolderName));
            var baseDir = Path.Combine(root, "OpenTabletArtist", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(baseDir);

            Assert.True(Directory.Exists(OtdUxPaths.ProjectPath(baseDir)),
                "A development tree has the UX sources — the OTD UX card should be shown.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PublishedLayout_ResolvesToANonExistentDirectory()
    {
        // A release install is a flat folder, not four levels under a repo root: nothing to launch.
        var installDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.False(Directory.Exists(OtdUxPaths.ProjectPath(installDir)),
                "A published build ships no UX sources — the OTD UX card must stay hidden.");
        }
        finally { Directory.Delete(installDir, recursive: true); }
    }
}
