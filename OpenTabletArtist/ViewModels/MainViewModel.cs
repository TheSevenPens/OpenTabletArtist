using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Application shell: owns navigation and the composed page view models, plus the shared
/// <see cref="AppSession"/> (daemon connection + settings + data) and the
/// <see cref="IDialogService"/>. It holds no feature state of its own and no longer pushes data
/// into pages — each page VM self-subscribes to the session's role interfaces (#41 follow-up).
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsFileStore _settingsStore = new SettingsFileStore();
    private readonly AppSession _session;
    private readonly DaemonStatusViewModel _daemonStatus;
    private readonly TabletAutoMapper _autoMapper;
    private readonly WindowsInkAutoSetup _winInkAutoSetup;
    private readonly IDialogService _dialogs;
    private readonly DriverConflictMonitor _conflicts;
    private readonly HealthService _health;
    private readonly ProfileSwitchService _profileSwitch;
    // One shared hotkey window holds every global hotkey; the managers each register their own ids on it.
    private readonly GlobalHotkeyService _globalHotkeys;
    private readonly ProfileHotkeyManager _profileHotkeys;
    private readonly MonitorCycleService _monitorCycle;
    private readonly MonitorCycleHotkeys _monitorHotkeys;
    private readonly PerAppProfileStore _perAppStore;
    private readonly PerAppSwitcher _perAppSwitcher;

    // One cached settings VM per tablet (heavy: holds subscriptions). Reconciled on each data load.
    private readonly Dictionary<string, TabletDetailViewModel> _tabletDetails = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Daemon connection state + Start/Stop/Restart commands — surfaced for the tray menu (#72).</summary>
    public IConnectionState Connection => _session;

    /// <summary>Active-profile override state (#320) — the shell binds this for the "Profile override" cue.</summary>
    public ProfileSwitchService ProfileSwitch => _profileSwitch;

    /// <summary>Monitor-cycle switch events (#89) — the shell subscribes to show a toast on cycle.</summary>
    public MonitorCycleService MonitorCycle => _monitorCycle;

    /// <summary>Per-app switch state (#167) — the shell binds this for the "App profile" cue.</summary>
    public PerAppSwitcher PerAppSwitch => _perAppSwitcher;

    /// <summary>Restore the user's default before exit if a per-app snapshot is applied (#167). Awaited by
    /// the tray's Quit while the daemon is still connected, so no per-app snapshot lingers after close.</summary>
    public Task ShutdownRestorePerAppAsync() => _perAppSwitcher.StopAsync();

    // Surfaced for the tray's tablet actions (#186/#187): the dynamics-reveal line and the
    // Open Tablet Settings / Switch Display items read device data, persist via the settings
    // coordinator, and open the per-tablet dialog through the dialog service.
    public IDeviceData DeviceData => _session;
    public ISettingsCoordinator SettingsCoordinator => _session;
    public IDialogService Dialogs => _dialogs;

    public AboutViewModel About { get; } = new();
    public DriverCleanupViewModel DriverCleanup { get; }
    public CustomTabletConfigsViewModel Configs { get; }
    public PresetsViewModel Presets { get; }
    public HotkeysViewModel Hotkeys { get; }
    public PerAppViewModel PerApp { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public DashboardViewModel Dashboard { get; }
    public TestViewModel Test { get; }
    public LogViewModel Log { get; }
    public PluginsViewModel Plugins { get; }
    public DaemonViewModel Daemon { get; }
    public WindowsInkViewModel WindowsInk { get; }
    public VMultiViewModel VMulti { get; }
    /// <summary>The ADVANCED tabbed page: OpenTabletDriver's subpages (Daemon / Windows Ink / Configs /
    /// Diagnostics / Console / Plugins) plus the driver-management pages (VMulti / Driver Cleanup), all as
    /// tabs behind one sidebar node.</summary>
    public AdvancedViewModel Advanced { get; }
    /// <summary>The SETTINGS tabbed page: OTA's own preference subpages (Startup / Theme / Dev Tools),
    /// split out of ADVANCED into their own sidebar node.</summary>
    public SettingsViewModel Settings { get; }
    public StartupViewModel Startup { get; } = new();
    /// <summary>The DEVELOPER page: a top-level sidebar node (after ADVANCED), shown only when its
    /// visibility toggle (SETTINGS → Dev Tools) is on. Assigned in the constructor (not a field
    /// initializer) so its break-config commands can reach the session's settings coordinator + device data.</summary>
    public DeveloperViewModel Developer { get; }
    public ThemeViewModel Theme { get; } = new();
    /// <summary>The SETTINGS → Dev Tools tab (the Developer-page visibility toggle).</summary>
    public DevToolsViewModel DevTools { get; } = new();
    /// <summary>The SETTINGS → Shortcut tab (create a Start-menu shortcut; Windows-only).</summary>
    public ShortcutViewModel Shortcut { get; } = new();

    /// <summary>Version + BETA, shown in the sidebar footer. The app name is dropped (it's already the
    /// brand at the top of the nav) so the label fits the narrower sidebar. Same version formatter as About.</summary>
    public string TitleBarText { get; } = $"{AppVersionInfo.Format(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)}  BETA";

    /// <summary>Tablets list + supported-tablets link, now rendered as a section of Home (the standalone
    /// Tablets page was merged in). Populated by <see cref="RebuildTablets"/> on each data load.</summary>
    public TabletsOverviewViewModel TabletsOverview { get; }

    /// <summary>The single TABLET page (#542): one sidebar node whose page hosts a switcher dropdown over
    /// the selected tablet's detail view. Replaces the old per-tablet nav children. Its tablet list +
    /// default selection are refreshed by <see cref="RebuildTablets"/> on each data load.</summary>
    public TabletPageViewModel TabletPage { get; }

    // The active page is the VM instance itself (typed navigation, #15). The content host
    // resolves it to a view via DataTemplates keyed by VM type, so there's no page-name string,
    // no view-lookup converter, and no per-view DataContext re-point.
    [ObservableProperty] private ObservableObject? _currentPage;

    // Sidebar highlight (converter-free): HOME and ADVANCED bind IsChecked to these; the flat leaves
    // between them (PRESETS … ABOUT) are data-driven via NavLeaves (#477), each carrying its own
    // IsSelected. Every advanced subpage is a tab inside the ADVANCED tabbed page, so the single ADVANCED
    // node drives its highlight.
    public bool IsDashboard => ReferenceEquals(CurrentPage, Dashboard);
    public bool IsTabletPage => ReferenceEquals(CurrentPage, TabletPage);
    public bool IsSettings => ReferenceEquals(CurrentPage, Settings);
    public bool IsAdvanced => ReferenceEquals(CurrentPage, Advanced);

    /// <summary>The tablet detail page manages its own scrolling — a fixed header + tab rail with a
    /// per-tab scroll region — so the outer content ScrollViewer is <c>Disabled</c> for it, bounding the
    /// page to the viewport so its inner scroll engages and the header no longer scrolls away (#507).
    /// Every other page is plain content and uses the outer scroll (<c>Auto</c>).</summary>
    public Avalonia.Controls.Primitives.ScrollBarVisibility ContentScrollBarVisibility =>
        CurrentPage is TabletPageViewModel or TabletDetailViewModel
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

    /// <summary>The flat leaf nodes between HOME and ADVANCED (#477): each has a label, its target page,
    /// a selection flag the sidebar highlights, and a visibility flag (Per-App is feature-gated).</summary>
    public ObservableCollection<NavLeafViewModel> NavLeaves { get; } = new();

    public MainViewModel()
    {
        _session = new AppSession(new DaemonClient(), new DaemonLifecycleService(), _settingsStore);
        var dialogs = new DialogService(_session);
        _dialogs = dialogs;
        TabletsOverview = new TabletsOverviewViewModel(dialogs); // #155: opens the supported-tablets dialog

        // Conflicting-driver detection (#245), shared by the Driver cleanup page and the Home alert.
        _conflicts = new DriverConflictMonitor(_session.Daemon, _session);

        // Health-check catalog (#317): shared source of the "Needs attention" issues for Home + pages.
        // Takes the conflict monitor too so a conflicting driver surfaces as a health issue.
        _health = new HealthService(_session, _session, _conflicts);

        // First-detection auto-mapping (#362): map a brand-new tablet to the primary display so it
        // doesn't span every monitor out of the box. Only ever acts once per tablet (persisted).
        _autoMapper = new TabletAutoMapper(_session);

        // Windows Ink auto-setup: install the plugin if missing and switch tablets to Windows Ink
        // (once VMulti is functional), so the user never has to configure Windows Ink themselves.
        _winInkAutoSetup = new WindowsInkAutoSetup(_session);

        // Global hotkeys (#320, #89): one shared hotkey window; the profile-switch manager and the
        // monitor-cycle manager each register their own chords on it and filter presses by their ids.
        _globalHotkeys = new GlobalHotkeyService();
        _profileSwitch = new ProfileSwitchService(_session, _settingsStore, () => _session.PresetDirectory);
        _profileHotkeys = new ProfileHotkeyManager(_globalHotkeys, _profileSwitch);
        _monitorCycle = new MonitorCycleService(_session, _session);
        _monitorHotkeys = new MonitorCycleHotkeys(_globalHotkeys, _monitorCycle);
        // Per-app profile switching (#167): a foreground watcher + pen-state provider drive the switch
        // policy, applying snapshots ephemerally (editor stays on the user's default).
        _perAppStore = PerAppProfileStore.ForApp();
        _perAppSwitcher = new PerAppSwitcher(
            new Win32ForegroundAppWatcher(),
            _perAppStore,
            new PerAppApplier(_session, _settingsStore, () => _session.PresetDirectory),
            new DispatcherDebounceScheduler(TimeSpan.FromMilliseconds(200)),
            ownExeName: System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe");

        // Page VMs depend on the session through its role interfaces and on IDialogService,
        // and self-subscribe to the session's data load / connection state.
        DriverCleanup = new DriverCleanupViewModel(dialogs, _conflicts);
        Configs = new CustomTabletConfigsViewModel(dialogs,
            new ConfigurationsDirectoryProvider(() => _session.ConfigurationDirectory));
        Presets = new PresetsViewModel(_settingsStore, _session, _session, dialogs, _profileHotkeys, _profileSwitch);
        Hotkeys = new HotkeysViewModel(_profileHotkeys, _monitorHotkeys, dialogs, _session);
        PerApp = new PerAppViewModel(_perAppSwitcher, _perAppStore, _session, dialogs, _session);
        Diagnostics = new DiagnosticsViewModel(_session.Daemon, _session, _session);
        WindowsInk = new WindowsInkViewModel(_session, dialogs, _health);
        VMulti = new VMultiViewModel(dialogs, _health);
        // Shared daemon status/control surface for the Home problem card + the Daemon tab.
        _daemonStatus = new DaemonStatusViewModel(_session, () => OpenAdvancedTab(AdvancedTab.Daemon));
        Dashboard = new DashboardViewModel(_session, _daemonStatus, dialogs, NavigateToTabletByName, _health, TabletsOverview,
            () => OpenSettingsTab(SettingsTab.DriverCleanup),
            () => OpenAdvancedTab(AdvancedTab.WindowsInk),
            () => OpenAdvancedTab(AdvancedTab.VMulti),
            () => OpenAdvancedTab(AdvancedTab.CustomTabletConfigs));
        Test = new TestViewModel(_session.Daemon, _session, dialogs);
        Log = new LogViewModel(_session.Daemon, _session);
        Plugins = new PluginsViewModel(_session, _session);
        Daemon = new DaemonViewModel(_daemonStatus);
        // Developer page: its "introduce a real config error" commands act on the live tablet settings.
        Developer = new DeveloperViewModel(_session, _session);

        // The ADVANCED tabbed page groups the driver/daemon subpages behind one sidebar node, with its own
        // subpage navigation (tab rail, like a tablet's page). It shares the sub-view models built above.
        Advanced = new AdvancedViewModel(Daemon, WindowsInk, Configs, Diagnostics, Log, Plugins,
            VMulti);
        // The SETTINGS tabbed page holds OTA's own preference subpages, sharing the same VM instances,
        // behind its own sidebar node in front of ADVANCED. Presets + Per-App Presets (#571) and Developer
        // (#572) are folded in as tabs — Per-App is feature-gated, Developer is gated by the Dev Tools toggle.
        Settings = new SettingsViewModel(Startup, Hotkeys, Theme, DevTools, Shortcut, DriverCleanup,
            Presets, PerApp, Developer);

        // The single TABLET page (#542): a switcher dropdown over the selected tablet's headerless detail
        // view. It resolves detail VMs through the shell (which owns the per-tablet cache + daemon plumbing).
        TabletPage = new TabletPageViewModel(ResolveTabletDetail);

        // The flat sidebar leaves between HOME and ADVANCED, as data (#477). Presets + Per-App Presets moved
        // into SETTINGS tabs (#571). Selection is synced in OnCurrentPageChanged.
        NavLeaves.Add(new NavLeafViewModel("SCRIBBLE", Test));
        NavLeaves.Add(new NavLeafViewModel("ABOUT", About));

        // Build the per-tablet nav children now and on every data load (tablets connect/pair/forget).
        _session.DataLoaded += RebuildTablets;
        // Then reconcile any open tablet page with the freshly-loaded settings so an external edit
        // (e.g. via the OTD UX) is picked up. Subscribed after RebuildTablets so it runs on survivors.
        _session.DataLoaded += ReconcileOpenTabletDetails;
        RebuildTablets();

        CurrentPage = Dashboard;

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        // Auto-start the daemon if needed and begin connecting (session owns connect + polling).
        await _session.StartAndConnectAsync();
    }

    [RelayCommand]
    private void Navigate(object page) => CurrentPage = page as ObservableObject;

    /// <summary>Open the ADVANCED tabbed page on a specific tab — the deep-link target for health-issue
    /// "Fix" buttons (Windows Ink, VMulti, Driver Cleanup) and the Home daemon card's "Open daemon page".</summary>
    private void OpenAdvancedTab(AdvancedTab tab)
    {
        Advanced.SelectedTab = tab;
        Navigate(Advanced);
    }

    /// <summary>Open the SETTINGS tabbed page on a specific tab (the command-palette deep-link target).</summary>
    private void OpenSettingsTab(SettingsTab tab)
    {
        Settings.SelectedTab = tab;
        Navigate(Settings);
    }

    /// <summary>The ordered set of pages the "screenshot all pages" developer aid visits (#437): Home,
    /// every visible root nav leaf, and each ADVANCED sub-tab. Each entry is a filename slug + the action
    /// that navigates to it; the caller renders after each and restores the original page.</summary>
    public IReadOnlyList<(string Slug, Action Navigate)> ScreenshotTargets()
    {
        var list = new List<(string, Action)> { ("home", () => CurrentPage = Dashboard) };
        // Each tablet, shown by selecting it on the single TABLET page.
        foreach (var choice in TabletPage.Tablets)
        {
            var name = choice.Name;
            list.Add(($"tablet-{Slugify(name)}", () => NavigateToTabletByName(name)));
        }
        foreach (var leaf in NavLeaves.Where(l => l.IsVisible))
        {
            var page = leaf.Page;
            list.Add((Slugify(leaf.Label), () => CurrentPage = page));
        }
        foreach (AdvancedTab tab in System.Enum.GetValues<AdvancedTab>())
        {
            var t = tab;
            list.Add(($"advanced-{Slugify(t.ToString())}", () => OpenAdvancedTab(t)));
        }
        foreach (SettingsTab tab in System.Enum.GetValues<SettingsTab>())
        {
            var t = tab;
            list.Add(($"settings-{Slugify(t.ToString())}", () => OpenSettingsTab(t)));
        }
        return list;
    }

    private static string Slugify(string s) => s.ToLowerInvariant().Replace(' ', '-');

    /// <summary>Resolve (lazily creating + caching) a tablet's detail VM by name for the TABLET page host
    /// (#542). Returns null if no such profile exists. The VM is headerless — the page owns the header.</summary>
    private TabletDetailViewModel? ResolveTabletDetail(string name)
    {
        if (!_tabletDetails.TryGetValue(name, out var vm))
        {
            var profile = _session.CurrentSettings?.Profiles.FirstOrDefault(p => p.Tablet == name);
            if (profile == null) return null;
            vm = _dialogs.CreateTabletDetail(profile, () => ForgetTabletByNameAsync(name),
                () => OpenAdvancedTab(AdvancedTab.CustomTabletConfigs));
            _tabletDetails[name] = vm;
        }
        vm.ShowHeader = false;
        return vm;
    }

    /// <summary>Open the TABLET page on a specific tablet (the Home cards, a health-issue "Fix" deep-link),
    /// optionally deep-linking to one of its tabs.</summary>
    private void NavigateToTabletByName(string name, TabletDetailTab? tab = null)
    {
        TabletPage.Select(name, tab);
        CurrentPage = TabletPage;
    }

    private async Task ForgetTabletByNameAsync(string name)
    {
        var settings = _session.CurrentSettings;
        var profile = settings?.Profiles.FirstOrDefault(p => p.Tablet == name);
        if (settings != null && profile != null)
        {
            settings.Profiles.Remove(profile);
            // Reload rebuilds the list + prunes the VM; the TABLET page reselects a survivor (or shows the
            // no-tablet placeholder) in RebuildTablets.
            await _session.ApplyAndSaveSettingsAsync(settings);
        }
    }

    /// <summary>Reconcile the TABLET page's switcher list + cached page VMs with the session's tablets.</summary>
    private void RebuildTablets()
    {
        var ordered = _session.Profiles
            .OrderByDescending(p => p.IsDetected)
            .ThenByDescending(p => p.LastSeen ?? DateTime.MinValue)
            .ThenBy(p => p.Tablet, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = new HashSet<string>(ordered.Select(p => p.Tablet), StringComparer.OrdinalIgnoreCase);

        // Drop cached detail VMs for tablets that are gone; the TABLET page reselects a survivor below.
        foreach (var key in _tabletDetails.Keys.Where(k => !names.Contains(k)).ToList())
        {
            _tabletDetails[key].Dispose();
            _tabletDetails.Remove(key);
        }

        // Rebuild the richer overview rows (Home cards, same order) — status, last-seen, specs, navigable (#307).
        var specByName = new Dictionary<string, DetectedTablet>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _session.DetectedTablets) specByName[d.Name] = d;
        TabletsOverview.Tablets = ordered
            .Select(p =>
            {
                specByName.TryGetValue(p.Tablet, out var d);
                var specs = d != null ? $"{d.Area} · {d.Pressure} pressure levels · {d.Buttons} buttons" : "";
                return new TabletOverviewItemViewModel(p.Tablet, p.IsDetected, p.StatusText,
                    p.LastSeenDetail, specs, () => NavigateToTabletByName(p.Tablet));
            })
            .ToList();
        TabletsOverview.HasTablets = ordered.Count > 0;

        // Refresh the TABLET page's switcher + default selection (first detected / last-used, #542). Runs
        // after the prune so a reselection resolves a fresh (surviving) detail VM.
        TabletPage.SetTablets(ordered.Select(p => (p.Tablet, p.IsDetected)).ToList());
    }

    /// <summary>After each session data load, reconcile any cached tablet page with the freshly-loaded
    /// settings so a change made outside OTA (e.g. in the OTD UX) is picked up rather than showing stale
    /// values. Runs after <see cref="RebuildTablets"/>, which has already dropped VMs for gone tablets.</summary>
    private void ReconcileOpenTabletDetails()
    {
        var settings = _session.CurrentSettings;
        if (settings == null) return;
        foreach (var (name, vm) in _tabletDetails)
        {
            var profile = settings.Profiles.FirstOrDefault(p =>
                string.Equals(p.Tablet, name, StringComparison.OrdinalIgnoreCase));
            vm.ReconcileExternalChange(settings, profile);
        }
    }

    // Throttle so rapid focus flicker doesn't spam the daemon; the reload itself is coalesced anyway.
    private long _lastActivationReloadTick;

    /// <summary>The window regained focus — re-pull settings so an external edit made while we were in
    /// the background (e.g. the user changed the mapping in the OTD UX and alt-tabbed back) is reflected
    /// promptly instead of waiting for the ~30s fallback poll. The reload flows through the session's
    /// DataLoaded, which runs <see cref="ReconcileOpenTabletDetails"/>.</summary>
    public void OnWindowActivated()
    {
        if (!_session.IsConnected) return;
        var now = Environment.TickCount64;
        if (now - _lastActivationReloadTick < 750) return;
        _lastActivationReloadTick = now;
        _ = _session.ReloadAsync();
    }

    partial void OnCurrentPageChanged(ObservableObject? oldValue, ObservableObject? newValue)
    {
        // Stop the debug stream when leaving the ADVANCED tabbed page (which hosts Diagnostics).
        // The tabbed-page view also stops it on tab-switch; this covers navigating away entirely.
        if (ReferenceEquals(oldValue, Advanced) && !ReferenceEquals(newValue, Advanced))
            _ = Diagnostics.StopDebuggingAsync();

        // Start/stop the Test page's driver-input source so the daemon debug stream is only on
        // while Test is visible (same lifecycle treatment as Diagnostics).
        if (ReferenceEquals(newValue, Test) && !ReferenceEquals(oldValue, Test))
            _ = Test.ActivateAsync();
        else if (ReferenceEquals(oldValue, Test) && !ReferenceEquals(newValue, Test))
            _ = Test.DeactivateAsync();

        // Rescan the Hotkeys + Per-App snapshot lists when SETTINGS opens — both are tabs there now
        // (#571) — so a snapshot saved on the Presets page shows up in their pickers.
        if (ReferenceEquals(newValue, Settings))
        {
            _ = Hotkeys.LoadAsync();
            _ = PerApp.LoadAsync();
        }

        // Refresh the sidebar highlight (the IsXxx getters derive from CurrentPage).
        foreach (var leaf in NavLeaves)
            leaf.IsSelected = ReferenceEquals(CurrentPage, leaf.Page);
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsTabletPage));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsAdvanced));
        OnPropertyChanged(nameof(ContentScrollBarVisibility));
    }

    public void Dispose()
    {
        _session.DataLoaded -= RebuildTablets;
        _session.DataLoaded -= ReconcileOpenTabletDetails;
        _autoMapper.Dispose();    // unsubscribes DataLoaded (first-detection auto-mapping)
        _winInkAutoSetup.Dispose(); // unsubscribes DataLoaded (Windows Ink auto-setup)
        _daemonStatus.Dispose();  // unsubscribes from session PropertyChanged
        foreach (var vm in _tabletDetails.Values) vm.Dispose(); // unsubscribe per-tablet detection
        Diagnostics.Dispose();    // stops debugging + unsubscribes connection sync
        Dashboard.Dispose();      // cancels VMulti install/uninstall token + unsubscribes
        Presets.Dispose();        // unsubscribes DataLoaded
        Hotkeys.Dispose();        // unsubscribes DataLoaded
        PerApp.Dispose();         // unsubscribes DataLoaded + spike events
        Test.Dispose();           // stops the daemon debug stream if running
        Log.Dispose();        // unsubscribes the daemon log stream + connection sync
        Plugins.Dispose();        // unsubscribes DataLoaded
        WindowsInk.Dispose();     // unsubscribes DataLoaded + connection sync
        VMulti.Dispose();         // cancels the VMulti install/uninstall token
        _session.Dispose();       // cancels the connect/poll loops, disposes the daemon client + load gate
        DriverCleanup.Dispose();
        _conflicts.Dispose();
        _health.Dispose();        // unsubscribes from DataLoaded + connection changes
        _profileHotkeys.Dispose(); // drops its registrations + event hook (shared service, not disposed here)
        _monitorHotkeys.Dispose(); // drops its registration + event hook
        _globalHotkeys.Dispose();  // destroys the shared message-only hotkey window
        _perAppSwitcher.Dispose(); // stops the foreground watcher + pen stream
    }
}

/// <summary>A flat leaf node in the page navigation bar (#477): a label, the page it opens, a selection
/// flag the sidebar highlights (synced by the shell on navigation), and a visibility flag (feature-gated
/// entries hide themselves). Clicking it runs the shell's Navigate command with <see cref="Page"/>.</summary>
public partial class NavLeafViewModel : ObservableObject
{
    public NavLeafViewModel(string label, ObservableObject page, bool isVisible = true)
    {
        Label = label;
        Page = page;
        _isVisible = isVisible;
    }

    public string Label { get; }
    public ObservableObject Page { get; }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible;
}

public record ConfigurationItem(string Name, string FileName, string Path, string SizeText);

/// <summary>
/// View-model record for a settings snapshot file shown in the Saved Settings list.
/// Plain-property record so Avalonia bindings can resolve Name/LastModified directly
/// (JObject indexer bindings stopped rendering for TextBlock.Text in Avalonia 12).
/// </summary>
public record PresetInfo(string Name, string Path, string Content, string LastModified);
