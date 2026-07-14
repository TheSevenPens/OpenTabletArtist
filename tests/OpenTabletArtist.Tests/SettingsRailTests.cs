using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The SETTINGS rail hides the Windows-only Startup subpage off-Windows (registry Run key). The filter is a
/// pure predicate (<see cref="SettingsViewModel.TabAppliesToOs"/>) so it's testable without constructing the
/// whole view-model graph — mirroring <see cref="AdvancedRailTests"/>.
/// </summary>
public class SettingsRailTests
{
    [Theory]
    [InlineData(SettingsTab.Startup)]        // registry Run key
    [InlineData(SettingsTab.Shortcut)]       // Start-menu .lnk via WScript.Shell
    [InlineData(SettingsTab.DriverCleanup)]  // Windows manufacturer-driver cleanup (moved from Advanced, #562)
    public void WindowsOnlyTabs_HiddenOffWindows_ShownOnWindows(SettingsTab tab)
    {
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: true));
        Assert.False(SettingsViewModel.TabAppliesToOs(tab, isWindows: false));
    }

    [Theory]
    [InlineData(SettingsTab.DevTools)]
    [InlineData(SettingsTab.Theme)]
    [InlineData(SettingsTab.Hotkeys)]
    public void CrossPlatformTabs_ShownOnEveryOs(SettingsTab tab)
    {
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: true));
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: false));
    }
}
