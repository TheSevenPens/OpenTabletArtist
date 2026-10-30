using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The Filters tab on the Advanced page (#1049). Filters are per tablet (#1052), so it lists every tablet's own,
/// each under that tablet's name, rather than one tablet's under a page that has no way to say which.
/// </summary>
public class FiltersViewModelTests
{
    private static readonly PenDynamicsSettings SoftCurve =
        new(new PressureCurveSettings(0.4, 0, 1, 0, 1, PressureMinApproach.Clamp), 0, 0, true);

    /// <summary>A profile with the Pen Dynamics filter, and the Hover Limit when asked.</summary>
    private static Profile Profile(string tablet, bool dynamics = true, bool? hover = null)
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { OutputMode = TestOtd.Output(), Tablet = tablet } } };
        if (dynamics) PressureCurveProfile.Write(settings, tablet, SoftCurve, enable: true);
        if (hover is { } enabled) HoverProfile.Write(settings, tablet, 127, enabled, false);
        return settings.Profiles.First();
    }

    private static ProfileItem Item(Profile profile, bool detected) => new(profile, detected, LastSeen: null);

    private static FakeDeviceData Data(params ProfileItem[] items) => new() { Profiles = items.ToList() };

    [Fact]
    public void EveryKnownTablet_GetsASection_PluggedInFirst_ThenByName()
    {
        var data = Data(Item(Profile("Wacom PTH-660"), false), Item(Profile("Huion HS611"), true),
            Item(Profile("Wacom PTK-470"), false), Item(Profile("XP-Pen Deco L"), true));
        using var vm = new FiltersViewModel(data);

        Assert.Equal(["Huion HS611", "XP-Pen Deco L", "Wacom PTH-660", "Wacom PTK-470"], vm.Tablets.Select(t => t.Name));
        Assert.True(vm.HasTablets);
    }

    /// <summary>The point of listing every tablet: one tablet's filters are never shown under another's name.</summary>
    [Fact]
    public void EachTabletShowsOnlyItsOwnFilters()
    {
        var data = Data(Item(Profile("Wacom PTK-870", dynamics: true, hover: false), true),
            Item(Profile("Huion HS611", dynamics: false), true),
            Item(Profile("Wacom PTH-660", dynamics: true), false));
        using var vm = new FiltersViewModel(data);

        Assert.Equal(["Hover Limit", "Pen Dynamics"], Titles(vm, "Wacom PTK-870").OrderBy(t => t));
        Assert.Empty(Titles(vm, "Huion HS611"));
        Assert.Equal(["Pen Dynamics"], Titles(vm, "Wacom PTH-660"));
    }

    [Fact]
    public void AFilterIsNamedForWhatItIs_AndSaidToBeOnOrOff()
    {
        var data = Data(Item(Profile("Wacom PTK-870", dynamics: true, hover: false), true));
        using var vm = new FiltersViewModel(data);

        var cards = vm.Tablets.Single().Filters.ToDictionary(f => f.Title);

        Assert.Equal("Enabled", cards["Pen Dynamics"].StatusText);
        Assert.Equal("Disabled", cards["Hover Limit"].StatusText);
        Assert.EndsWith("DynamicsFilter", cards["Pen Dynamics"].FullPath);
        Assert.False(cards["Pen Dynamics"].IsLegacy);
    }

    [Fact]
    public void ATabletWithNoFilters_SaysSo()
    {
        var data = Data(Item(Profile("Huion HS611", dynamics: false), true));
        using var vm = new FiltersViewModel(data);

        var tablet = vm.Tablets.Single();

        Assert.False(tablet.HasFilters);
        Assert.True(tablet.HasNoFilters);
    }

    [Fact]
    public void WithNoTabletsAtAll_ThereIsNothingToList()
    {
        using var vm = new FiltersViewModel(Data());

        Assert.False(vm.HasTablets);
        Assert.Empty(vm.Tablets);
    }

    /// <summary>It shows what is saved now. A filter switched on after the page was built is picked up when settings
    /// load again and when the tab is opened, not only when the app started.</summary>
    [Fact]
    public void ItRebuilds_WhenSettingsLoad_AndWhenAskedTo()
    {
        var profile = Profile("Wacom PTK-870", dynamics: true);
        var data = Data(Item(profile, true));
        using var vm = new FiltersViewModel(data);
        Assert.Single(vm.Tablets.Single().Filters);

        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        HoverProfile.Write(settings, "Wacom PTK-870", 127, enable: true, nearProximityOnly: false);
        data.RaiseDataLoaded();
        Assert.Equal(2, vm.Tablets.Single().Filters.Count);

        profile.Filters.Remove(profile.Filters.First(f => f!.Path!.EndsWith("HoverFilter")));
        vm.Refresh();
        Assert.Single(vm.Tablets.Single().Filters);
    }

    [Fact]
    public void ADisposedViewModel_StopsListeningToSettings()
    {
        var data = Data(Item(Profile("Wacom PTK-870"), true));
        var vm = new FiltersViewModel(data);
        vm.Dispose();

        data.Profiles = [];
        data.RaiseDataLoaded();

        Assert.Single(vm.Tablets);
    }

    private static List<string> Titles(FiltersViewModel vm, string tablet) =>
        vm.Tablets.Single(t => t.Name == tablet).Filters.Select(f => f.Title).ToList();
}

/// <summary>
/// The Filters tab is a developer view on the Advanced page: hidden until Settings, Developer turns it on, and gone
/// again, with the page falling back to the Daemon tab, when it is turned off while open.
/// </summary>
// These switch a developer setting, which is written to the app settings file; see TestAppDataRootTests.
[Collection("AppSettingsFile")]
public class AdvancedFiltersTabTests
{
    // The tabs this test does not exercise are never touched on these paths, so they are left out.
    private static AdvancedViewModel Advanced(FiltersViewModel filters) =>
        new(daemon: null!, configs: null!, diagnostics: null!, log: null!, plugins: null!, filters: filters);

    [Fact]
    public void TheFiltersTab_IsHidden_UntilTheDeveloperSettingTurnsItOn()
    {
        using var filters = new FiltersViewModel(new FakeDeviceData());
        var was = DeveloperSettings.Instance.ShowFiltersTab;
        try
        {
            DeveloperSettings.Instance.ShowFiltersTab = false;
            var advanced = Advanced(filters);
            Assert.False(advanced.Tabs.Single(t => t.Tab == AdvancedTab.Filters).IsVisible);

            DeveloperSettings.Instance.ShowFiltersTab = true;
            Assert.True(advanced.Tabs.Single(t => t.Tab == AdvancedTab.Filters).IsVisible);
        }
        finally { DeveloperSettings.Instance.ShowFiltersTab = was; }
    }

    /// <summary>Turning it off while it is the open tab must not leave the page showing a tab the rail no longer lists.</summary>
    [Fact]
    public void TurningItOffWhileOpen_FallsBackToTheDaemonTab()
    {
        using var filters = new FiltersViewModel(new FakeDeviceData());
        var was = DeveloperSettings.Instance.ShowFiltersTab;
        try
        {
            DeveloperSettings.Instance.ShowFiltersTab = true;
            var advanced = Advanced(filters);
            advanced.SelectedTab = AdvancedTab.Filters;
            Assert.Same(filters, advanced.SelectedContent);

            DeveloperSettings.Instance.ShowFiltersTab = false;

            Assert.Equal(AdvancedTab.Daemon, advanced.SelectedTab);
        }
        finally { DeveloperSettings.Instance.ShowFiltersTab = was; }
    }

    [Fact]
    public void OpeningTheTab_RebuildsTheList()
    {
        var data = new FakeDeviceData();
        using var filters = new FiltersViewModel(data);
        var was = DeveloperSettings.Instance.ShowFiltersTab;
        try
        {
            DeveloperSettings.Instance.ShowFiltersTab = true;
            var advanced = Advanced(filters);
            Assert.Empty(filters.Tablets);

            // A tablet appears without settings loading again.
            data.Profiles = [new ProfileItem(new Profile { OutputMode = TestOtd.Output(), Tablet = "Wacom PTK-870" }, true, null)];
            advanced.SelectedTab = AdvancedTab.Filters;

            Assert.Single(filters.Tablets);
        }
        finally { DeveloperSettings.Instance.ShowFiltersTab = was; }
    }
}
