using System;
using System.IO;
using System.Linq;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class PressurePluginInstallerTests
{
    [Fact]
    public void SourceCandidates_BundledLocationFirst()
    {
        var baseDir = Path.Combine("C:", "app");
        var first = PressurePluginPaths.SourceCandidates(baseDir).First();
        Assert.Equal(
            Path.GetFullPath(Path.Combine(baseDir, "BundledPlugins", "OtdWindowsHelperPressureCurve", "OtdWindowsHelper.PressureCurve.dll")),
            first);
    }

    [Fact]
    public void SourceCandidates_IncludesDevBuildOutput()
    {
        var candidates = PressurePluginPaths.SourceCandidates(Path.Combine("C:", "repo", "OTDWindowsHelper", "bin", "Debug", "net10.0")).ToList();
        Assert.Equal(3, candidates.Count);
        Assert.Contains(candidates, c => c.Contains(Path.Combine("plugins", "OtdWindowsHelper.PressureCurve")) && c.Contains(Path.Combine("bin", "Debug", "net8.0")));
    }

    [Fact]
    public void CopyIfNeeded_FreshThenUpToDate()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "src.dll");
            File.WriteAllText(src, "v1");
            var pluginDir = Path.Combine(dir, "plugins");

            Assert.Equal(PluginInstallOutcome.Installed, PressurePluginInstaller.CopyIfNeeded(src, pluginDir));
            var dest = Path.Combine(pluginDir, "OtdWindowsHelperPressureCurve", "OtdWindowsHelper.PressureCurve.dll");
            Assert.True(File.Exists(dest));

            Assert.Equal(PluginInstallOutcome.None, PressurePluginInstaller.CopyIfNeeded(src, pluginDir)); // up to date
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CopyIfNeeded_OverwriteReportsUpdated()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "src.dll");
            File.WriteAllText(src, "v1");
            var pluginDir = Path.Combine(dir, "plugins");
            Assert.Equal(PluginInstallOutcome.Installed, PressurePluginInstaller.CopyIfNeeded(src, pluginDir));

            File.WriteAllText(src, "v2-longer-content"); // different size + newer
            Assert.Equal(PluginInstallOutcome.Updated, PressurePluginInstaller.CopyIfNeeded(src, pluginDir));
            var dest = Path.Combine(pluginDir, "OtdWindowsHelperPressureCurve", "OtdWindowsHelper.PressureCurve.dll");
            Assert.Equal("v2-longer-content", File.ReadAllText(dest));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CopyIfNeeded_MissingSource_IsNone()
        => Assert.Equal(PluginInstallOutcome.None,
            PressurePluginInstaller.CopyIfNeeded(Path.Combine("C:", "nope", "x.dll"), Path.Combine("C:", "plugins")));

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"otdplug_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }
}
