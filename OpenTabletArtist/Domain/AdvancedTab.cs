namespace OpenTabletArtist.Domain;

/// <summary>Tabs of the ADVANCED tabbed page. The first six are OpenTabletDriver's own subpages
/// (shown under an "OpenTabletDriver" section label in the rail); the rest are OpenTabletArtist's own
/// advanced pages ("OpenTabletArtist" section). Replaces the old <c>OtdTab</c>, whose values are the
/// first group here. See docs/design/ux-terminology.md.</summary>
public enum AdvancedTab
{
    // OpenTabletDriver group
    Daemon = 0,
    WindowsInk = 1,
    CustomTabletConfigs = 2,
    Diagnostics = 3,
    Log = 4,
    Plugins = 5,
    // OpenTabletArtist group
    VMulti = 6,
    DriverCleanup = 7,
    Startup = 8,
    Developer = 9,
    Theme = 10,
}
