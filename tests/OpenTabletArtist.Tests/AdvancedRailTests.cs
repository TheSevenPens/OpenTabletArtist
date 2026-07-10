using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The ADVANCED rail hides the Windows-only subpages off-Windows (#140, macOS plan Phase 2). The filter is a
/// pure predicate (<see cref="AdvancedViewModel.RailTabAppliesToOs"/>) so it's testable without constructing
/// the whole view-model graph.
/// </summary>
public class AdvancedRailTests
{
    [Theory]
    [InlineData(AdvancedTab.WindowsInk)]
    [InlineData(AdvancedTab.VMulti)]
    [InlineData(AdvancedTab.DriverCleanup)]
    [InlineData(AdvancedTab.Startup)]
    public void WindowsOnlyTabs_HiddenOffWindows_ShownOnWindows(AdvancedTab tab)
    {
        Assert.True(AdvancedViewModel.RailTabAppliesToOs(tab, isWindows: true));
        Assert.False(AdvancedViewModel.RailTabAppliesToOs(tab, isWindows: false));
    }

    [Theory]
    [InlineData(AdvancedTab.Daemon)]
    [InlineData(AdvancedTab.CustomTabletConfigs)]
    [InlineData(AdvancedTab.Diagnostics)]
    [InlineData(AdvancedTab.Log)]
    [InlineData(AdvancedTab.Plugins)]
    [InlineData(AdvancedTab.Developer)]
    [InlineData(AdvancedTab.Theme)]
    public void CrossPlatformTabs_ShownOnEveryOs(AdvancedTab tab)
    {
        Assert.True(AdvancedViewModel.RailTabAppliesToOs(tab, isWindows: true));
        Assert.True(AdvancedViewModel.RailTabAppliesToOs(tab, isWindows: false));
    }
}
