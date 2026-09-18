using System;
using System.Collections.Generic;
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

    // --- Compatibility gate on the offline bundle (#739) ---

    private static readonly Version Otd = new(0, 6, 4, 0);

    /// <summary>Writes a bundle whose manifest declares the given supported range.</summary>
    private static string Bundle(string dir, string? min, string? max = null)
    {
        var src = Path.Combine(dir, "bundle");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "WindowsInk.dll"), "dll");
        var fields = new List<string> { "\"Name\": \"Windows Ink\"", "\"PluginVersion\": \"1.2.3\"" };
        if (min != null) fields.Add($"\"SupportedDriverVersion\": \"{min}\"");
        if (max != null) fields.Add($"\"MaxSupportedDriverVersion\": \"{max}\"");
        File.WriteAllText(Path.Combine(src, "metadata.json"), "{" + string.Join(",", fields) + "}");
        return src;
    }

    [Fact]
    public void InstallIfCompatible_SupportedBundle_IsInstalled()
    {
        var dir = TempDir();
        try
        {
            var src = Bundle(dir, min: "0.6.0");
            var outcome = WindowsInkBundledInstaller.InstallIfCompatible(src, Path.Combine(dir, "plugins"), Otd);

            Assert.Equal(PluginInstallOutcome.Installed, outcome);
            Assert.True(File.Exists(Path.Combine(dir, "plugins", "Windows Ink", "WindowsInk.dll")));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The regression: the release workflow picked the plugin with the highest directory version without
    /// consulting the OTD version shipping beside it, and the offline installer copied whatever it found.
    /// A plugin built for a newer driver would go in and break the pen — so refuse it, and say so.
    /// </summary>
    [Fact]
    public void InstallIfCompatible_BundleNeedingANewerDriver_IsRefused()
    {
        var dir = TempDir();
        try
        {
            var src = Bundle(dir, min: "0.7.0");
            var pluginDir = Path.Combine(dir, "plugins");

            Assert.Equal(PluginInstallOutcome.Incompatible,
                WindowsInkBundledInstaller.InstallIfCompatible(src, pluginDir, Otd));
            Assert.False(Directory.Exists(Path.Combine(pluginDir, "Windows Ink")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void InstallIfCompatible_BundleCappedBelowOurDriver_IsRefused()
    {
        var dir = TempDir();
        try
        {
            var src = Bundle(dir, min: "0.5.0", max: "0.6.3");

            Assert.Equal(PluginInstallOutcome.Incompatible,
                WindowsInkBundledInstaller.InstallIfCompatible(src, Path.Combine(dir, "plugins"), Otd));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void InstallIfCompatible_BundleCappedAtOurDriver_IsInstalled()
    {
        var dir = TempDir();
        try
        {
            // Spelled to four parts on purpose: Version treats an unspecified revision as -1, so a cap
            // written "0.6.4" compares BELOW the running 0.6.4.0 and would be refused. That is upstream's
            // rule, and offline has to apply it exactly as the online path does.
            var src = Bundle(dir, min: "0.6.0", max: "0.6.4.0");

            Assert.Equal(PluginInstallOutcome.Installed,
                WindowsInkBundledInstaller.InstallIfCompatible(src, Path.Combine(dir, "plugins"), Otd));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Upstream compares major and minor for EQUALITY, not "at least" — a plugin built for an
    /// older driver line is as unsupported as one built for a newer one.</summary>
    [Theory]
    [InlineData("0.5.0")]   // older minor
    [InlineData("1.6.0")]   // different major
    public void InstallIfCompatible_BundleFromAnotherDriverLine_IsRefused(string min)
    {
        var dir = TempDir();
        try
        {
            Assert.Equal(PluginInstallOutcome.Incompatible,
                WindowsInkBundledInstaller.InstallIfCompatible(Bundle(dir, min), Path.Combine(dir, "plugins"), Otd));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void InstallIfCompatible_MissingManifest_IsNone()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "bundle");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "WindowsInk.dll"), "dll");   // DLLs but no manifest

            Assert.Equal(PluginInstallOutcome.None,
                WindowsInkBundledInstaller.InstallIfCompatible(src, Path.Combine(dir, "plugins"), Otd));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>A corrupt manifest is a broken bundle: the daemon reads the same file, so copying it in
    /// would produce an install that can't be recognised or updated.</summary>
    [Fact]
    public void InstallIfCompatible_CorruptManifest_IsNone_AndCopiesNothing()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "bundle");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "WindowsInk.dll"), "dll");
            File.WriteAllText(Path.Combine(src, "metadata.json"), "{ truncated");
            var pluginDir = Path.Combine(dir, "plugins");

            Assert.Equal(PluginInstallOutcome.None,
                WindowsInkBundledInstaller.InstallIfCompatible(src, pluginDir, Otd));
            Assert.False(Directory.Exists(Path.Combine(pluginDir, "Windows Ink")));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// A manifest with no declared minimum can't be judged — upstream's <c>IsSupportedBy</c>
    /// dereferences <c>SupportedDriverVersion</c> unconditionally and throws a NullReferenceException
    /// on it. Found by this test: without the guard, a partial manifest took down the install path
    /// instead of being refused.
    /// </summary>
    [Fact]
    public void InstallIfCompatible_ManifestWithNoDeclaredMinimum_IsNone_AndDoesNotThrow()
    {
        var dir = TempDir();
        try
        {
            var src = Bundle(dir, min: null);
            var pluginDir = Path.Combine(dir, "plugins");

            Assert.Equal(PluginInstallOutcome.None,
                WindowsInkBundledInstaller.InstallIfCompatible(src, pluginDir, Otd));
            Assert.False(Directory.Exists(Path.Combine(pluginDir, "Windows Ink")));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"winink_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }
}
