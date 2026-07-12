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
    [Fact]
    public void Startup_HiddenOffWindows_ShownOnWindows()
    {
        Assert.True(SettingsViewModel.TabAppliesToOs(SettingsTab.Startup, isWindows: true));
        Assert.False(SettingsViewModel.TabAppliesToOs(SettingsTab.Startup, isWindows: false));
    }

    [Theory]
    [InlineData(SettingsTab.DevTools)]
    [InlineData(SettingsTab.Theme)]
    public void CrossPlatformTabs_ShownOnEveryOs(SettingsTab tab)
    {
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: true));
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: false));
    }
}
