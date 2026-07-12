namespace OpenTabletArtist.Domain;

/// <summary>Tabs of the ADVANCED tabbed page, a single flat list (#477): OpenTabletDriver's own subpages
/// (Daemon, Windows Ink Plugin, Configs, Diagnostics, Console, Plugins) followed by the driver-management
/// pages (VMulti, Driver Cleanup). OTA's own preference pages (Startup, Developer, Theme) moved to the
/// SETTINGS page (<see cref="SettingsTab"/>). See docs/design/ux-terminology.md.</summary>
public enum AdvancedTab
{
    Daemon = 0,
    WindowsInk = 1,
    CustomTabletConfigs = 2,
    Diagnostics = 3,
    Log = 4,
    Plugins = 5,
    VMulti = 6,
    DriverCleanup = 7,
}
