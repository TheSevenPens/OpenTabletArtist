using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// View model for the Dashboard — the application's home/status page (Option C, #41 PR 4).
/// Unlike the narrow page VMs, the Dashboard surfaces the whole session, so it takes the
/// concrete <see cref="AppSession"/> (connection + device + settings) rather than a single
/// role interface, and owns the Dashboard-only concerns: the VMulti driver card and the
/// Windows Ink plugin card. It forwards the session's connection/device state (mirroring its
/// PropertyChanged) so the existing bindings keep working.
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly AppSession _session;
    private readonly IDialogService _dialogs;
    private readonly VMultiDetector _vmulti = new();
    private readonly VMultiInstaller _vmultiInstaller = new();
    private readonly WindowsInkPluginService _winInk = new();
    private readonly CancellationTokenSource _cts = new();

    public DashboardViewModel(AppSession session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;

        _session.PropertyChanged += OnSessionPropertyChanged;
        _session.DataLoaded += OnSessionDataLoaded;

        _ = InitVmultiAsync();
    }

    // --- Connection + device state forwarded from the session (mirrored below) ---
    public bool IsConnected => _session.IsConnected;
    public string DaemonStatusText => _session.DaemonStatusText;
    public bool ShowAppOwnedDaemon => _session.ShowAppOwnedDaemon;
    public bool ShowForeignDaemonWarning => _session.ShowForeignDaemonWarning;
    public bool ShowDaemonSourceUnknown => _session.ShowDaemonSourceUnknown;
    public string DaemonSourcePath => _session.DaemonSourcePath;
    public bool CanStartDaemon => _session.CanStartDaemon;
    public bool HasTablet => _session.HasTablet;
    public bool HasWindowsInk => _session.HasWindowsInk;

    // Lifecycle-operation feedback, forwarded from the session (mirrored via PropertyChanged).
    public bool IsDaemonBusy => _session.IsDaemonBusy;
    public string DaemonOperationStatus => _session.DaemonOperationStatus;
    public string DaemonOperationError => _session.DaemonOperationError;
    public bool HasDaemonOperationError => _session.HasDaemonOperationError;

    public IAsyncRelayCommand StartDaemonCommand => _session.StartDaemonCommand;
    public IAsyncRelayCommand StopDaemonCommand => _session.StopDaemonCommand;
    public IAsyncRelayCommand RestartDaemonCommand => _session.RestartDaemonCommand;
    public IRelayCommand LaunchOtdUxCommand => _session.LaunchOtdUxCommand;

    public string CurrentOtdVersion { get; } = typeof(Settings).Assembly.GetName().Version?.ToString() ?? "Unknown";

    [ObservableProperty] private string _tabletStatusText = "No tablet detected";
    [ObservableProperty] private string _windowsInkStatusText = "Not configured";

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null) OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(IDeviceData.HasTablet) or nameof(IDeviceData.TabletName))
            TabletStatusText = _session.HasTablet ? $"{_session.TabletName} detected" : "No tablet detected";
        if (e.PropertyName == nameof(IDeviceData.HasWindowsInk))
            WindowsInkStatusText = _session.HasWindowsInk ? "Plugin active" : "Plugin not active in current profile";
    }

    private void OnSessionDataLoaded()
    {
        // The Windows Ink card reflects the daemon's plugin directory from the last data load.
        _winInkPluginDirectory = _winInk.GetPluginDirectoryPath(_session.PluginDirectory);
        RefreshWindowsInkInstalledStatus();
    }

    [RelayCommand]
    private async Task RefreshConnection()
    {
        if (_session.IsConnected) await _session.ReloadAsync();
        else await _session.ConnectAsync();
    }

    [RelayCommand]
    private async Task OpenConnectedTabletSettings()
    {
        var settings = _session.CurrentSettings;
        if (!_session.HasTablet || string.IsNullOrEmpty(_session.TabletName) || settings == null) return;
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == _session.TabletName);
        if (profile != null)
            await _dialogs.ShowTabletSettingsAsync(profile);
    }

    // --- VMulti driver card ---
    [ObservableProperty] private bool _vmultiInstalled;
    [ObservableProperty] private string _vmultiMessage = "Checking...";
    [ObservableProperty] private string _vmultiHidStatus = "Checking...";
    [ObservableProperty] private string _vmultiSetupApiStatus = "Checking...";
    [ObservableProperty] private bool _vmultiInstalling;
    [ObservableProperty] private string _vmultiInstallStatus = "";
    [ObservableProperty] private bool _showInstallButton;
    [ObservableProperty] private bool _showUninstallButton;

    partial void OnVmultiInstalledChanged(bool value) => UpdateVmultiButtons();
    partial void OnVmultiInstallingChanged(bool value) => UpdateVmultiButtons();

    private void UpdateVmultiButtons()
    {
        ShowInstallButton = !VmultiInstalled && !VmultiInstalling;
        ShowUninstallButton = VmultiInstalled && !VmultiInstalling;
    }

    private async Task InitVmultiAsync() => await RefreshVmultiDetection();

    [RelayCommand]
    private async Task RefreshVmultiDetection()
    {
        var (hidResult, setupResult) = await Task.Run(() =>
            (_vmulti.DetectHid(), _vmulti.DetectSetupApi()));

        VmultiHidStatus = hidResult.Message;
        VmultiSetupApiStatus = setupResult.Message;
        // "Installed" means a working driver — detection treats driverless leftover nodes (Code 28
        // after an uninstall) as not installed, so the card flips to "Not installed" once the driver
        // is gone instead of reporting the orphaned device nodes as installed.
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

    [RelayCommand]
    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task InstallVmulti()
    {
        var confirmed = await _dialogs.ShowConfirmAsync(
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
            await RefreshVmultiDetection();
            await _dialogs.ShowMessageAsync("VMulti Installation", installResult.Message);
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
        var confirmed = await _dialogs.ShowConfirmAsync(
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
            await RefreshVmultiDetection();
            await _dialogs.ShowMessageAsync("VMulti Uninstallation", uninstallResult.Message);
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

    // --- Windows Ink plugin card ---
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWinInkCheckUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowWinInkInstallUpdate))]
    private bool _winInkUpdateAvailable;
    [ObservableProperty] private string _winInkLatestVersion = "";
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

    private PluginMetadata? _winInkLatest;
    private string _winInkPluginDirectory = "";

    public bool ShowWinInkInstall => !WinInkInstalled && !WinInkBusy;
    public bool ShowWinInkUninstall => WinInkInstalled && !WinInkBusy;
    public bool ShowWinInkCheckUpdate => WinInkInstalled && !WinInkUpdateAvailable && !WinInkBusy;
    public bool ShowWinInkInstallUpdate => WinInkInstalled && WinInkUpdateAvailable && !WinInkBusy;
    public bool ShowWinInkRefresh => !WinInkBusy;
    public bool HasWinInkUpdateCheckStatus => !string.IsNullOrEmpty(WinInkUpdateCheckStatus);

    private string? WinInkPluginParentDirectory =>
        string.IsNullOrEmpty(_winInkPluginDirectory) ? null : Path.GetDirectoryName(_winInkPluginDirectory);

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
        WinInkVersionMismatch = !WindowsInkPluginService.IsCompatible(installed);

        RecomputeWinInkUpdate();
    }

    private async Task FetchLatestWindowsInkAsync()
    {
        _winInkLatest = await _winInk.GetLatestCompatibleAsync();
        WinInkLatestVersion = _winInkLatest?.PluginVersion?.ToString() ?? "";
        RecomputeWinInkUpdate();
    }

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
        if (!_session.IsConnected)
        {
            await _dialogs.ShowMessageAsync("Windows Ink Plugin",
                "The OpenTabletDriver daemon isn't connected. Start it first, then try again.");
            return;
        }

        WinInkBusy = true;
        WinInkBusyStatus = isUpgrade ? "Finding latest version..." : "Finding plugin...";
        WinInkUpdateCheckStatus = "";
        try
        {
            _winInkLatest ??= await _winInk.GetLatestCompatibleAsync();
            if (_winInkLatest == null)
            {
                await _dialogs.ShowMessageAsync("Windows Ink Plugin",
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

            await _dialogs.ShowMessageAsync("Windows Ink Plugin",
                ok
                    ? $"Windows Ink plugin v{WinInkPluginVersion} is now installed.\n\n" +
                      "Set a tablet's output mode to \"Windows Ink\" to enable pressure and tilt."
                    : "The plugin download did not complete successfully. Check the daemon log for details.");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Windows Ink Plugin", $"Error: {ex.Message}");
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

        var confirmed = await _dialogs.ShowConfirmAsync(
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
                ok = TryDeletePluginDirectory(_winInkPluginDirectory);
            }

            await _session.Daemon.LoadPluginsAsync();
            RefreshWindowsInkInstalledStatus();

            await _dialogs.ShowMessageAsync("Windows Ink Plugin",
                ok && !WinInkInstalled
                    ? "The Windows Ink plugin has been uninstalled."
                    : "The plugin could not be fully removed. Check the daemon log for details.");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Windows Ink Plugin", $"Error: {ex.Message}");
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

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session.DataLoaded -= OnSessionDataLoaded;
        _cts.Cancel();
        _cts.Dispose();
    }
}
