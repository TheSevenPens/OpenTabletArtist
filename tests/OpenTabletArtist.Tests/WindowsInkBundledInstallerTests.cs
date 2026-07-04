using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class WindowsInkBundledInstallerTests
{
    [Fact]
    public void CopyIfNeeded_FreshInstall_CopiesEveryFile_IntoWindowsInkFolder()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "bundle");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "WindowsInk.dll"), "dll");
            File.WriteAllText(Path.Combine(src, "VMulti.dll"), "dll2");
            File.WriteAllText(Path.Combine(src, "metadata.json"), "{}");
            var pluginDir = Path.Combine(dir, "plugins");

            Assert.Equal(PluginInstallOutcome.Installed, WindowsInkBundledInstaller.CopyIfNeeded(src, pluginDir));

            var destDir = Path.Combine(pluginDir, "Windows Ink");
            Assert.True(File.Exists(Path.Combine(destDir, "WindowsInk.dll")));
            Assert.True(File.Exists(Path.Combine(destDir, "VMulti.dll")));
            Assert.True(File.Exists(Path.Combine(destDir, "metadata.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CopyIfNeeded_WhenAlreadyInstalled_ReportsUpdated()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "bundle");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "metadata.json"), "{}");
            var pluginDir = Path.Combine(dir, "plugins");

            Assert.Equal(PluginInstallOutcome.Installed, WindowsInkBundledInstaller.CopyIfNeeded(src, pluginDir));
            // A second copy over an existing metadata.json → Updated (daemon may need a restart to swap DLLs).
            Assert.Equal(PluginInstallOutcome.Updated, WindowsInkBundledInstaller.CopyIfNeeded(src, pluginDir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CopyIfNeeded_MissingSource_IsNone()
        => Assert.Equal(PluginInstallOutcome.None,
            WindowsInkBundledInstaller.CopyIfNeeded(Path.Combine("C:", "nope"), Path.Combine("C:", "plugins")));

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"winink_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }
}
