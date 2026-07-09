using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The ADVANCED tabbed page: a single sidebar node whose content area has its own subpage
/// navigation (tab rail, like a tablet's page), hosting the advanced subpages in two groups —
/// OpenTabletDriver's own (Daemon, Windows Ink Plugin, Custom Tablet Compatibility, Diagnostics,
/// Console, Plugins) and OpenTabletArtist's own (VMulti Driver, Driver Cleanup, Startup, Developer,
/// Theme). It doesn't own those view models; it holds the shared instances so each tab can display the
/// existing view. <see cref="SelectedTab"/> lets callers deep-link to a specific tab (e.g. a
/// health-issue "Fix" opening the Windows Ink tab).
///
/// Replaces the old OpenTabletDriver tabbed page: its six tabs are now the first group here, so the
/// separate OPENTABLETDRIVER sidebar node is gone. See docs/design/ux-terminology.md.
/// </summary>
public partial class AdvancedViewModel : ObservableObject
{
    public AdvancedViewModel(
        DaemonViewModel daemon, WindowsInkViewModel windowsInk, CustomTabletConfigsViewModel configs,
        DiagnosticsViewModel diagnostics, LogViewModel log, PluginsViewModel plugins,
        VMultiViewModel vmulti, DriverCleanupViewModel driverCleanup, StartupViewModel startup,
        DeveloperViewModel developer, ThemeViewModel theme)
    {
        Daemon = daemon;
        WindowsInk = windowsInk;
        Configs = configs;
        Diagnostics = diagnostics;
        Log = log;
        Plugins = plugins;
        VMulti = vmulti;
        DriverCleanup = driverCleanup;
        Startup = startup;
        Developer = developer;
        Theme = theme;
    }

    // OpenTabletDriver group
    public DaemonViewModel Daemon { get; }
    public WindowsInkViewModel WindowsInk { get; }
    public CustomTabletConfigsViewModel Configs { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public LogViewModel Log { get; }
    public PluginsViewModel Plugins { get; }
    // OpenTabletArtist group
    public VMultiViewModel VMulti { get; }
    public DriverCleanupViewModel DriverCleanup { get; }
    public StartupViewModel Startup { get; }
    public DeveloperViewModel Developer { get; }
    public ThemeViewModel Theme { get; }

    /// <summary>Tab to preselect for deep-links (health-issue "Fix", the daemon card's "Open daemon page", …).</summary>
    [ObservableProperty] private AdvancedTab _selectedTab = AdvancedTab.Daemon;
}
