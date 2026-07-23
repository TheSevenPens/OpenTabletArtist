using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>One tab in the SETTINGS rail: its label, which subpage it selects, the content view model the
/// host shows when it's active, a selection flag the rail highlights, and a visibility flag (gated tabs
/// like Developer and Per-App Presets hide themselves).</summary>
public partial class SettingsTabItem : ObservableObject
{
    public SettingsTabItem(string label, SettingsTab tab, object content, bool isVisible = true)
    {
        Label = label;
        Tab = tab;
        Content = content;
        _isVisible = isVisible;
    }

    public string Label { get; }
    public SettingsTab Tab { get; }
    // The subpage view model; the content host resolves it to a view by DataTemplate on its runtime type.
    public object Content { get; }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible;
}

/// <summary>
/// The SETTINGS tabbed page: a sidebar node whose content area has its own subpage navigation — a
/// <b>flat</b> tab rail hosting OpenTabletArtist's preference subpages. Split out of the ADVANCED page so
/// those OTA-owned settings live under their own node. Mirrors <see cref="AdvancedViewModel"/> (data-driven
/// rail + shared subpage VMs, deep-linkable via <see cref="SelectedTab"/>), but flat — there are no owner
/// sections here. Presets/Per-App/Developer folded in from top-level nav nodes (#571/#572); Per-App is
/// feature-gated via its tab's IsVisible. See docs/design/ux-terminology.md.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsTabItem[] _allTabs;

    public SettingsViewModel(StartupViewModel startup, HotkeysViewModel hotkeys, ThemeViewModel theme,
        ShortcutViewModel shortcut, DesktopEntryViewModel desktopEntry, DriverCleanupViewModel driverCleanup,
        PresetsViewModel presets, PerAppViewModel perApp, DeveloperViewModel developer)
    {
        // The "System" pivot holds each OS's own integration capabilities, kept deliberately separate so
        // Windows and Linux each do their native thing (no shared abstraction). On Windows it's the three
        // Windows pages in two columns (Startup + Shortcut | Driver Cleanup); on Linux it's the single
        // application-menu-entry (.desktop) card, the counterpart to the Windows Start-menu shortcut. macOS
        // has no equivalent yet, so the pivot is hidden there (see TabAppliesToOs).
        object system = OperatingSystem.IsWindows()
            ? new SystemSettingsViewModel(startup, shortcut, driverCleanup)
            : desktopEntry;
        var tabs = new SettingsTabItem[]
        {
            new("PRESETS", SettingsTab.Presets, presets),
            new("PER-APP PRESETS", SettingsTab.PerAppPresets, perApp, isVisible: FeatureFlags.PerAppProfiles),
            new("HOTKEYS", SettingsTab.Hotkeys, hotkeys),
            new("APPEARANCE", SettingsTab.Theme, theme),
            new("SYSTEM", SettingsTab.System, system),
            new("DEVELOPER", SettingsTab.Developer, developer),
        }.Where(t => TabAppliesToOs(t.Tab, OperatingSystem.IsWindows(), OperatingSystem.IsLinux())).ToArray();
        Tabs = tabs;
        _allTabs = tabs;

        // Default to the first visible tab — the configured default may be OS-hidden or gated off.
        if (Current is not { IsVisible: true } && FirstVisible is { } first)
            _selectedTab = first.Tab;
        UpdateSelection();
    }

    /// <summary>Whether a SETTINGS pivot applies on the given OS. The <b>System</b> pivot holds OS-specific
    /// integration capabilities — the Windows pages (Startup Run key + Shortcut .lnk + Driver Cleanup) on
    /// Windows, the application-menu-entry card on Linux — so it shows on both but is hidden on macOS, which
    /// has no equivalent yet. Every other pivot is cross-platform. Pure (OS passed in, not checked inline)
    /// so it's unit-testable — matching <see cref="AdvancedViewModel.RailTabAppliesToOs"/>.</summary>
    public static bool TabAppliesToOs(SettingsTab tab, bool isWindows, bool isLinux) =>
        tab != SettingsTab.System || isWindows || isLinux;

    /// <summary>The settings subpages, flat (no owner grouping). Gated tabs stay in the list but hide via
    /// their <see cref="SettingsTabItem.IsVisible"/>.</summary>
    public IReadOnlyList<SettingsTabItem> Tabs { get; }

    /// <summary>The active tab; the deep-link target (e.g. the command palette's "settings-…" entries).</summary>
    [ObservableProperty] private SettingsTab _selectedTab = SettingsTab.Presets;

    /// <summary>The subpage the content host shows for the current tab.</summary>
    public object? SelectedContent => Current?.Content;
    /// <summary>The current subpage's name, shown as its body tab title (matching the tablet page's tabs).</summary>
    public string CurrentTabTitle => Current?.Label ?? "";
    private SettingsTabItem? Current => _allTabs.FirstOrDefault(t => t.Tab == SelectedTab);
    private SettingsTabItem? FirstVisible => _allTabs.FirstOrDefault(t => t.IsVisible);

    /// <summary>Clicking a rail tab selects it (the tab item is the command parameter).</summary>
    [RelayCommand]
    private void SelectTab(SettingsTabItem? item)
    {
        if (item != null) SelectedTab = item.Tab;
    }

    partial void OnSelectedTabChanged(SettingsTab oldValue, SettingsTab newValue)
    {
        // Coerce a deep-link to a tab that's hidden on this OS or gated off, back to the first visible tab.
        var target = _allTabs.FirstOrDefault(t => t.Tab == newValue);
        if ((target is null || !target.IsVisible) && FirstVisible is { } first && first.Tab != newValue)
        {
            SelectedTab = first.Tab;
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
