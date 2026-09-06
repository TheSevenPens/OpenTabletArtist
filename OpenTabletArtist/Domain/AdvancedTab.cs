namespace OpenTabletArtist.Domain;

/// <summary>Pivots of the ADVANCED tabbed page (Zune Phase 2 merges): <b>Daemon</b> (daemon status),
/// <b>Console</b> (the daemon log), <b>Configs</b>, <b>Diagnostics</b>, <b>Plugins</b>. The VMulti
/// driver had a pivot here until #vmulti-to-drivers moved it to SETTINGS → DRIVERS, beside driver cleanup;
/// Windows Ink left for Plugins before it. Deep-links to a merged-away page target its containing pivot. See docs/design/ux-terminology.md and zune-redesign.md.</summary>
public enum AdvancedTab
{
    Daemon = 0,               // daemon status + version
    CustomTabletConfigs = 2,
    Diagnostics = 3,
    Plugins = 4,
    Console = 5,              // the daemon Console log (its own tab, next to Daemon in the rail)
}
