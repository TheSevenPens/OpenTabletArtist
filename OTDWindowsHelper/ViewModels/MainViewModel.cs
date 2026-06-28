using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

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

    /// <summary>Daemon connection state + Start/Stop/Restart commands — surfaced for the tray menu (#72).</summary>
    public IConnectionState Connection => _session;

    /// <summary>App version for the sidebar footer — read from the assembly so it never drifts (the
    /// release workflow stamps the tag version at build).</summary>
    public string AppVersion { get; } = AppVersionInfo.Format(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public AboutViewModel About { get; } = new();
    public UtilitiesViewModel Utilities { get; }
    public CustomTabletConfigsViewModel Configs { get; }
    public PresetsViewModel Presets { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public TabletSettingsViewModel TabletSettings { get; }
    public DashboardViewModel Dashboard { get; }
    public TestViewModel Test { get; }
    public PluginsViewModel Plugins { get; }
    public SettingsViewModel Settings { get; } = new();

    // The active page is the VM instance itself (typed navigation, #15). The content host
    // resolves it to a view via DataTemplates keyed by VM type, so there's no page-name string,
    // no view-lookup converter, and no per-view DataContext re-point.
    [ObservableProperty] private ObservableObject? _currentPage;

    // Sidebar highlight: each nav button binds IsChecked to one of these (converter-free).
    public bool IsDashboard => ReferenceEquals(CurrentPage, Dashboard);
    public bool IsTabletSettings => ReferenceEquals(CurrentPage, TabletSettings);
    public bool IsPresets => ReferenceEquals(CurrentPage, Presets);
    public bool IsConfigs => ReferenceEquals(CurrentPage, Configs);
    public bool IsUtilities => ReferenceEquals(CurrentPage, Utilities);
    public bool IsDiagnostics => ReferenceEquals(CurrentPage, Diagnostics);
    public bool IsTest => ReferenceEquals(CurrentPage, Test);
    public bool IsPlugins => ReferenceEquals(CurrentPage, Plugins);
    public bool IsSettings => ReferenceEquals(CurrentPage, Settings);
    public bool IsAbout => ReferenceEquals(CurrentPage, About);

    public MainViewModel()
    {
        _session = new AppSession(new DaemonClient(), new DaemonLifecycleService(), _settingsStore);
        var dialogs = new DialogService(_session);

        // Page VMs depend on the session through its role interfaces and on IDialogService,
        // and self-subscribe to the session's data load / connection state.
        Utilities = new UtilitiesViewModel(dialogs);
        Configs = new CustomTabletConfigsViewModel(dialogs, new ConfigurationsDirectoryProvider());
        Presets = new PresetsViewModel(_settingsStore, _session, _session, dialogs);
        Diagnostics = new DiagnosticsViewModel(_session.Daemon, _session);
        TabletSettings = new TabletSettingsViewModel(_session, _session, dialogs);
        Dashboard = new DashboardViewModel(_session, dialogs);
        Test = new TestViewModel(_session.Daemon, _session, dialogs);
        Plugins = new PluginsViewModel(_session, _session);

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

        // Refresh the sidebar highlight (the IsXxx getters derive from CurrentPage).
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsTabletSettings));
        OnPropertyChanged(nameof(IsPresets));
        OnPropertyChanged(nameof(IsConfigs));
        OnPropertyChanged(nameof(IsUtilities));
        OnPropertyChanged(nameof(IsDiagnostics));
        OnPropertyChanged(nameof(IsTest));
        OnPropertyChanged(nameof(IsPlugins));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsAbout));
    }

    public void Dispose()
    {
        Diagnostics.Dispose();    // stops debugging + unsubscribes connection sync
        Dashboard.Dispose();      // cancels VMulti install/uninstall token + unsubscribes
        TabletSettings.Dispose(); // unsubscribes DataLoaded
        Presets.Dispose();        // unsubscribes DataLoaded
        Test.Dispose();           // stops the daemon debug stream if running
        Plugins.Dispose();        // unsubscribes DataLoaded
        _session.Dispose();       // cancels the connect/poll loops, disposes the daemon client + load gate
        Utilities.Dispose();
    }
}

public record ConfigurationItem(string Name, string FileName, string Path, string SizeText);

/// <summary>
/// View-model record for a settings snapshot file shown in the Saved Settings list.
/// Plain-property record so Avalonia bindings can resolve Name/LastModified directly
/// (JObject indexer bindings stopped rendering for TextBlock.Text in Avalonia 12).
/// </summary>
public record PresetInfo(string Name, string Path, string Content, string LastModified);
