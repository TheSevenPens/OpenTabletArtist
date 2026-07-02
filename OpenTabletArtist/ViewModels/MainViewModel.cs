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
    private readonly IDialogService _dialogs;
    private readonly DriverConflictMonitor _conflicts;

    // One cached settings VM per tablet (heavy: holds subscriptions). Reconciled on each data load.
    private readonly Dictionary<string, TabletDetailViewModel> _tabletDetails = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedTabletName;

    /// <summary>Daemon connection state + Start/Stop/Restart commands — surfaced for the tray menu (#72).</summary>
    public IConnectionState Connection => _session;

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
    public DiagnosticsViewModel Diagnostics { get; }
    public DashboardViewModel Dashboard { get; }
    public TestViewModel Test { get; }
    public LogViewModel Log { get; }
    public PluginsViewModel Plugins { get; }
    public DaemonViewModel Daemon { get; }
    public ThemeViewModel Theme { get; } = new();

    /// <summary>Landing page for the Tablets group (header click / nothing selected).</summary>
    public TabletsOverviewViewModel TabletsOverview { get; } = new();

    /// <summary>The per-tablet nav children — paired ∪ connected, ordered like the old list (detected
    /// first, then most-recently-seen). Rebuilt on each data load; clicking one shows its page.</summary>
    public ObservableCollection<TabletNavItemViewModel> Tablets { get; } = new();
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
        ReferenceEquals(page, Daemon) || ReferenceEquals(page, Configs)
        || ReferenceEquals(page, Diagnostics) || ReferenceEquals(page, Log)
        || ReferenceEquals(page, Plugins) || ReferenceEquals(page, DriverCleanup)
        || ReferenceEquals(page, Theme);

    // Sidebar highlight: each nav button binds IsChecked to one of these (converter-free).
    public bool IsDashboard => ReferenceEquals(CurrentPage, Dashboard);
    public bool IsTabletsOverview => ReferenceEquals(CurrentPage, TabletsOverview);
    public bool IsPresets => ReferenceEquals(CurrentPage, Presets);
    public bool IsConfigs => ReferenceEquals(CurrentPage, Configs);
    public bool IsDriverCleanup => ReferenceEquals(CurrentPage, DriverCleanup);
    public bool IsDiagnostics => ReferenceEquals(CurrentPage, Diagnostics);
    public bool IsTest => ReferenceEquals(CurrentPage, Test);
    public bool IsLog => ReferenceEquals(CurrentPage, Log);
    public bool IsPlugins => ReferenceEquals(CurrentPage, Plugins);
    public bool IsDaemon => ReferenceEquals(CurrentPage, Daemon);
    public bool IsTheme => ReferenceEquals(CurrentPage, Theme);
    public bool IsAbout => ReferenceEquals(CurrentPage, About);

    public MainViewModel()
    {
        _session = new AppSession(new DaemonClient(), new DaemonLifecycleService(), _settingsStore);
        var dialogs = new DialogService(_session);
        _dialogs = dialogs;

        // Conflicting-driver detection (#245), shared by the Driver cleanup page and the Home alert.
        _conflicts = new DriverConflictMonitor(_session.Daemon, _session);

        // Page VMs depend on the session through its role interfaces and on IDialogService,
        // and self-subscribe to the session's data load / connection state.
        DriverCleanup = new DriverCleanupViewModel(dialogs, _conflicts);
        Configs = new CustomTabletConfigsViewModel(dialogs, new ConfigurationsDirectoryProvider());
        Presets = new PresetsViewModel(_settingsStore, _session, _session, dialogs);
        Diagnostics = new DiagnosticsViewModel(_session.Daemon, _session, _session);
        Dashboard = new DashboardViewModel(_session, dialogs, NavigateToTabletByName, _conflicts, () => Navigate(DriverCleanup));
        Test = new TestViewModel(_session.Daemon, _session, dialogs);
        Log = new LogViewModel(_session.Daemon, _session);
        Plugins = new PluginsViewModel(_session, _session);
        Daemon = new DaemonViewModel(_session);

        // Build the per-tablet nav children now and on every data load (tablets connect/pair/forget).
        _session.DataLoaded += RebuildTablets;
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

    /// <summary>Navigate to a tablet's settings page (lazily creating + caching its VM).</summary>
    [RelayCommand]
    private void NavigateToTablet(TabletNavItemViewModel item)
    {
        if (!_tabletDetails.TryGetValue(item.Name, out var vm))
        {
            var profile = _session.CurrentSettings?.Profiles.FirstOrDefault(p => p.Tablet == item.Name);
            if (profile == null) { CurrentPage = TabletsOverview; return; }
            vm = _dialogs.CreateTabletDetail(profile, () => ForgetTabletByNameAsync(item.Name));
            _tabletDetails[item.Name] = vm;
        }
        CurrentPage = vm;
    }

    private void NavigateToTabletByName(string name)
    {
        var item = Tablets.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item != null) NavigateToTablet(item);
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
            CurrentPage = TabletsOverview;
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
            if (ReferenceEquals(CurrentPage, vm)) CurrentPage = TabletsOverview;
            vm.Dispose();
        }

        // Rebuild the lightweight nav-item list (order shifts as detection/last-seen change).
        Tablets.Clear();
        foreach (var p in ordered)
            Tablets.Add(new TabletNavItemViewModel(p.Tablet, p.IsDetected,
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

    private void UpdateTabletSelection()
    {
        _selectedTabletName = _tabletDetails.FirstOrDefault(kv => ReferenceEquals(kv.Value, CurrentPage)).Key;
        foreach (var item in Tablets)
            item.IsSelected = string.Equals(item.Name, _selectedTabletName, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnCurrentPageChanged(ObservableObject? oldValue, ObservableObject? newValue)
    {
        // Stop the debug stream when leaving the Diagnostics page.
        if (ReferenceEquals(oldValue, Diagnostics) && !ReferenceEquals(newValue, Diagnostics))
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

        // Highlight the selected tablet child (or none) in the sidebar.
        UpdateTabletSelection();

        // Refresh the sidebar highlight (the IsXxx getters derive from CurrentPage).
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsTabletsOverview));
        OnPropertyChanged(nameof(IsPresets));
        OnPropertyChanged(nameof(IsConfigs));
        OnPropertyChanged(nameof(IsDriverCleanup));
        OnPropertyChanged(nameof(IsDiagnostics));
        OnPropertyChanged(nameof(IsTest));
        OnPropertyChanged(nameof(IsLog));
        OnPropertyChanged(nameof(IsPlugins));
        OnPropertyChanged(nameof(IsDaemon));
        OnPropertyChanged(nameof(IsTheme));
        OnPropertyChanged(nameof(IsAbout));
    }

    public void Dispose()
    {
        _session.DataLoaded -= RebuildTablets;
        foreach (var vm in _tabletDetails.Values) vm.Dispose(); // unsubscribe per-tablet detection
        Diagnostics.Dispose();    // stops debugging + unsubscribes connection sync
        Dashboard.Dispose();      // cancels VMulti install/uninstall token + unsubscribes
        Presets.Dispose();        // unsubscribes DataLoaded
        Test.Dispose();           // stops the daemon debug stream if running
        Log.Dispose();        // unsubscribes the daemon log stream + connection sync
        Plugins.Dispose();        // unsubscribes DataLoaded
        _session.Dispose();       // cancels the connect/poll loops, disposes the daemon client + load gate
        DriverCleanup.Dispose();
        _conflicts.Dispose();
    }
}

/// <summary>One tablet in the sidebar's Tablets group — name + live connection status + selection.
/// Forget is a command on the item itself (a right-click context menu lives in a popup where an
/// ancestor binding back to the shell isn't reliable).</summary>
public partial class TabletNavItemViewModel : ObservableObject
{
    private readonly Func<TabletNavItemViewModel, Task> _forget;

    public TabletNavItemViewModel(string name, bool isDetected, Func<TabletNavItemViewModel, Task> forget)
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
