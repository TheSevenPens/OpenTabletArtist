using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class HoverProfileTests
{
    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    [Fact]
    public void Write_ThenRead_RoundTrips()
    {
        var settings = SettingsFor("Tab");

        HoverProfile.Write(settings, "Tab", maxHoverDistance: 80, enable: true);
        var read = HoverProfile.Read(settings, "Tab");

        Assert.NotNull(read);
        Assert.Equal(80, read!.MaxHoverDistance);
        Assert.True(read.Enabled);
        Assert.False(read.NearProximityOnly); // defaults off when not specified
    }

    [Fact]
    public void Write_NearProximity_RoundTrips()
    {
        var settings = SettingsFor("Tab");

        HoverProfile.Write(settings, "Tab", maxHoverDistance: 100, enable: true, nearProximityOnly: true);
        var read = HoverProfile.Read(settings, "Tab");

        Assert.NotNull(read);
        Assert.True(read!.NearProximityOnly);
        Assert.Equal(100, read.MaxHoverDistance);
    }

    [Fact]
    public void Read_NullWhenNoFilter()
    {
        Assert.Null(HoverProfile.Read(SettingsFor("Tab"), "Tab"));
        Assert.Null(HoverProfile.ReadProfile(null));
    }

    [Fact]
    public void Write_Disabled_PreservesValue_AndEnabledFlag()
    {
        var settings = SettingsFor("Tab");

        HoverProfile.Write(settings, "Tab", maxHoverDistance: 120, enable: false);
        var read = HoverProfile.Read(settings, "Tab");

        Assert.NotNull(read);
        Assert.Equal(120, read!.MaxHoverDistance);
        Assert.False(read.Enabled);
    }

    [Fact]
    public void Write_Twice_UpdatesInPlace_NoDuplicateFilters()
    {
        var settings = SettingsFor("Tab");

        HoverProfile.Write(settings, "Tab", 80, enable: true);
        HoverProfile.Write(settings, "Tab", 200, enable: true);

        var filters = settings.Profiles.First().Filters;
        Assert.Single(filters, f => f.Path == HoverProfile.FilterTypeName);
        Assert.Equal(200, HoverProfile.Read(settings, "Tab")!.MaxHoverDistance);
    }
}
