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

    /// <summary>Hotkeys and Per-App both build their rows from the presets folder, one tab away from where
    /// presets are saved, so selecting either has to rescan — the page-entry rescan alone left a preset
    /// saved during the same visit missing from the list next door.</summary>
    [Theory]
    [InlineData(SettingsTab.Hotkeys)]
    [InlineData(SettingsTab.PerAppPresets)]
    public void PresetBackedTabs_RescanOnEnter(SettingsTab tab)
        => Assert.True(SettingsViewModel.TabRescansPresets(tab));

    /// <summary>Every other tab reads something else, so entering it must not touch the presets folder.
    /// Presets itself is included: it maintains its own list as you save and delete.</summary>
    [Theory]
    [InlineData(SettingsTab.Presets)]
    [InlineData(SettingsTab.Theme)]
    [InlineData(SettingsTab.System)]
    [InlineData(SettingsTab.Drivers)]
    [InlineData(SettingsTab.Developer)]
    public void OtherTabs_DoNotRescan(SettingsTab tab)
        => Assert.False(SettingsViewModel.TabRescansPresets(tab));
}
