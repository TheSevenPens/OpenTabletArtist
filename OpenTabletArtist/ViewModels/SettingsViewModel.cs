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
    // Kept for RefreshOnEnter — the two tabs whose contents are built from the presets folder.
    private readonly HotkeysViewModel _hotkeys;
    private readonly PerAppViewModel _perApp;

    public SettingsViewModel(StartupViewModel startup, HotkeysViewModel hotkeys, ThemeViewModel theme,
        ShortcutViewModel shortcut, DesktopEntryViewModel desktopEntry, DriverCleanupViewModel driverCleanup,
        VMultiViewModel vmulti, PresetsViewModel presets, PerAppViewModel perApp, DeveloperViewModel developer)
    {
        _hotkeys = hotkeys;
        _perApp = perApp;
        // The "System" pivot holds each OS's own integration capabilities, kept deliberately separate so
        // Windows and Linux each do their native thing (no shared abstraction). On Windows it's Startup +
        // Shortcut stacked; on Linux it's the single application-menu-entry (.desktop) card, the counterpart
        // to the Windows Start-menu shortcut. macOS has no equivalent yet, so the pivot is hidden there (see
        // TabAppliesToOs).
        //
        // Driver Cleanup used to be System's right column (#drivers-tab). It is not an integration
        // capability — it removes somebody else's driver — and it is the longest page of the three, so it
        // took the whole right side while Startup and Shortcut are a toggle and a checkbox. Its own pivot
        // now, which also leaves System as a single column of two small cards.
        object system = OperatingSystem.IsWindows()
            ? new CompositeSectionViewModel(startup, shortcut)
            : desktopEntry;

        // Both drivers the app manages, side by side: what is wrong with the machine's existing drivers on
        // the left, the virtual pen driver OTD needs on the right. VMulti had its own ADVANCED pivot until
        // #vmulti-to-drivers, which is where a user looking for "drivers" would never have found it.
        var drivers = new TwoColumnSectionViewModel(new object[] { driverCleanup }, new object[] { vmulti });
        var tabs = new SettingsTabItem[]
        {
            new("PRESETS", SettingsTab.Presets, presets),
            new("PER-APP PRESETS", SettingsTab.PerAppPresets, perApp, isVisible: FeatureFlags.PerAppProfiles),
            new("HOTKEYS", SettingsTab.Hotkeys, hotkeys),
            new("THEME", SettingsTab.Theme, theme),
            new("SYSTEM", SettingsTab.System, system),
            new("DRIVERS", SettingsTab.Drivers, drivers),
            new("DEV", SettingsTab.Developer, developer),
        }.Where(t => TabAppliesToOs(t.Tab, OperatingSystem.IsWindows(), OperatingSystem.IsLinux())).ToArray();
        Tabs = tabs;
        _allTabs = tabs;

        // Default to the first visible tab — the configured default may be OS-hidden or gated off.
        if (Current is not { IsVisible: true } && FirstVisible is { } first)
            _selectedTab = first.Tab;
        UpdateSelection();
    }

    /// <summary>Whether a SETTINGS pivot applies on the given OS. The <b>System</b> pivot holds OS-specific
    /// integration capabilities — the Windows pages (Startup Run key + Shortcut .lnk) on Windows, the
    /// application-menu-entry card on Linux — so it shows on both but is hidden on macOS, which has no
    /// equivalent yet. <b>Drivers</b> is Windows-only: the conflicting drivers it finds and the cleanup tool
    /// it runs are both Windows things, and it inherits that from the Driver Cleanup page it holds, which was
    /// Windows-only inside System. Every other pivot is cross-platform. Pure (OS passed in, not checked
    /// inline) so it's unit-testable — matching <see cref="AdvancedViewModel.RailTabAppliesToOs"/>.</summary>
    public static bool TabAppliesToOs(SettingsTab tab, bool isWindows, bool isLinux) => tab switch
    {
        SettingsTab.System => isWindows || isLinux,
        SettingsTab.Drivers => isWindows,
        _ => true,
    };

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
        RefreshOnEnter(newValue);
    }

    /// <summary>
    /// Rescan the tabs that read the presets folder, on the way in. Both list presets saved on the
    /// Presets tab — one tab along — and the only rescan used to be on entering the SETTINGS page as a
    /// whole, so saving a preset and stepping next door to bind it showed a list without it in, until you
    /// left Settings and came back. The page-entry rescan stays: arriving with one of these already
    /// selected changes no tab, so this never fires for it.
    /// </summary>
    private void RefreshOnEnter(SettingsTab tab)
    {
        if (!TabRescansPresets(tab)) return;
        if (tab == SettingsTab.Hotkeys) _ = _hotkeys.RefreshAsync();
        else _ = _perApp.RefreshAsync();
    }

    /// <summary>Which tabs are built from the presets folder, and so go stale when a preset is saved or
    /// deleted while SETTINGS is already open. A pure predicate, like <see cref="TabAppliesToOs"/>, so the
    /// rule is testable without constructing the whole view-model graph.</summary>
    public static bool TabRescansPresets(SettingsTab tab) =>
        tab is SettingsTab.Hotkeys or SettingsTab.PerAppPresets;

    private void UpdateSelection()
    {
        foreach (var t in _allTabs) t.IsSelected = t.Tab == SelectedTab;
    }
}
