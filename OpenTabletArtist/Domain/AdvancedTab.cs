namespace OpenTabletArtist.Domain;

/// <summary>Pivots of the ADVANCED tabbed page (Zune Phase 2 merges): <b>Daemon</b> (daemon status +
/// Console log), <b>Drivers</b> (Windows Ink Plugin + VMulti — Windows-only), <b>Configs</b>,
/// <b>Diagnostics</b>, <b>Plugins</b>. The formerly-separate Console/WindowsInk/VMulti tabs are now
/// stacked inside Daemon/Drivers via <see cref="ViewModels.CompositeSectionViewModel"/>. Deep-links to a
/// merged-away page target its containing pivot. See docs/design/ux-terminology.md and zune-redesign.md.</summary>
public enum AdvancedTab
{
    Daemon = 0,               // daemon status + Console log
    Drivers = 1,              // Windows Ink Plugin + VMulti (Windows-only)
    CustomTabletConfigs = 2,
    Diagnostics = 3,
    Plugins = 4,
}
