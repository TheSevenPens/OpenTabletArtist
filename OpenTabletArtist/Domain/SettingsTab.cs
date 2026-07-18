namespace OpenTabletArtist.Domain;

/// <summary>Pivots of the SETTINGS tabbed page — OpenTabletArtist's own preferences (Zune Phase 2 merges).
/// <b>Theme</b> shows as "APPEARANCE"; <b>System</b> stacks Startup + Shortcut + Driver Cleanup (all
/// Windows-only, so the pivot is hidden off-Windows). Per-App Presets is feature-gated. Deep-links to a
/// merged-away page target its containing pivot. See docs/design/ux-terminology.md and zune-redesign.md.</summary>
public enum SettingsTab
{
    Presets = 0,       // moved in from a top-level nav node (#571).
    PerAppPresets = 1, // moved in from a top-level nav node (#571); feature-gated.
    Hotkeys = 2,
    Theme = 3,         // labelled "APPEARANCE" in the rail.
    System = 4,        // Zune merge: Startup + Shortcut + Driver Cleanup (all Windows-only).
    Developer = 6,     // always shown (#572).
}
