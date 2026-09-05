namespace OpenTabletArtist.Domain;

/// <summary>Pivots of the ADVANCED tabbed page (Zune Phase 2 merges): <b>Daemon</b> (daemon status),
/// <b>Console</b> (the daemon log), <b>VMulti</b> (the virtual pen driver — Windows-only),
/// <b>Configs</b>, <b>Diagnostics</b>, <b>Plugins</b>. VMulti was called <b>Drivers</b> while it also
/// carried Windows Ink, which now sits on Plugins beside the plugin list it manages. Deep-links to a
/// merged-away page target its containing pivot. See docs/design/ux-terminology.md and zune-redesign.md.</summary>
public enum AdvancedTab
{
    Daemon = 0,               // daemon status + version
    VMulti = 1,               // the VMulti virtual pen driver (Windows-only). Was Drivers, when it also
                              // carried Windows Ink — that moved to Plugins (#winink-to-plugins).
    CustomTabletConfigs = 2,
    Diagnostics = 3,
    Plugins = 4,
    Console = 5,              // the daemon Console log (its own tab, next to Daemon in the rail)
}
