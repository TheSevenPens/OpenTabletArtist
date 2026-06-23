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
    private readonly ISettingsFileStore _settingsStore = new SettingsFileStore();
    // Shared application session — owns the daemon connection, settings, and data load (Option C, #41).
    private readonly AppSession _session;
    private readonly VMultiDetector _vmulti = new();
    private readonly VMultiInstaller _vmultiInstaller = new();
    private readonly WindowsInkPluginService _winInk = new();
    private readonly CancellationTokenSource _cts = new(); // for VMulti install/uninstall (shell-owned for now)

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

    // Device data also forwards to the session (#41 PR 2); mirrored via the session PropertyChanged.
    public bool HasTablet => _session.HasTablet;
    public string TabletName => _session.TabletName;
    public string TabletArea => _session.TabletArea;
    public string TabletPressure => _session.TabletPressure;
    public string TabletButtons => _session.TabletButtons;
    public string OutputMode => _session.OutputMode;
    public bool HasWindowsInk => _session.HasWindowsInk;
    public Settings? CurrentSettings => _session.CurrentSettings;

    // Dashboard data
    [ObservableProperty] private bool _vmultiInstalled;
    [ObservableProperty] private string _vmultiMessage = "Checking...";
    [ObservableProperty] private string _vmultiHidStatus = "Checking...";
    [ObservableProperty] private string _vmultiSetupApiStatus = "Checking...";
    [ObservableProperty] private bool _vmultiInstalling;
    [ObservableProperty] private string _vmultiInstallStatus = "";

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

    // TabletStatusText / WindowsInkStatusText are recomputed from the session's device data
    // in the PropertyChanged mirror (see ctor), since HasTablet/TabletName/HasWindowsInk now
    // live on the session.

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

    // OTD Version (from referenced assembly)
    public string CurrentOtdVersion { get; } = typeof(Settings).Assembly.GetName().Version?.ToString() ?? "Unknown";

    public MainViewModel()
    {
        // The session owns the daemon connection, settings, and data load.
        _session = new AppSession(new DaemonClient(), new DaemonLifecycleService(), _settingsStore);

        // Presets reads the session's current settings + apply path via delegates.
        Presets = new PresetsViewModel(_settingsStore, () => _session.CurrentSettings, _session.ApplyAndSaveSettingsAsync);
        // Diagnostics owns the debug-report subscription; the shell keeps its IsConnected
        // in sync and stops it on page-leave/dispose.
        Diagnostics = new DiagnosticsViewModel(_session.Daemon);
        // Tablet Settings: the shell pushes the derived profile list and provides the shared
        // dialog-open + forget logic (also used by the Dashboard's "Open").
        TabletSettings = new TabletSettingsViewModel(OpenTabletSettingsForProfile, ForgetProfileCore);

        // Mirror the session's property changes as our own so the Dashboard/Diagnostics
        // bindings on the shell update, and recompute the derived status texts + gate.
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) OnPropertyChanged(e.PropertyName);
            if (e.PropertyName is nameof(IDeviceData.HasTablet) or nameof(IDeviceData.TabletName))
                TabletStatusText = _session.HasTablet ? $"{_session.TabletName} detected" : "No tablet detected";
            if (e.PropertyName == nameof(IDeviceData.HasWindowsInk))
                WindowsInkStatusText = _session.HasWindowsInk ? "Plugin active" : "Plugin not active in current profile";
            if (e.PropertyName == nameof(IConnectionState.IsConnected))
                Diagnostics.IsConnected = _session.IsConnected;
        };
        // The session drives connection; the shell reacts to do the data load + tablet reset
        // (data-load moves into the session in #41 PR 2).
        // The session does the load + tablet reset itself; the shell reacts to push data into
        // the pages and refresh the Windows Ink card (those move into their VMs in #41 PR 3-4).
        _session.DataLoaded += OnSessionDataLoaded;

        _ = InitAsync();
    }

    private void OnSessionDataLoaded()
    {
        TabletSettings.Profiles = _session.Profiles;
        Presets.PresetDirectory = _session.PresetDirectory;
        _ = Presets.LoadAsync();
        _winInkPluginDirectory = _winInk.GetPluginDirectoryPath(_session.PluginDirectory);
        RefreshWindowsInkInstalledStatus();
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

        // Auto-start the daemon if needed and begin connecting (session owns this + polling now).
        await _session.StartAndConnectAsync();
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
        // When connected, "refresh" reloads data; when disconnected, ask the session to (re)connect.
        if (_session.IsConnected)
            await _session.ReloadAsync();
        else
            await _session.ConnectAsync();
    }


    [RelayCommand]
    private async Task OpenConnectedTabletSettings()
    {
        var settings = _session.CurrentSettings;
        if (!_session.HasTablet || string.IsNullOrEmpty(_session.TabletName) || settings == null) return;
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == _session.TabletName);
        if (profile != null)
            await OpenTabletSettingsForProfile(profile);
    }

    // Shared by the Tablet Settings page (via delegate) — removes a profile and re-applies.
    private async Task ForgetProfileCore(string tabletName)
    {
        var settings = _session.CurrentSettings;
        if (settings == null || string.IsNullOrEmpty(tabletName)) return;
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) return;

        settings.Profiles.Remove(profile);
        await _session.ApplyAndSaveSettingsAsync(settings);
    }

    private async Task OpenTabletSettingsForProfile(Profile profile)
    {
        var tabletName = profile.Tablet;
        var digitizer = _session.GetTabletDigitizer(tabletName);
        var dialog = new Views.TabletSettingsDialog(
            profile,
            _session.CurrentSettings,
            async updatedSettings => await _session.ApplyAndSaveSettingsAsync(updatedSettings),
            async () =>
            {
                var settings = await _session.Daemon.GetSettingsAsync();
                return settings?.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
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
        _session.Dispose();    // cancels the connect/poll loops, disposes the daemon client + load gate
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
