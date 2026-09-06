namespace OpenTabletArtist.Domain;

/// <summary>Pivots of the SETTINGS tabbed page — OpenTabletArtist's own preferences (Zune Phase 2 merges).
/// <b>Theme</b> and <b>Developer</b> show as "THEME" and "DEV". <b>System</b> holds OS-specific integration
/// capabilities — Startup + Shortcut on Windows, the application-menu-entry (.desktop) card on Linux — so it
/// shows on both and is hidden only on macOS; <b>Drivers</b> is Windows-only, since cleaning up a
/// manufacturer tablet driver is a Windows problem. Per-App Presets is feature-gated. Deep-links to a
/// merged-away page target its containing pivot. See docs/design/ux-terminology.md and zune-redesign.md.</summary>
public enum SettingsTab
{
    Presets = 0,       // moved in from a top-level nav node (#571).
    PerAppPresets = 1, // moved in from a top-level nav node (#571); feature-gated.
    Hotkeys = 2,
    Theme = 3,         // labelled "THEME" in the rail.
    System = 4,        // Zune merge: Startup + Shortcut.
    Drivers = 5,       // Driver Cleanup, out of System's right column (#drivers-tab).
    Developer = 6,     // labelled "DEV"; always shown (#572).
}
