namespace OpenTabletArtist.Domain;

/// <summary>Tabs of the SETTINGS tabbed page — OpenTabletArtist's own preferences. Startup is
/// Windows-only (registry Run key, <c>StartupService.IsSupported</c>) and hidden off-Windows; Theme and
/// Dev Tools are cross-platform. The Developer page itself is a separate top-level nav node (shown after
/// ADVANCED), toggled by the checkbox on the Dev Tools tab. See docs/design/ux-terminology.md.</summary>
public enum SettingsTab
{
    Startup = 0,
    Theme = 1,
    DevTools = 2,
}
