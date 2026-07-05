using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The "OpenTabletDriver" hub page: a single sidebar entry whose content area has its own secondary tab
/// rail (like a tablet's page), hosting the underlying OTD engine pages — Daemon, Windows Ink Plugin,
/// Custom Tablet Compatibility, Diagnostics, Log, Plugins. It doesn't own those view models; it just
/// holds the shared instances so each tab can display the existing view. <see cref="SelectedTab"/> lets
/// callers deep-link to a specific tab (e.g. a health-issue "Fix" opening the Windows Ink tab).
/// </summary>
public partial class OpenTabletDriverViewModel : ObservableObject
{
    public OpenTabletDriverViewModel(DaemonViewModel daemon, WindowsInkViewModel windowsInk,
        CustomTabletConfigsViewModel configs, DiagnosticsViewModel diagnostics, LogViewModel log,
        PluginsViewModel plugins)
    {
        Daemon = daemon;
        WindowsInk = windowsInk;
        Configs = configs;
        Diagnostics = diagnostics;
        Log = log;
        Plugins = plugins;
    }

    public DaemonViewModel Daemon { get; }
    public WindowsInkViewModel WindowsInk { get; }
    public CustomTabletConfigsViewModel Configs { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public LogViewModel Log { get; }
    public PluginsViewModel Plugins { get; }

    /// <summary>Tab to preselect for deep-links (#393).</summary>
    [ObservableProperty] private OtdHubTab _selectedTab = OtdHubTab.Daemon;
}
