using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The SETTINGS rail's two OS-filtered pivots. <b>System</b> holds integration capabilities — the Windows
/// pages (Startup Run key + Shortcut .lnk) on Windows, the application-menu-entry (.desktop) card on Linux —
/// so it shows on both and is hidden only on macOS. <b>Drivers</b> is Windows-only. The filter is a pure
/// predicate (<see cref="SettingsViewModel.TabAppliesToOs"/>) so it's testable without constructing the whole
/// view-model graph — mirroring <see cref="AdvancedRailTests"/>.
/// </summary>
public class SettingsRailTests
{
    [Fact]
    public void System_ShownOnWindowsAndLinux_HiddenOnMac()
    {
        // Windows: the Startup + Shortcut pages.
        Assert.True(SettingsViewModel.TabAppliesToOs(SettingsTab.System, isWindows: true, isLinux: false));
        // Linux: the application-menu-entry card.
        Assert.True(SettingsViewModel.TabAppliesToOs(SettingsTab.System, isWindows: false, isLinux: true));
        // macOS (neither): no equivalent yet, so the pivot is hidden.
        Assert.False(SettingsViewModel.TabAppliesToOs(SettingsTab.System, isWindows: false, isLinux: false));
    }

    [Fact]
    public void Drivers_ShownOnWindowsOnly()
    {
        // It finds conflicting manufacturer drivers and runs TabletDriverCleanup — both Windows-only, which
        // it inherits from having been System's Windows-only right column (#drivers-tab).
        Assert.True(SettingsViewModel.TabAppliesToOs(SettingsTab.Drivers, isWindows: true, isLinux: false));
        Assert.False(SettingsViewModel.TabAppliesToOs(SettingsTab.Drivers, isWindows: false, isLinux: true));
        Assert.False(SettingsViewModel.TabAppliesToOs(SettingsTab.Drivers, isWindows: false, isLinux: false));
    }

    [Theory]
    [InlineData(SettingsTab.Presets)]
    [InlineData(SettingsTab.Theme)]
    [InlineData(SettingsTab.Hotkeys)]
    public void CrossPlatformTabs_ShownOnEveryOs(SettingsTab tab)
    {
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: true, isLinux: false));
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: false, isLinux: true));
        Assert.True(SettingsViewModel.TabAppliesToOs(tab, isWindows: false, isLinux: false));
    }
}
