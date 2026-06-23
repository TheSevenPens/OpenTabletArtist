using System;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OtdWindowsHelper.Domain;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class WinInkUpdateStateTests
{
    private const string Name = "Windows Ink";
    private static readonly Version Otd = new(0, 6, 7);

    private static PluginMetadata Meta(string name, Version plugin, Version supported, Version? max = null) =>
        new()
        {
            Name = name,
            PluginVersion = plugin,
            SupportedDriverVersion = supported,
            MaxSupportedDriverVersion = max,
        };

    // --- IsUpdateAvailable ---

    [Fact]
    public void NewerLatest_IsUpdate()
        => Assert.True(WinInkUpdateState.IsUpdateAvailable(new Version(1, 0), new Version(1, 1)));

    [Fact]
    public void OlderLatest_IsNotUpdate()
        => Assert.False(WinInkUpdateState.IsUpdateAvailable(new Version(1, 1), new Version(1, 0)));

    [Fact]
    public void EqualVersions_AreNotUpdate()
        => Assert.False(WinInkUpdateState.IsUpdateAvailable(new Version(1, 0), new Version(1, 0)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullVersion_IsNotUpdate(bool installedNull)
    {
        var installed = installedNull ? null : new Version(1, 0);
        var latest = installedNull ? new Version(1, 0) : null;
        Assert.False(WinInkUpdateState.IsUpdateAvailable(installed, latest));
    }

    // --- SelectNewestCompatible ---

    [Fact]
    public void PicksHighestCompatibleVersion()
    {
        var all = new[]
        {
            Meta(Name, new Version(1, 0, 0), new Version(0, 6, 0)),
            Meta(Name, new Version(1, 3, 0), new Version(0, 6, 0)),
            Meta(Name, new Version(1, 1, 0), new Version(0, 6, 0)),
        };

        var picked = WinInkUpdateState.SelectNewestCompatible(all, Otd, Name);

        Assert.NotNull(picked);
        Assert.Equal(new Version(1, 3, 0), picked!.PluginVersion);
    }

    [Fact]
    public void IgnoresOtherPluginNames()
    {
        var all = new[]
        {
            Meta("Some Other Plugin", new Version(9, 9, 9), new Version(0, 6, 0)),
            Meta(Name, new Version(1, 0, 0), new Version(0, 6, 0)),
        };

        var picked = WinInkUpdateState.SelectNewestCompatible(all, Otd, Name);

        Assert.Equal(new Version(1, 0, 0), picked!.PluginVersion);
    }

    [Fact]
    public void ExcludesIncompatibleReleases()
    {
        var all = new[]
        {
            // Higher version but for OTD 0.5.x — incompatible with 0.6.7 (minor mismatch).
            Meta(Name, new Version(2, 0, 0), new Version(0, 5, 0)),
            Meta(Name, new Version(1, 0, 0), new Version(0, 6, 0)),
        };

        var picked = WinInkUpdateState.SelectNewestCompatible(all, Otd, Name);

        Assert.Equal(new Version(1, 0, 0), picked!.PluginVersion);
    }

    [Fact]
    public void NoCompatibleReleases_ReturnsNull()
    {
        var all = new[] { Meta(Name, new Version(1, 0, 0), new Version(0, 5, 0)) };
        Assert.Null(WinInkUpdateState.SelectNewestCompatible(all, Otd, Name));
    }

    [Fact]
    public void Empty_ReturnsNull()
        => Assert.Null(WinInkUpdateState.SelectNewestCompatible([], Otd, Name));
}
