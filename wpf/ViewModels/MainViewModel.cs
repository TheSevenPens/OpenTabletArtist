using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OtdWindowsHelper.Concurrency;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Helpers;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    // Shared application session — owns the daemon connection + lifecycle (Option C, #41).
    private readonly AppSession _session = new(new DaemonClient(), new DaemonLifecycleService());
    private readonly ISettingsFileStore _settingsStore = new SettingsFileStore();
    private readonly VMultiDetector _vmulti = new();
    private readonly VMultiInstaller _vmultiInstaller = new();
    private readonly WindowsInkPluginService _winInk = new();
    private readonly CancellationTokenSource _cts = new();
    // Ensures only the latest data load applies (Connected handler, 3s poll, Refresh). See #19.
    private readonly LatestOnlyGate _loadGate = new();

    /// <summary>Page view models composed by this shell (page-VM split, #14 phase 2).</summary>
    public AboutViewModel About { get; } = new();
    public UtilitiesViewModel Utilities { get; } = new();
    public CustomTabletConfigsViewModel Configs { get; } = new();
    public PresetsViewModel Presets { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public TabletSettingsViewModel TabletSettings { get; }

    [ObservableProperty] private string _currentPage = "Dashboard";

    // Connection state forwards to the shared AppSession (Option C, #41). The Dashboard and
    // Diagnostics views still bind these names on the shell; the shell mirrors the session's
    // PropertyChanged (see ctor) so those bindings update.
    public IConnectionState Connection => _session;
    public bool IsConnected => _session.IsConnected;
    public string ConnectionStatus => _session.ConnectionStatus;
    public bool IsDaemonRunning => _session.IsDaemonRunning;
    public bool IsAppOwnedDaemon => _session.IsAppOwnedDaemon;
    public bool IsForeignDaemon => _session.IsForeignDaemon;
    public string DaemonSourcePath => _session.DaemonSourcePath;
    public bool ShowAppOwnedDaemon => _session.ShowAppOwnedDaemon;
    public bool ShowForeignDaemonWarning => _session.ShowForeignDaemonWarning;
    public bool ShowDaemonSourceUnknown => _session.ShowDaemonSourceUnknown;
    public bool CanStartDaemon => _session.CanStartDaemon;
    public string DaemonStatusText => _session.DaemonStatusText;

    // Daemon lifecycle commands live on the session; forward for the (still shell-bound) Dashboard.
    public IAsyncRelayCommand StartDaemonCommand => _session.StartDaemonCommand;
    public IRelayCommand StopDaemonCommand => _session.StopDaemonCommand;
    public IAsyncRelayCommand RestartDaemonCommand => _session.RestartDaemonCommand;
    public IRelayCommand LaunchOtdUxCommand => _session.LaunchOtdUxCommand;


    // Dashboard data
    [ObservableProperty] private string _tabletName = "";
    [ObservableProperty] private bool _hasTablet;
    [ObservableProperty] private bool _vmultiInstalled;
    [ObservableProperty] private string _vmultiMessage = "Checking...";
    [ObservableProperty] private string _vmultiHidStatus = "Checking...";
    [ObservableProperty] private string _vmultiSetupApiStatus = "Checking...";
    [ObservableProperty] private bool _vmultiInstalling;
    [ObservableProperty] private string _vmultiInstallStatus = "";
    [ObservableProperty] private bool _hasWindowsInk;

    // Windows Ink plugin (Kuuube's Windows Ink plugin) — install state & versions
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWinInkInstall))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkUninstall))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkCheckUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkInstallUpdate))]
    private bool _winInkInstalled;
    [ObservableProperty] private string _winInkInstallStatusText = "Checking...";
    [ObservableProperty] private string _winInkPluginVersion = "";
    [ObservableProperty] private string _winInkSupportedDriverVersion = "";
    [ObservableProperty] private bool _winInkVersionMismatch;
    // True once an update check found a newer plugin version in the repository.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWinInkCheckUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkInstallUpdate))]
    private bool _winInkUpdateAvailable;
    [ObservableProperty] private string _winInkLatestVersion = "";
    // Short result line shown after a check ("Up to date", etc.).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWinInkUpdateCheckStatus))]
    private string _winInkUpdateCheckStatus = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWinInkInstall))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkUninstall))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkCheckUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkInstallUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkRefresh))]
    private bool _winInkBusy;
    [ObservableProperty] private string _winInkBusyStatus = "";

    // Cached latest-available metadata (for install/update) and the plugin's
    // full directory path on disk (for uninstall).
    private PluginMetadata? _winInkLatest;
    private string _winInkPluginDirectory = "";

    public bool ShowWinInkInstall => !WinInkInstalled && !WinInkBusy;
    public bool ShowWinInkUninstall => WinInkInstalled && !WinInkBusy;
    // When installed, the update button has two states: "Check for Update" until a
    // check finds a newer version, then "Install Update".
    public bool ShowWinInkCheckUpdate => WinInkInstalled && !WinInkUpdateAvailable && !WinInkBusy;
    public bool ShowWinInkInstallUpdate => WinInkInstalled && WinInkUpdateAvailable && !WinInkBusy;
    public bool ShowWinInkRefresh => !WinInkBusy;
    public bool HasWinInkUpdateCheckStatus => !string.IsNullOrEmpty(WinInkUpdateCheckStatus);

    // Computed properties for Avalonia bindings (replacing DataTriggers)
    [ObservableProperty] private string _tabletStatusText = "No tablet detected";
    [ObservableProperty] private string _windowsInkStatusText = "Not configured";
    [ObservableProperty] private bool _showInstallButton;
    [ObservableProperty] private bool _showUninstallButton;

    partial void OnHasTabletChanged(bool value)
    {
        TabletStatusText = value ? $"{TabletName} detected" : "No tablet detected";
    }

    partial void OnTabletNameChanged(string value)
    {
        if (HasTablet) TabletStatusText = $"{value} detected";
    }

    partial void OnHasWindowsInkChanged(bool value)
    {
        WindowsInkStatusText = value ? "Plugin active" : "Plugin not active in current profile";
    }

    private string? WinInkPluginParentDirectory =>
        string.IsNullOrEmpty(_winInkPluginDirectory) ? null : Path.GetDirectoryName(_winInkPluginDirectory);

    /// <summary>
    /// Reads the installed Windows Ink plugin's metadata from disk (cheap, no network)
    /// and updates the install status, displayed versions, and compatibility indicator.
    /// </summary>
    private void RefreshWindowsInkInstalledStatus()
    {
        var installed = _winInk.ReadInstalled(WinInkPluginParentDirectory);

        if (installed == null)
        {
            WinInkInstalled = false;
            WinInkInstallStatusText = "Not installed";
            WinInkPluginVersion = "";
            WinInkSupportedDriverVersion = "";
            WinInkVersionMismatch = false;
            WinInkUpdateAvailable = false;
            WinInkUpdateCheckStatus = "";
            return;
        }

        WinInkInstalled = true;
        WinInkPluginVersion = installed.PluginVersion?.ToString() ?? "?";
        WinInkInstallStatusText = $"v{WinInkPluginVersion} installed";
        WinInkSupportedDriverVersion = installed.SupportedDriverVersion?.ToString() ?? "?";
        // "Mismatch" = the installed plugin does not declare support for the running
        // OTD version (OTD's own IsSupportedBy compatibility rule).
        WinInkVersionMismatch = !WindowsInkPluginService.IsCompatible(installed);

        RecomputeWinInkUpdate();
    }

    /// <summary>
    /// Queries the repository for the newest available release and caches it, then
    /// recomputes whether a newer plugin version exists. Network call.
    /// </summary>
    private async Task FetchLatestWindowsInkAsync()
    {
        _winInkLatest = await _winInk.GetLatestCompatibleAsync();
        WinInkLatestVersion = _winInkLatest?.PluginVersion?.ToString() ?? "";
        RecomputeWinInkUpdate();
    }

    /// <summary>
    /// An update is available when the latest release in the repository has a higher
    /// plugin version than the one currently installed.
    /// </summary>
    private void RecomputeWinInkUpdate()
    {
        if (!WinInkInstalled)
        {
            WinInkUpdateAvailable = false;
            return;
        }

        var installed = _winInk.ReadInstalled(WinInkPluginParentDirectory);
        WinInkUpdateAvailable = WinInkUpdateState.IsUpdateAvailable(
            installed?.PluginVersion, _winInkLatest?.PluginVersion);
    }

    /// <summary>
    /// Explicit "Check for Update" action: fetches the latest release and reports
    /// whether a newer version is available.
    /// </summary>
    [RelayCommand]
    private async Task CheckForWindowsInkUpdate()
    {
        if (WinInkBusy) return;
        WinInkBusy = true;
        WinInkBusyStatus = "Checking for updates...";
        WinInkUpdateCheckStatus = "";
        try
        {
            await FetchLatestWindowsInkAsync();
            WinInkUpdateCheckStatus = WinInkUpdateAvailable
                ? ""
                : _winInkLatest == null
                    ? "Couldn't reach the plugin repository"
                    : $"Up to date (v{WinInkPluginVersion})";
        }
        finally
        {
            WinInkBusy = false;
            WinInkBusyStatus = "";
        }
    }

    /// <summary>
    /// Refreshes everything shown on the Windows Ink card: re-reads the installed
    /// plugin from disk and re-checks the repository for the latest release.
    /// </summary>
    [RelayCommand]
    private async Task RefreshWindowsInk()
    {
        if (WinInkBusy) return;
        WinInkBusy = true;
        WinInkBusyStatus = "Refreshing...";
        WinInkUpdateCheckStatus = "";
        try
        {
            RefreshWindowsInkInstalledStatus();
            await FetchLatestWindowsInkAsync();
        }
        finally
        {
            WinInkBusy = false;
            WinInkBusyStatus = "";
        }
    }

    [RelayCommand]
    private async Task InstallWindowsInk() => await InstallOrUpgradeWindowsInkAsync(isUpgrade: false);

    [RelayCommand]
    private async Task InstallWindowsInkUpdate() => await InstallOrUpgradeWindowsInkAsync(isUpgrade: true);

    private async Task InstallOrUpgradeWindowsInkAsync(bool isUpgrade)
    {
        if (WinInkBusy) return;
        if (!IsConnected)
        {
            await Dialogs.ShowMessageAsync("Windows Ink Plugin",
                "The OpenTabletDriver daemon isn't connected. Start it first, then try again.");
            return;
        }

        WinInkBusy = true;
        WinInkBusyStatus = isUpgrade ? "Finding latest version..." : "Finding plugin...";
        WinInkUpdateCheckStatus = "";
        try
        {
            // Ensure we have the latest available metadata (re-fetch if a check
            // hasn't run yet).
            _winInkLatest ??= await _winInk.GetLatestCompatibleAsync();
            if (_winInkLatest == null)
            {
                await Dialogs.ShowMessageAsync("Windows Ink Plugin",
                    $"Couldn't find a Windows Ink release compatible with OpenTabletDriver v{CurrentOtdVersion}.\n\n" +
                    "Check your internet connection and try again.");
                return;
            }

            WinInkBusyStatus = isUpgrade
                ? $"Installing update v{_winInkLatest.PluginVersion}..."
                : $"Installing v{_winInkLatest.PluginVersion}...";

            var ok = await _session.Daemon.DownloadPluginAsync(_winInkLatest);
            await _session.Daemon.LoadPluginsAsync();

            RefreshWindowsInkInstalledStatus();
            await FetchLatestWindowsInkAsync();

            await Dialogs.ShowMessageAsync("Windows Ink Plugin",
                ok
                    ? $"Windows Ink plugin v{WinInkPluginVersion} is now installed.\n\n" +
                      "Set a tablet's output mode to \"Windows Ink\" to enable pressure and tilt."
                    : "The plugin download did not complete successfully. Check the daemon log for details.");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync("Windows Ink Plugin", $"Error: {ex.Message}");
        }
        finally
        {
            WinInkBusy = false;
            WinInkBusyStatus = "";
        }
    }

    [RelayCommand]
    private async Task UninstallWindowsInk()
    {
        if (WinInkBusy) return;

        var confirmed = await Dialogs.ShowConfirmAsync(
            "Uninstall Windows Ink Plugin",
            "This removes Kuuube's Windows Ink plugin from OpenTabletDriver.\n\n" +
            "Any tablet using a Windows Ink output mode will fall back to a standard " +
            "pointer mode, and pen pressure/tilt will stop working until you reinstall it.\n\n" +
            "Do you want to proceed?");
        if (!confirmed)
            return;

        WinInkBusy = true;
        WinInkBusyStatus = "Uninstalling...";
        try
        {
            bool ok;
            try
            {
                ok = await _session.Daemon.UninstallPluginAsync(_winInkPluginDirectory);
            }
            catch
            {
                // The daemon only uninstalls plugins it currently has loaded. If the
                // plugin is installed on disk but failed to load (e.g. incompatible),
                // remove the directory directly so the user isn't stuck.
                ok = TryDeletePluginDirectory(_winInkPluginDirectory);
            }

            await _session.Daemon.LoadPluginsAsync();
            RefreshWindowsInkInstalledStatus();

            await Dialogs.ShowMessageAsync("Windows Ink Plugin",
                ok && !WinInkInstalled
                    ? "The Windows Ink plugin has been uninstalled."
                    : "The plugin could not be fully removed. Check the daemon log for details.");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync("Windows Ink Plugin", $"Error: {ex.Message}");
        }
        finally
        {
            WinInkBusy = false;
            WinInkBusyStatus = "";
        }
    }

    private static bool TryDeletePluginDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    partial void OnVmultiInstalledChanged(bool value)
    {
        ShowInstallButton = !value && !VmultiInstalling;
        ShowUninstallButton = value && !VmultiInstalling;
    }

    partial void OnVmultiInstallingChanged(bool value)
    {
        ShowInstallButton = !VmultiInstalled && !value;
        ShowUninstallButton = VmultiInstalled && !value;
    }

    // Typed OTD data
    private Settings? _settings;
    [ObservableProperty] private JToken? _tabletsJson; // tablets remain JToken (complex runtime type)

    // OTD Version (from referenced assembly)
    public string CurrentOtdVersion { get; } = typeof(Settings).Assembly.GetName().Version?.ToString() ?? "Unknown";


    // Tablet specs
    [ObservableProperty] private string _tabletArea = "";
    [ObservableProperty] private string _tabletPressure = "";
    [ObservableProperty] private string _tabletButtons = "";
    [ObservableProperty] private string _outputMode = "";

    [ObservableProperty] private string _settingsFilePath = "";

    /// <summary>Current OTD Settings object (typed). Use for reads and modifications.</summary>
    public Settings? CurrentSettings => _settings;

    public MainViewModel()
    {
        // Presets is coupled to the shell's current settings + apply path, so it receives
        // those as delegates rather than owning daemon/settings state itself.
        Presets = new PresetsViewModel(_settingsStore, () => _settings, ApplyAndSaveSettingsAsync);
        // Diagnostics owns the debug-report subscription; the shell keeps its IsConnected
        // in sync and stops it on page-leave/dispose.
        Diagnostics = new DiagnosticsViewModel(_session.Daemon);
        // Tablet Settings: the shell pushes the derived profile list and provides the shared
        // dialog-open + forget logic (also used by the Dashboard's "Open").
        TabletSettings = new TabletSettingsViewModel(OpenTabletSettingsForProfile, ForgetProfileCore);

        // Mirror the session's connection-state changes as our own so the Dashboard/Diagnostics
        // bindings on the shell update, and keep the Diagnostics connection gate in sync.
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(IConnectionState.IsConnected))
                Diagnostics.IsConnected = _session.IsConnected;
        };
        // The session drives connection; the shell reacts to do the data load + tablet reset
        // (data-load moves into the session in #41 PR 2).
        _session.Connected += () => _ = LoadDataAsync();
        _session.Disconnected += () => { HasTablet = false; TabletName = ""; };

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var (hidResult, setupResult) = await Task.Run(() =>
            (_vmulti.DetectHid(), _vmulti.DetectSetupApi()));

        VmultiHidStatus = hidResult.Message;
        VmultiSetupApiStatus = setupResult.Message;
        VmultiInstalled = hidResult.Visible || setupResult.Installed;

        if (hidResult.Functional)
            VmultiMessage = "Installed & active";
        else if (setupResult.Installed && !setupResult.Enabled)
            VmultiMessage = "Installed but disabled";
        else if (setupResult.Installed)
            VmultiMessage = "Installed (not active in HID)";
        else
            VmultiMessage = "Not installed";

        // Auto-start the daemon if needed and begin connecting (session owns this now).
        await _session.StartAndConnectAsync();
        _ = PollDataAsync();
    }

    private async Task PollDataAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            await Task.Delay(3000, _cts.Token).ConfigureAwait(false);
            if (IsConnected)
            {
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(LoadDataAsync);
                }
                catch { }
            }
        }
    }

    // Coalesced entry point: only the most recently requested load applies its results.
    private Task LoadDataAsync() => _loadGate.RunAsync(LoadDataCoreAsync);

    private async Task LoadDataCoreAsync()
    {
        try
        {
            // Load tablets (JToken — complex runtime type)
            var tablets = await _session.Daemon.GetTabletsAsync();
            TabletsJson = tablets;

            // Build set of detected tablet names and record last-seen timestamps
            var detectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tablets.Count > 0)
            {
                foreach (var t in tablets)
                {
                    var props = t["Properties"] ?? t;
                    var name = props["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        detectedNames.Add(name);
                        AppSettings.Set($"LastSeen:{name}", DateTime.Now.ToString("o"));
                    }
                }

                var first = tablets[0];
                var firstProps = first["Properties"] ?? first;
                HasTablet = true;
                TabletName = firstProps["Name"]?.ToString() ?? "Unknown";

                var specs = firstProps["Specifications"];
                var digi = specs?["Digitizer"];
                var pen = specs?["Pen"];
                TabletArea = $"{digi?["Width"]} x {digi?["Height"]} mm";
                TabletPressure = pen?["MaxPressure"]?.ToString() ?? "?";
                TabletButtons = pen?["ButtonCount"]?.ToString() ?? "?";
            }
            else
            {
                HasTablet = false;
                TabletName = "";
                TabletArea = "";
                TabletPressure = "";
                TabletButtons = "";
            }

            // Load settings — TYPED
            _settings = await _session.Daemon.GetSettingsAsync();

            if (_settings != null)
            {
                var profileItems = _settings.Profiles
                    .Select(p =>
                    {
                        bool detected = detectedNames.Contains(p.Tablet);
                        DateTime? lastSeen = null;
                        var stored = AppSettings.Get($"LastSeen:{p.Tablet}");
                        if (stored != null && DateTime.TryParse(stored, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                            lastSeen = dt;
                        if (detected)
                            lastSeen = DateTime.Now;
                        return new ProfileItem(p, detected, lastSeen);
                    })
                    .ToList();
                TabletSettings.Profiles = profileItems;

                if (profileItems.Count > 0)
                {
                    var mode = profileItems[0].Profile.OutputMode?.Path;
                    OutputMode = mode?.Split('.').LastOrDefault() ?? "Unknown";
                    HasWindowsInk = mode?.Contains("WinInk", StringComparison.OrdinalIgnoreCase) ?? false;
                }
            }

            // Load app info — TYPED
            var appInfo = await _session.Daemon.GetAppInfoAsync();
            if (appInfo != null)
            {
                Presets.PresetDirectory = appInfo.PresetDirectory ?? "";
                SettingsFilePath = appInfo.SettingsFile ?? "";
                _winInkPluginDirectory = _winInk.GetPluginDirectoryPath(appInfo.PluginDirectory);
                RefreshWindowsInkInstalledStatus();
            }
            await Presets.LoadAsync();
        }
        catch { /* Data load failed — will retry on next connection */ }
    }

    /// <summary>
    /// Apply settings to the daemon AND save to disk (like OTD's own Save + Apply).
    /// </summary>
    public async Task ApplyAndSaveSettingsAsync(Settings settings)
    {
        _settings = settings;
        await _session.Daemon.SetSettingsAsync(settings);

        // Persist to disk (same as OTD's own UX Save button).
        // TODO(#21): surface the failure instead of ignoring TrySave's result.
        if (!string.IsNullOrEmpty(SettingsFilePath))
            _settingsStore.TrySave(settings, SettingsFilePath);

        await LoadDataAsync();
    }

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    partial void OnCurrentPageChanged(string? oldValue, string newValue)
    {
        if (oldValue == "Diagnostics" && newValue != "Diagnostics")
            _ = Diagnostics.StopDebuggingAsync();
    }

    [RelayCommand]
    private async Task RefreshConnection()
    {
        // When connected, "refresh" reloads data (still shell-owned until #41 PR 2);
        // when disconnected, ask the session to (re)connect.
        if (_session.IsConnected)
            await LoadDataAsync();
        else
            await _session.ConnectAsync();
    }


    [RelayCommand]
    private async Task OpenConnectedTabletSettings()
    {
        if (!HasTablet || string.IsNullOrEmpty(TabletName) || _settings == null) return;
        var profile = _settings.Profiles.FirstOrDefault(p => p.Tablet == TabletName);
        if (profile != null)
            await OpenTabletSettingsForProfile(profile);
    }

    // Shared by the Tablet Settings page (via delegate) — removes a profile and re-applies.
    private async Task ForgetProfileCore(string tabletName)
    {
        if (_settings == null || string.IsNullOrEmpty(tabletName)) return;
        var profile = _settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) return;

        _settings.Profiles.Remove(profile);
        await ApplyAndSaveSettingsAsync(_settings);
    }

    private (float Width, float Height)? GetTabletDigitizer(string tabletName)
    {
        if (TabletsJson is not JArray tablets) return null;
        foreach (var t in tablets)
        {
            var props = t["Properties"] ?? t;
            if (props["Name"]?.ToString() == tabletName)
            {
                var digi = props["Specifications"]?["Digitizer"];
                if (digi != null)
                {
                    var w = digi["Width"]?.Value<float>() ?? 0;
                    var h = digi["Height"]?.Value<float>() ?? 0;
                    if (w > 0 && h > 0) return (w, h);
                }
            }
        }
        return null;
    }

    private async Task OpenTabletSettingsForProfile(Profile profile)
    {
        var tabletName = profile.Tablet;
        var digitizer = GetTabletDigitizer(tabletName);
        var dialog = new Views.TabletSettingsDialog(
            profile,
            _settings,
            async updatedSettings => await ApplyAndSaveSettingsAsync(updatedSettings),
            async () =>
            {
                _settings = await _session.Daemon.GetSettingsAsync();
                if (_settings != null)
                    return _settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
                return null;
            },
            digitizer);

        var mainWindow = Dialogs.GetMainWindow();
        if (mainWindow != null)
            await dialog.ShowDialog(mainWindow);
    }

    [RelayCommand]
    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task InstallVmulti()
    {
        var confirmed = await Dialogs.ShowConfirmAsync(
            "Install VMulti Driver",
            "VMulti driver installation may restart your computer.\n\n" +
            "Please save all work in other applications before continuing.\n\n" +
            "Do you want to proceed?");

        if (!confirmed)
            return;

        VmultiInstalling = true;
        VmultiInstallStatus = "Starting...";

        Action<string> onStatus = status =>
            Dispatcher.UIThread.InvokeAsync(() => VmultiInstallStatus = status);
        _vmultiInstaller.StatusChanged += onStatus;

        try
        {
            var installResult = await Task.Run(() => _vmultiInstaller.InstallAsync(_cts.Token));
            VmultiInstallStatus = installResult.Message;

            if (installResult.Success)
            {
                await RefreshVmultiDetection();
                await Dialogs.ShowMessageAsync("VMulti Installation", installResult.Message);
            }
            else
            {
                await Dialogs.ShowMessageAsync("VMulti Installation", installResult.Message);
            }
        }
        catch (Exception ex)
        {
            VmultiInstallStatus = $"Error: {ex.Message}";
        }
        finally
        {
            _vmultiInstaller.StatusChanged -= onStatus;
            VmultiInstalling = false;
        }
    }

    [RelayCommand]
    private async Task UninstallVmulti()
    {
        var confirmed = await Dialogs.ShowConfirmAsync(
            "Uninstall VMulti Driver",
            "This will uninstall the VMulti virtual driver.\n\n" +
            "Your computer may need to restart to complete the removal.\n" +
            "Please save all work in other applications before continuing.\n\n" +
            "Do you want to proceed?");

        if (!confirmed)
            return;

        VmultiInstalling = true;
        VmultiInstallStatus = "Starting uninstall...";

        Action<string> onStatus = status =>
            Dispatcher.UIThread.InvokeAsync(() => VmultiInstallStatus = status);
        _vmultiInstaller.StatusChanged += onStatus;

        try
        {
            var uninstallResult = await Task.Run(() => _vmultiInstaller.UninstallAsync(_cts.Token));
            VmultiInstallStatus = uninstallResult.Message;

            if (uninstallResult.Success)
            {
                await RefreshVmultiDetection();
                await Dialogs.ShowMessageAsync("VMulti Uninstallation", uninstallResult.Message);
            }
            else
            {
                await Dialogs.ShowMessageAsync("VMulti Uninstallation", uninstallResult.Message);
            }
        }
        catch (Exception ex)
        {
            VmultiInstallStatus = $"Error: {ex.Message}";
        }
        finally
        {
            _vmultiInstaller.StatusChanged -= onStatus;
            VmultiInstalling = false;
        }
    }


    [RelayCommand]
    private async Task RefreshVmultiDetection()
    {
        var (hidResult, setupResult) = await Task.Run(() =>
            (_vmulti.DetectHid(), _vmulti.DetectSetupApi()));

        VmultiHidStatus = hidResult.Message;
        VmultiSetupApiStatus = setupResult.Message;
        VmultiInstalled = hidResult.Visible || setupResult.Installed;

        if (hidResult.Functional)
            VmultiMessage = "Installed & active";
        else if (setupResult.Installed && !setupResult.Enabled)
            VmultiMessage = "Installed but disabled";
        else if (setupResult.Installed)
            VmultiMessage = "Installed (not active in HID)";
        else
            VmultiMessage = "Not installed";
    }

    public void Dispose()
    {
        Diagnostics.Dispose(); // stops debugging + disables the daemon debug stream if active
        _cts.Cancel();
        _session.Dispose();    // cancels the connect loop + disposes the daemon client
        _loadGate.Dispose();
        Utilities.Dispose();
    }
}

public record ConfigurationItem(string Name, string FileName, string Path, string SizeText);

public record ProfileItem(Profile Profile, bool IsDetected, DateTime? LastSeen)
{
    public string Tablet => Profile.Tablet;

    public string StatusText
    {
        get
        {
            if (IsDetected) return "Detected";
            if (LastSeen == null) return "Not detected";
            return $"Not detected — {FormatRelativeTime(LastSeen.Value)}";
        }
    }

    public string? LastSeenDetail
    {
        get
        {
            if (IsDetected || LastSeen == null) return null;
            return $"Last seen {LastSeen.Value:yyyy-MM-dd} at {LastSeen.Value:h:mm tt}";
        }
    }

    private static string FormatRelativeTime(DateTime lastSeen)
    {
        var elapsed = DateTime.Now - lastSeen;

        if (elapsed.TotalMinutes < 1) return "seen just now";
        if (elapsed.TotalMinutes < 60) return $"seen {(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"seen {(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 2) return "seen yesterday";
        if (elapsed.TotalDays < 7) return $"seen {(int)elapsed.TotalDays} days ago";
        if (elapsed.TotalDays < 30) return $"seen {(int)(elapsed.TotalDays / 7)} weeks ago";
        return "seen a long time ago";
    }
}

/// <summary>
/// View-model record for a settings snapshot file shown in the Saved Settings list.
/// Plain-property record so Avalonia bindings can resolve Name/LastModified directly
/// (JObject indexer bindings stopped rendering for TextBlock.Text in Avalonia 12).
/// </summary>
public record PresetInfo(string Name, string Path, string Content, string LastModified);
