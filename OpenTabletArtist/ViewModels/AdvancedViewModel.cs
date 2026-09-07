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
    // Always visible for Advanced (OS-inapplicable tabs are filtered out of the array); present so the
    // shared TabbedPageView rail template can bind IsVisible uniformly across pages (#zune Phase 0).
    [ObservableProperty] private bool _isVisible = true;
}

/// <summary>
/// The ADVANCED page: a single page in the page menu whose content has its own tab menu
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
        DaemonViewModel daemon, CustomTabletConfigsViewModel configs,
        DiagnosticsViewModel diagnostics, LogViewModel log, PluginsViewModel plugins)
    {
        _diagnostics = diagnostics;
        _configs = configs;

        // Daemon status + version get their own tab; the Console log is its own tab beside it. The VMULTI
        // pivot is gone: it held one card, and that card moved to SETTINGS → DRIVERS beside driver cleanup
        // (#vmulti-to-drivers). With it went the only Windows-only pivot here, so the rail no longer filters
        // by OS at all.
        var tabs = new AdvancedTabItem[]
        {
            new("DAEMON", AdvancedTab.Daemon, daemon),
            new("CONSOLE", AdvancedTab.Console, log),
            new("CONFIGS", AdvancedTab.CustomTabletConfigs, configs),
            new("DIAGNOSTICS", AdvancedTab.Diagnostics, diagnostics),
            new("PLUGINS", AdvancedTab.Plugins, plugins),
        };
        Tabs = tabs;
        _allTabs = tabs;
        UpdateSelection();
    }

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
        UpdateSelection();
        OnPropertyChanged(nameof(SelectedContent));
        OnPropertyChanged(nameof(CurrentTabTitle));

        // Turn the daemon debug stream off when leaving the Diagnostics tab so it doesn't keep cloning
        // reports (see docs/dev/DIAGNOSTICS.md). Leaving the ADVANCED page entirely is covered by the shell
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
