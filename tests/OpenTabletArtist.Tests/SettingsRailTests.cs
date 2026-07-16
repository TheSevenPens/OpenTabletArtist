using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The SETTINGS rail hides the Windows-only System pivot off-Windows (Startup registry Run key + Shortcut
/// .lnk + Driver Cleanup, merged into one pivot for the Zune redesign). The filter is a pure predicate
/// (<see cref="SettingsViewModel.TabAppliesToOs"/>) so it's testable without constructing the whole
/// view-model graph — mirroring <see cref="AdvancedRailTests"/>.
/// </summary>
public class SettingsRailTests
{
    [Theory]
    [InlineData(SettingsTab.System)]  // Startup + Shortcut + Driver Cleanup — all Windows-only
    public void WindowsOnlyTabs_HiddenOffWindows_ShownOnWindows(SettingsTab tab)
    {
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: true));
        Assert.False(SettingsViewModel.TabAppliesToOs(tab, isWindows: false));
    }

    [Theory]
    [InlineData(SettingsTab.Presets)]
    [InlineData(SettingsTab.DevTools)]
    [InlineData(SettingsTab.Theme)]
    [InlineData(SettingsTab.Hotkeys)]
    public void CrossPlatformTabs_ShownOnEveryOs(SettingsTab tab)
    {
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: true));
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: false));
    }
}
