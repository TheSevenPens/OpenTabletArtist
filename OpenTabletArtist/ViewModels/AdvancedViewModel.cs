using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>One tab in the ADVANCED rail (#477): its label, which subpage it selects, the content VM
/// the content host shows when it's active, and a selection flag the rail binds its highlight to.</summary>
public partial class AdvancedTabItem : ObservableObject
{
    public AdvancedTabItem(string label, AdvancedTab tab, object content)
    {
        Label = label;
        Tab = tab;
        Content = content;
    }

    public string Label { get; }
    public AdvancedTab Tab { get; }
    // The subpage view model; the content host resolves it to a view by DataTemplate on its runtime type.
    public object Content { get; }
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// The ADVANCED tabbed page: a single sidebar node whose content area has its own subpage navigation
/// (a <b>flat</b> tab rail, #477), hosting OpenTabletDriver's own subpages (Daemon, Windows Ink Plugin,
/// Configs, Diagnostics, Console, Plugins) plus the driver-management pages (VMulti, Driver Cleanup). It
/// doesn't own those view models; it holds the shared instances so each tab can display the existing view.
/// <see cref="SelectedTab"/> lets callers deep-link to a specific tab (e.g. a health-issue "Fix" opening
/// the Windows Ink tab). OTA's own preference pages (Startup / Developer / Theme) live on the SETTINGS
/// page (<see cref="SettingsViewModel"/>).
///
/// The rail is <b>data-driven</b> (#477): the view binds an ItemsControl to <see cref="Tabs"/> and the
/// content host to <see cref="SelectedContent"/> — no per-tab code-behind. See docs/design/ux-terminology.md.
/// </summary>
public partial class AdvancedViewModel : ObservableObject
{
    private readonly AdvancedTabItem[] _allTabs;
    // Held only to stop the daemon debug stream when the Diagnostics tab is left (see below).
    private readonly DiagnosticsViewModel _diagnostics;
    // Held to re-scan the config folder on entry (the daemon's real path may arrive after construction).
    private readonly CustomTabletConfigsViewModel _configs;

    public AdvancedViewModel(
        DaemonViewModel daemon, WindowsInkViewModel windowsInk, CustomTabletConfigsViewModel configs,
        DiagnosticsViewModel diagnostics, LogViewModel log, PluginsViewModel plugins,
        VMultiViewModel vmulti, DriverCleanupViewModel driverCleanup)
    {
        _diagnostics = diagnostics;
        _configs = configs;

        var tabs = new AdvancedTabItem[]
        {
            new("DAEMON", AdvancedTab.Daemon, daemon),
            new("WINDOWS INK PLUGIN", AdvancedTab.WindowsInk, windowsInk),
            new("CONFIGS", AdvancedTab.CustomTabletConfigs, configs),
            new("DIAGNOSTICS", AdvancedTab.Diagnostics, diagnostics),
            new("CONSOLE", AdvancedTab.Log, log),
            new("PLUGINS", AdvancedTab.Plugins, plugins),
            new("VMULTI DRIVER", AdvancedTab.VMulti, vmulti),
            new("DRIVER CLEANUP", AdvancedTab.DriverCleanup, driverCleanup),
        }.Where(t => RailTabAppliesToOs(t.Tab, OperatingSystem.IsWindows())).ToArray();
        Tabs = tabs;
        _allTabs = tabs;
        UpdateSelection();
    }

    /// <summary>Whether an ADVANCED subpage applies on the given OS. The Windows-only subpages are hidden
    /// off-Windows (#140): VMulti + Windows Ink don't exist on macOS/Linux (the daemon uses its own native
    /// output there) and Driver Cleanup runs a Windows-only tool. Filtering them out of the rail keeps the
    /// deep-link enum intact — a stray deep-link to a hidden tab is coerced back to a visible one (see
    /// <see cref="OnSelectedTabChanged"/>). Pure (OS passed in, not checked inline) so the filter is
    /// unit-testable — matching how the health evaluator takes its platform flag.</summary>
    public static bool RailTabAppliesToOs(AdvancedTab tab, bool isWindows) =>
        isWindows
        || tab is not (AdvancedTab.WindowsInk or AdvancedTab.VMulti or AdvancedTab.DriverCleanup);

    /// <summary>The advanced subpages, a single flat list (no owner grouping).</summary>
    public IReadOnlyList<AdvancedTabItem> Tabs { get; }

    /// <summary>The active tab; the deep-link target (health-issue "Fix", the daemon card's
    /// "Open daemon page"). Setting it swaps the content + rail highlight.</summary>
    [ObservableProperty] private AdvancedTab _selectedTab = AdvancedTab.Daemon;

    /// <summary>The subpage the content host shows for the current tab.</summary>
    public object? SelectedContent => Current?.Content;
    /// <summary>The current subpage's name, shown as its body tab title (matching the tablet page's tabs).</summary>
    public string CurrentTabTitle => Current?.Label ?? "";
    private AdvancedTabItem? Current => _allTabs.FirstOrDefault(t => t.Tab == SelectedTab);

    /// <summary>Clicking a rail tab selects it (the tab item is the command parameter).</summary>
    [RelayCommand]
    private void SelectTab(AdvancedTabItem? item)
    {
        if (item != null) SelectedTab = item.Tab;
    }

    partial void OnSelectedTabChanged(AdvancedTab oldValue, AdvancedTab newValue)
    {
        // A deep-link to a tab hidden on this OS (e.g. a stale nav or the developer screenshot aid targeting
        // WindowsInk off-Windows) would leave nothing selected and a blank content area — coerce to the first
        // visible tab instead. Re-enters this handler with a valid tab, so the work below runs for it. (#140)
        if (_allTabs.Length > 0 && _allTabs.All(t => t.Tab != newValue))
        {
            SelectedTab = _allTabs[0].Tab;
            return;
        }

        UpdateSelection();
        OnPropertyChanged(nameof(SelectedContent));
        OnPropertyChanged(nameof(CurrentTabTitle));

        // Turn the daemon debug stream off when leaving the Diagnostics tab so it doesn't keep cloning
        // reports (see docs/DIAGNOSTICS.md). Leaving the ADVANCED page entirely is covered by the shell
        // (MainViewModel.OnCurrentPageChanged).
        if (oldValue == AdvancedTab.Diagnostics && newValue != AdvancedTab.Diagnostics)
            _ = _diagnostics.StopDebuggingAsync();

        // Re-scan the config folder on entry — the daemon's real path may have arrived since construction.
        if (newValue == AdvancedTab.CustomTabletConfigs)
            _configs.RefreshConfigurationsCommand.Execute(null);
    }

    private void UpdateSelection()
    {
        foreach (var t in _allTabs) t.IsSelected = t.Tab == SelectedTab;
    }
}
