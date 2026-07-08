using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private string? _selectedTabletName;

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
    /// <summary>The OpenTabletDriver tabbed page (Daemon / Windows Ink / Configs / Diagnostics / Log / Plugins tabs).</summary>
    public OpenTabletDriverViewModel OpenTabletDriver { get; }
    public StartupViewModel Startup { get; } = new();
    /// <summary>Assigned in the constructor (not a field initializer) so its break-config commands can
    /// reach the session's settings coordinator + device data.</summary>
    public DeveloperViewModel Developer { get; }
    public ThemeViewModel Theme { get; } = new();

    /// <summary>Tablets list + supported-tablets link, now rendered as a section of Home (the standalone
    /// Tablets page was merged in). Populated by <see cref="RebuildTablets"/> on each data load.</summary>
    public TabletsOverviewViewModel TabletsOverview { get; } = new();

    /// <summary>The per-tablet child nodes under the Tablets parent node — paired ∪ connected, ordered
    /// like the old list (detected first, then most-recently-seen). Rebuilt on each data load; clicking
    /// one shows its page.</summary>
    public ObservableCollection<TabletNavNodeViewModel> Tablets { get; } = new();
    public bool HasTablets => Tablets.Count > 0;

    // The active page is the VM instance itself (typed navigation, #15). The content host
    // resolves it to a view via DataTemplates keyed by VM type, so there's no page-name string,
    // no view-lookup converter, and no per-view DataContext re-point.
    [ObservableProperty] private ObservableObject? _currentPage;

    /// <summary>Whether the sidebar's collapsible "ADVANCED" group is expanded. Collapsed by default;
    /// auto-expands when one of its pages becomes active so the highlight is visible.</summary>
    [ObservableProperty] private bool _isAdvancedExpanded;

    /// <summary>The pages tucked under the ADVANCED group.</summary>
    private bool IsAdvancedPage(object? page) =>
        ReferenceEquals(page, OpenTabletDriver) || ReferenceEquals(page, VMulti)
        || ReferenceEquals(page, DriverCleanup) || ReferenceEquals(page, Startup)
        || ReferenceEquals(page, Developer) || ReferenceEquals(page, Theme);

    // Sidebar highlight: each nav button binds IsChecked to one of these (converter-free).
    public bool IsDashboard => ReferenceEquals(CurrentPage, Dashboard);
    public bool IsPresets => ReferenceEquals(CurrentPage, Presets);
    public bool IsHotkeys => ReferenceEquals(CurrentPage, Hotkeys);
    public bool IsPerApp => ReferenceEquals(CurrentPage, PerApp);
    public bool IsDriverCleanup => ReferenceEquals(CurrentPage, DriverCleanup);
    public bool IsTest => ReferenceEquals(CurrentPage, Test);
    // Daemon / Windows Ink / Configs / Diagnostics / Log / Plugins are tabs inside the tabbed page now,
    // so the single OpenTabletDriver node drives the sidebar highlight instead of a per-page flag.
    public bool IsOtd => ReferenceEquals(CurrentPage, OpenTabletDriver);
    public bool IsVMulti => ReferenceEquals(CurrentPage, VMulti);
    public bool IsStartup => ReferenceEquals(CurrentPage, Startup);
    public bool IsDeveloper => ReferenceEquals(CurrentPage, Developer);
    public bool IsTheme => ReferenceEquals(CurrentPage, Theme);
    public bool IsAbout => ReferenceEquals(CurrentPage, About);

    public MainViewModel()
    {
        _session = new AppSession(new DaemonClient(), new DaemonLifecycleService(), _settingsStore);
        var dialogs = new DialogService(_session);
        _dialogs = dialogs;

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
        Configs = new CustomTabletConfigsViewModel(dialogs, new ConfigurationsDirectoryProvider());
        Presets = new PresetsViewModel(_settingsStore, _session, _session, dialogs, _profileHotkeys, _profileSwitch);
        Hotkeys = new HotkeysViewModel(_profileHotkeys, _monitorHotkeys, dialogs, _session);
        PerApp = new PerAppViewModel(_perAppSwitcher, _perAppStore, _session, dialogs, _session);
        Diagnostics = new DiagnosticsViewModel(_session.Daemon, _session, _session);
        WindowsInk = new WindowsInkViewModel(_session, dialogs, _health);
        VMulti = new VMultiViewModel(dialogs, _health);
        // Shared daemon status/control surface for the Home problem card + the Daemon page.
        _daemonStatus = new DaemonStatusViewModel(_session, OpenDaemonPage);
        Dashboard = new DashboardViewModel(_session, _daemonStatus, dialogs, NavigateToTabletByName, _health, TabletsOverview,
            () => Navigate(DriverCleanup), OpenWindowsInk, () => Navigate(VMulti));
        Test = new TestViewModel(_session.Daemon, _session, dialogs);
        Log = new LogViewModel(_session.Daemon, _session);
        Plugins = new PluginsViewModel(_session, _session);
        Daemon = new DaemonViewModel(_daemonStatus);
        // Developer page: its "introduce a real config error" commands act on the live tablet settings.
        Developer = new DeveloperViewModel(_session, _session);

        // The "OpenTabletDriver" tabbed page groups the subpages behind one sidebar node, with its own
        // subpage navigation (tab rail, like a tablet's page). It shares the sub-view models built above.
        OpenTabletDriver = new OpenTabletDriverViewModel(Daemon, WindowsInk, Configs, Diagnostics, Log, Plugins);

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

    // Deep-link to the Windows Ink tab of the OpenTabletDriver tabbed page (a health-issue "Fix" target).
    private void OpenWindowsInk()
    {
        OpenTabletDriver.SelectedTab = OtdTab.WindowsInk;
        Navigate(OpenTabletDriver);
    }

    // Deep-link to the Daemon tab of the OpenTabletDriver tabbed page (the Home daemon card's "Open daemon page").
    private void OpenDaemonPage()
    {
        OpenTabletDriver.SelectedTab = OtdTab.Daemon;
        Navigate(OpenTabletDriver);
    }

    /// <summary>Navigate to a tablet's settings page (lazily creating + caching its VM).</summary>
    [RelayCommand]
    private void NavigateToTablet(TabletNavNodeViewModel item) => ShowTablet(item, null);

    /// <summary>Show a tablet's page, optionally deep-linking to a specific tab (a health-issue "Fix").</summary>
    private void ShowTablet(TabletNavNodeViewModel item, TabletDetailTab? tab)
    {
        if (!_tabletDetails.TryGetValue(item.Name, out var vm))
        {
            var profile = _session.CurrentSettings?.Profiles.FirstOrDefault(p => p.Tablet == item.Name);
            if (profile == null) { CurrentPage = Dashboard; return; }
            vm = _dialogs.CreateTabletDetail(profile, () => ForgetTabletByNameAsync(item.Name));
            _tabletDetails[item.Name] = vm;
        }
        // Request the tab before showing the page: a fresh page consumes it on attach; an already-open
        // one switches via the VM's TabRequested event (re-navigation wouldn't re-attach the view).
        if (tab is { } t) vm.RequestTab(t);
        CurrentPage = vm;
    }

    private void NavigateToTabletByName(string name, TabletDetailTab? tab = null)
    {
        var item = Tablets.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item != null) ShowTablet(item, tab);
    }

    private async Task ForgetTabletByNameAsync(string name)
    {
        var settings = _session.CurrentSettings;
        var profile = settings?.Profiles.FirstOrDefault(p => p.Tablet == name);
        if (settings != null && profile != null)
        {
            settings.Profiles.Remove(profile);
            await _session.ApplyAndSaveSettingsAsync(settings); // reload rebuilds the list + prunes the VM
        }
        if (string.Equals(_selectedTabletName, name, StringComparison.OrdinalIgnoreCase))
            CurrentPage = Dashboard;
    }

    /// <summary>Reconcile the per-tablet nav children + cached page VMs with the session's tablets.</summary>
    private void RebuildTablets()
    {
        var ordered = _session.Profiles
            .OrderByDescending(p => p.IsDetected)
            .ThenByDescending(p => p.LastSeen ?? DateTime.MinValue)
            .ThenBy(p => p.Tablet, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = new HashSet<string>(ordered.Select(p => p.Tablet), StringComparer.OrdinalIgnoreCase);

        // Drop cached detail VMs for tablets that are gone; if one was open, fall back to the overview.
        foreach (var key in _tabletDetails.Keys.Where(k => !names.Contains(k)).ToList())
        {
            var vm = _tabletDetails[key];
            _tabletDetails.Remove(key);
            if (ReferenceEquals(CurrentPage, vm)) CurrentPage = Dashboard;
            vm.Dispose();
        }

        // Rebuild the lightweight nav-item list (order shifts as detection/last-seen change).
        Tablets.Clear();
        foreach (var p in ordered)
            Tablets.Add(new TabletNavNodeViewModel(p.Tablet, p.IsDetected,
                item => ForgetTabletByNameAsync(item.Name)));

        // Rebuild the richer overview rows (same order) — status, last-seen, specs, navigable (#307).
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

        OnPropertyChanged(nameof(HasTablets));
        TabletsOverview.HasTablets = ordered.Count > 0;
        UpdateTabletSelection();
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

    private void UpdateTabletSelection()
    {
        _selectedTabletName = _tabletDetails.FirstOrDefault(kv => ReferenceEquals(kv.Value, CurrentPage)).Key;
        foreach (var item in Tablets)
            item.IsSelected = string.Equals(item.Name, _selectedTabletName, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnCurrentPageChanged(ObservableObject? oldValue, ObservableObject? newValue)
    {
        // Stop the debug stream when leaving the OpenTabletDriver tabbed page (which hosts Diagnostics).
        // The tabbed-page view also stops it on tab-switch; this covers navigating away entirely.
        if (ReferenceEquals(oldValue, OpenTabletDriver) && !ReferenceEquals(newValue, OpenTabletDriver))
            _ = Diagnostics.StopDebuggingAsync();

        // Start/stop the Test page's driver-input source so the daemon debug stream is only on
        // while Test is visible (same lifecycle treatment as Diagnostics).
        if (ReferenceEquals(newValue, Test) && !ReferenceEquals(oldValue, Test))
            _ = Test.ActivateAsync();
        else if (ReferenceEquals(oldValue, Test) && !ReferenceEquals(newValue, Test))
            _ = Test.DeactivateAsync();

        // Keep the ADVANCED group open when navigating to one of its pages (e.g. from the tray or
        // programmatically), so the active item isn't hidden behind a collapsed group.
        if (IsAdvancedPage(newValue)) IsAdvancedExpanded = true;

        // Rescan the Hotkeys page when opened so a snapshot saved on the Saved Settings page shows here.
        if (ReferenceEquals(newValue, Hotkeys)) _ = Hotkeys.LoadAsync();
        // Same for the Per-App spike page's snapshot pickers.
        if (ReferenceEquals(newValue, PerApp)) _ = PerApp.LoadAsync();

        // Highlight the selected tablet child (or none) in the sidebar.
        UpdateTabletSelection();

        // Refresh the sidebar highlight (the IsXxx getters derive from CurrentPage).
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsPresets));
        OnPropertyChanged(nameof(IsHotkeys));
        OnPropertyChanged(nameof(IsPerApp));
        OnPropertyChanged(nameof(IsDriverCleanup));
        OnPropertyChanged(nameof(IsTest));
        OnPropertyChanged(nameof(IsOtd));
        OnPropertyChanged(nameof(IsVMulti));
        OnPropertyChanged(nameof(IsStartup));
        OnPropertyChanged(nameof(IsDeveloper));
        OnPropertyChanged(nameof(IsTheme));
        OnPropertyChanged(nameof(IsAbout));
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

/// <summary>A child node under the Tablets parent node in the page navigation bar (see
/// docs/design/ux-terminology.md) — name + live connection status + selection. Forget is a command on
/// the node itself (a right-click context menu lives in a popup where an ancestor binding back to the
/// shell isn't reliable).</summary>
public partial class TabletNavNodeViewModel : ObservableObject
{
    private readonly Func<TabletNavNodeViewModel, Task> _forget;

    public TabletNavNodeViewModel(string name, bool isDetected, Func<TabletNavNodeViewModel, Task> forget)
    {
        Name = name;
        _isDetected = isDetected;
        _forget = forget;
    }

    public string Name { get; }
    [ObservableProperty] private bool _isDetected;
    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private Task Forget() => _forget(this);
}

public record ConfigurationItem(string Name, string FileName, string Path, string SizeText);

/// <summary>
/// View-model record for a settings snapshot file shown in the Saved Settings list.
/// Plain-property record so Avalonia bindings can resolve Name/LastModified directly
/// (JObject indexer bindings stopped rendering for TextBlock.Text in Avalonia 12).
/// </summary>
public record PresetInfo(string Name, string Path, string Content, string LastModified);
