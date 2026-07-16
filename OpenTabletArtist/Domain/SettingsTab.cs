namespace OpenTabletArtist.Domain;

/// <summary>Tabs of the SETTINGS tabbed page — OpenTabletArtist's own preferences. Startup, Shortcut, and
/// Driver Cleanup are Windows-only and hidden off-Windows; the rest are cross-platform. Presets and Per-App
/// Presets moved in from top-level nav nodes (#571); Per-App is feature-gated. Developer moved in from a
/// top-level node (#572), shown only when the Dev Tools toggle is on. See docs/design/ux-terminology.md.</summary>
public enum SettingsTab
{
    Startup = 0,
    Theme = 1,
    DevTools = 2,
    Shortcut = 3,
    Hotkeys = 4,
    DriverCleanup = 5, // Windows-only; moved here from ADVANCED (#562).
    Presets = 6,       // moved in from a top-level nav node (#571).
    PerAppPresets = 7, // moved in from a top-level nav node (#571); feature-gated.
    Developer = 8,     // moved in from a top-level nav node (#572); shown only when Dev Tools is on.
}
