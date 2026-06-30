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
        Assert.Single(filters.Where(f => f.Path == HoverProfile.FilterTypeName));
        Assert.Equal(200, HoverProfile.Read(settings, "Tab")!.MaxHoverDistance);
    }
}
