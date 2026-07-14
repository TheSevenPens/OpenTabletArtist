namespace OpenTabletArtist.Domain;

/// <summary>Tabs of the SETTINGS tabbed page — OpenTabletArtist's own preferences. Startup and Shortcut are
/// Windows-only (the registry Run key / a Start-menu .lnk) and hidden off-Windows; Theme and Dev Tools are
/// cross-platform. The Developer page itself is a separate top-level nav node (shown after ADVANCED),
/// toggled by the checkbox on the Dev Tools tab. See docs/design/ux-terminology.md.</summary>
public enum SettingsTab
{
    Startup = 0,
    Theme = 1,
    DevTools = 2,
    Shortcut = 3,
    Hotkeys = 4,
    DriverCleanup = 5, // Windows-only; moved here from ADVANCED (#562).
}
