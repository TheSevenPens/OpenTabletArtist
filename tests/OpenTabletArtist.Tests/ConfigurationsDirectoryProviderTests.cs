using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ConfigurationsDirectoryProviderTests : IDisposable
{
    private readonly string _root;

    public ConfigurationsDirectoryProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ota-cfgroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>
    /// The fallback heuristic, exercised against an injected root (#738). It used to run against the real
    /// local-app-data folder and <em>create</em> a directory in the developer's OTD install — which failed
    /// outright where that path is unreachable, and quietly mutated a real artist setup where it wasn't.
    /// </summary>
    [Fact]
    public void GetOrCreate_ReturnsOtdConfigurationsPath()
    {
        var dir = new ConfigurationsDirectoryProvider(localAppData: () => _root).GetOrCreate();

        Assert.Equal(Path.Combine(_root, "OpenTabletDriver", "Configurations"), dir);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void GetOrCreate_PrefersDaemonDirectory_WhenProvided()
    {
        // The daemon's real folder (from AppInfo) wins over the fallback heuristic (#480/#467).
        var daemonDir = Path.Combine(_root, "daemon-cfg");

        var dir = new ConfigurationsDirectoryProvider(() => daemonDir, localAppData: () => _root).GetOrCreate();

        Assert.Equal(daemonDir, dir);
    }

    [Fact]
    public void GetOrCreate_ReturnsEmpty_WhenThereIsNoAppDataRoot()
    {
        // No root to build on and no daemon path — hand back the "no directory" signal callers handle
        // rather than a path that doesn't exist.
        Assert.Equal("", new ConfigurationsDirectoryProvider(localAppData: () => null).GetOrCreate());
    }

    [Fact]
    public void GetOrCreate_ReturnsEmpty_WhenTheDirectoryCannotBeCreated()
    {
        // A root whose parent is an existing file can never be created.
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "x");

        Assert.Equal("", new ConfigurationsDirectoryProvider(localAppData: () => blocker).GetOrCreate());
    }
}
