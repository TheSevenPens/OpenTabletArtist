using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>One tab in the SETTINGS rail: its label, which subpage it selects, the content view model the
/// host shows when it's active, and a selection flag the rail highlights.</summary>
public partial class SettingsTabItem : ObservableObject
{
    public SettingsTabItem(string label, SettingsTab tab, object content)
    {
        Label = label;
        Tab = tab;
        Content = content;
    }

    public string Label { get; }
    public SettingsTab Tab { get; }
    // The subpage view model; the content host resolves it to a view by DataTemplate on its runtime type.
    public object Content { get; }
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// The SETTINGS tabbed page: a sidebar node whose content area has its own subpage navigation — a
/// <b>flat</b> tab rail hosting OpenTabletArtist's preference subpages (Startup, Theme, Dev Tools). Split
/// out of the ADVANCED page so those OTA-owned settings live under their own node. Mirrors
/// <see cref="AdvancedViewModel"/> (data-driven rail + shared subpage VMs, deep-linkable via
/// <see cref="SelectedTab"/>), but flat — there are no owner sections here. See docs/design/ux-terminology.md.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsTabItem[] _allTabs;

    public SettingsViewModel(StartupViewModel startup, ThemeViewModel theme, DevToolsViewModel devTools)
    {
        var tabs = new SettingsTabItem[]
        {
            new("STARTUP", SettingsTab.Startup, startup),
            new("THEME", SettingsTab.Theme, theme),
            new("DEV TOOLS", SettingsTab.DevTools, devTools),
        }.Where(t => TabAppliesToOs(t.Tab, OperatingSystem.IsWindows())).ToArray();
        Tabs = tabs;
        _allTabs = tabs;

        // Default to the first visible tab — Startup is hidden off-Windows, so it can't be the default there.
        if (_allTabs.Length > 0 && _allTabs.All(t => t.Tab != _selectedTab))
            _selectedTab = _allTabs[0].Tab;
        UpdateSelection();
    }

    /// <summary>Whether a SETTINGS subpage applies on the given OS. Startup is Windows-only (registry Run
    /// key, <c>StartupService.IsSupported</c>); Developer and Theme are cross-platform. Pure (OS passed in,
    /// not checked inline) so it's unit-testable — matching <see cref="AdvancedViewModel.RailTabAppliesToOs"/>.</summary>
    public static bool TabAppliesToOs(SettingsTab tab, bool isWindows) =>
        isWindows || tab != SettingsTab.Startup;

    /// <summary>The settings subpages, flat (no owner grouping).</summary>
    public IReadOnlyList<SettingsTabItem> Tabs { get; }

    /// <summary>The active tab; the deep-link target (e.g. the command palette's "settings-…" entries).</summary>
    [ObservableProperty] private SettingsTab _selectedTab = SettingsTab.Startup;

    /// <summary>The subpage the content host shows for the current tab.</summary>
    public object? SelectedContent => Current?.Content;
    /// <summary>The current subpage's name, shown as its body tab title (matching the tablet page's tabs).</summary>
    public string CurrentTabTitle => Current?.Label ?? "";
    private SettingsTabItem? Current => _allTabs.FirstOrDefault(t => t.Tab == SelectedTab);

    /// <summary>Clicking a rail tab selects it (the tab item is the command parameter).</summary>
    [RelayCommand]
    private void SelectTab(SettingsTabItem? item)
    {
        if (item != null) SelectedTab = item.Tab;
    }

    partial void OnSelectedTabChanged(SettingsTab oldValue, SettingsTab newValue)
    {
        // Coerce a deep-link to a tab hidden on this OS (Startup off-Windows) back to the first visible tab.
        if (_allTabs.Length > 0 && _allTabs.All(t => t.Tab != newValue))
        {
            SelectedTab = _allTabs[0].Tab;
            return;
        }

        UpdateSelection();
        OnPropertyChanged(nameof(SelectedContent));
        OnPropertyChanged(nameof(CurrentTabTitle));
    }

    private void UpdateSelection()
    {
        foreach (var t in _allTabs) t.IsSelected = t.Tab == SelectedTab;
    }
}
