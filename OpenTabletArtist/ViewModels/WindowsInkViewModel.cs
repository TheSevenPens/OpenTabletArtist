using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The "Windows Ink Plugin" page (Advanced group): install / update / uninstall Kuuube's Windows Ink
/// plugin and show whether it's installed, compatible with the running driver, and actually in use by
/// the active profile. Moved off the Home page (#317) so Home just flags the issue and directs here.
/// </summary>
public partial class WindowsInkViewModel : ObservableObject, IDisposable
{
    private readonly AppSession _session;
    private readonly IDialogService _dialogs;
    private readonly HealthService _health;
    private readonly WindowsInkPluginService _winInk = new();

    private PluginMetadata? _winInkLatest;
    private string _winInkPluginDirectory = "";

    public WindowsInkViewModel(AppSession session, IDialogService dialogs, HealthService health)
    {
        _session = session;
        _dialogs = dialogs;
        _health = health;

        _session.PropertyChanged += OnSessionPropertyChanged;
        _session.DataLoaded += OnSessionDataLoaded;
        OnSessionDataLoaded(); // pick up state if data already loaded before this page was created
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDeviceData.HasWindowsInk))
        {
            OnPropertyChanged(nameof(HasWindowsInk));
            WindowsInkStatusText = _session.HasWindowsInk
                ? "Plugin active in current profile"
                : "Plugin not active in current profile";
        }
    }

    private void OnSessionDataLoaded()
    {
        _winInkPluginDirectory = _winInk.GetPluginDirectoryPath(_session.PluginDirectory);
        RefreshWindowsInkInstalledStatus();
        WindowsInkStatusText = _session.HasWindowsInk
            ? "Plugin active in current profile"
            : "Plugin not active in current profile";
    }

    /// <summary>The active profile actually uses a Windows Ink output mode.</summary>
    public bool HasWindowsInk => _session.HasWindowsInk;

    public string CurrentOtdVersion { get; } =
        typeof(Settings).Assembly.GetName().Version?.ToString() ?? "Unknown";

    [ObservableProperty] private string _windowsInkStatusText = "Not configured";

    // --- Install state ---
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
            _health.Refresh(); // reflect an uninstall immediately, not on the next background poll (#317)
            return;
        }

        WinInkInstalled = true;
        WinInkPluginVersion = installed.PluginVersion?.ToString() ?? "?";
        WinInkInstallStatusText = $"v{WinInkPluginVersion} installed";
        WinInkSupportedDriverVersion = installed.SupportedDriverVersion?.ToString() ?? "?";
        WinInkVersionMismatch = !WindowsInkPluginService.IsCompatible(installed);

        RecomputeWinInkUpdate();
        _health.Refresh(); // reflect an install/update immediately, not on the next background poll (#317)
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
    }
}
