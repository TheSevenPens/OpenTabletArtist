using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

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
    // Navigate the shell to a tablet's in-app settings page (#tablet-ux-overhaul). Replaces opening
    // the modal dialog for a Home card's "Settings".
    private readonly Action<string> _navigateToTablet;
    private readonly Action? _openDriverCleanup;
    // Jump to the Windows Ink Plugin page (Advanced) — where install/update now lives (#317).
    private readonly Action? _openWindowsInk;
    private readonly VMultiDetector _vmulti = new();
    private readonly VMultiInstaller _vmultiInstaller = new();
    private readonly CancellationTokenSource _cts = new();

    public DashboardViewModel(AppSession session, IDialogService dialogs, Action<string> navigateToTablet,
        HealthService health, DriverConflictMonitor? conflicts = null, Action? openDriverCleanup = null,
        Action? openWindowsInk = null)
    {
        _session = session;
        _dialogs = dialogs;
        _navigateToTablet = navigateToTablet;
        _openDriverCleanup = openDriverCleanup;
        _openWindowsInk = openWindowsInk;
        Health = health;
        Conflicts = conflicts ?? new DriverConflictMonitor();

        _session.PropertyChanged += OnSessionPropertyChanged;
        _session.DataLoaded += OnSessionDataLoaded;

        _ = InitVmultiAsync();
    }

    /// <summary>Conflicting-driver detection (#245); the Home alert card binds to this and the button
    /// jumps to the Driver cleanup page.</summary>
    public DriverConflictMonitor Conflicts { get; }

    /// <summary>The health-check catalog (#317); the Home "Needs attention" list binds to
    /// <c>Health.Issues</c>, and <see cref="RemediateCommand"/> dispatches each card's Fix button.</summary>
    public HealthService Health { get; }

    /// <summary>Perform an issue's fix: run the relevant command in place (install/update/reconnect) or
    /// navigate to the tablet whose setting needs changing.</summary>
    [RelayCommand]
    private void Remediate(HealthIssue? issue)
    {
        if (issue?.Remediation is not { } r) return;
        switch (r.Area)
        {
            case RemediationArea.WindowsInk:
                _openWindowsInk?.Invoke(); // the fix lives on the Windows Ink Plugin page now (#317)
                break;
            case RemediationArea.Daemon:
                if (issue.Id == "daemon.foreign") RestartDaemonCommand.Execute(null);
                else RefreshConnectionCommand.Execute(null);
                break;
            case RemediationArea.TabletPenBehavior:
                if (!string.IsNullOrEmpty(r.TabletName)) _navigateToTablet(r.TabletName);
                break;
        }
    }

    [RelayCommand]
    private void OpenDriverCleanup() => _openDriverCleanup?.Invoke();

    // --- Connection + device state forwarded from the session (mirrored below) ---
    public bool IsConnected => _session.IsConnected;
    public string DaemonStatusText => _session.DaemonStatusText;
    public bool ShowAppOwnedDaemon => _session.ShowAppOwnedDaemon;
    public bool ShowForeignDaemonWarning => _session.ShowForeignDaemonWarning;
    public bool ShowDaemonSourceUnknown => _session.ShowDaemonSourceUnknown;
    public string DaemonSourcePath => _session.DaemonSourcePath;
    // Version read off the connected daemon's binary (#296).
    public string DaemonVersion => _session.DaemonVersion;
    public bool HasDaemonVersion => _session.HasDaemonVersion;
    public bool CanStartDaemon => _session.CanStartDaemon;
    public bool HasTablet => _session.HasTablet;

    // Lifecycle-operation feedback, forwarded from the session (mirrored via PropertyChanged).
    public bool IsDaemonBusy => _session.IsDaemonBusy;
    public string DaemonOperationStatus => _session.DaemonOperationStatus;
    public bool ShowDaemonActivity => _session.ShowDaemonActivity;
    public string DaemonActivityText => _session.DaemonActivityText;
    public bool ShowStartButton => _session.ShowStartButton;
    public string DaemonOperationError => _session.DaemonOperationError;
    public bool HasDaemonOperationError => _session.HasDaemonOperationError;
    public bool IsDaemonExeMissing => _session.IsDaemonExeMissing;
    // Auto-connect gave up waiting but the background loop is still retrying (#296).
    public bool ConnectStalled => _session.ConnectStalled;

    public IAsyncRelayCommand StartDaemonCommand => _session.StartDaemonCommand;
    public IAsyncRelayCommand StopDaemonCommand => _session.StopDaemonCommand;
    public IAsyncRelayCommand RestartDaemonCommand => _session.RestartDaemonCommand;
    public IRelayCommand LaunchOtdUxCommand => _session.LaunchOtdUxCommand;

    [ObservableProperty] private string _tabletStatusText = "No tablet detected";

    /// <summary>One card per connected tablet (#190); empty when none, where the view shows the
    /// "No tablet detected" placeholder instead.</summary>
    [ObservableProperty] private List<DashboardTabletViewModel> _tablets = [];

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null) OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(IDeviceData.HasTablet) or nameof(IDeviceData.TabletName))
            TabletStatusText = _session.HasTablet ? $"{_session.TabletName} detected" : "No tablet detected";
        // Active tablet switched (e.g. from the tray or Test/Diagnostics) → refresh the card badge.
        if (e.PropertyName == nameof(IDeviceData.ActiveTabletName))
            RebuildTabletCards();
    }

    private void OnSessionDataLoaded()
    {
        RebuildTabletCards();
    }

    // One card per connected tablet (#190). The active-tablet badge is only meaningful when there's a
    // choice to make, so it's marked only when more than one tablet is connected.
    private void RebuildTabletCards()
    {
        var active = _session.ActiveTabletName;
        var multiple = _session.DetectedTablets.Count > 1;
        Tablets = _session.DetectedTablets
            .Select(t =>
            {
                var isActive = multiple && t.Name == active;
                return new DashboardTabletViewModel(
                    t, OpenTabletSettingsByNameAsync, _session.SetActiveTablet,
                    isActive: isActive, showSetActive: multiple && !isActive);
            })
            .ToList();
    }

    [RelayCommand]
    private async Task RefreshConnection()
    {
        if (_session.IsConnected) await _session.ReloadAsync();
        else await _session.ConnectAsync();
    }

    /// <summary>Open a specific tablet's in-app settings page (#190 / #tablet-ux-overhaul).</summary>
    private Task OpenTabletSettingsByNameAsync(string tabletName)
    {
        if (!string.IsNullOrEmpty(tabletName)) _navigateToTablet(tabletName);
        return Task.CompletedTask;
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

        // Recompute explicitly: the OnVmultiInstalledChanged hook only fires when the value changes,
        // so when detection legitimately leaves VmultiInstalled = false (its default), the buttons
        // would otherwise never be initialized and the Install button wouldn't appear.
        UpdateVmultiButtons();
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

            if (installResult.Success && installResult.RebootRecommended)
            {
                var restart = await _dialogs.ShowConfirmAsync(
                    "Restart recommended",
                    installResult.Message + "\n\nRestart now? Save your work in other apps first.");
                if (restart)
                    TryRestartWindows();
            }
            else
            {
                await _dialogs.ShowMessageAsync("VMulti Installation", installResult.Message);
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

    // User-initiated Windows restart (offered after a VMulti uninstall, #112). Best-effort.
    private static void TryRestartWindows()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 0")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
        }
        catch { /* best-effort; user can restart manually */ }
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

            if (uninstallResult.Success && uninstallResult.RebootRecommended)
            {
                var restart = await _dialogs.ShowConfirmAsync(
                    "Restart recommended",
                    uninstallResult.Message + "\n\nRestart now? Save your work in other apps first.");
                if (restart)
                    TryRestartWindows();
            }
            else
            {
                await _dialogs.ShowMessageAsync("VMulti Uninstallation", uninstallResult.Message);
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

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _session.DataLoaded -= OnSessionDataLoaded;
        _cts.Cancel();
        _cts.Dispose();
    }
}

/// <summary>One connected tablet on the Dashboard (#190): its name, a one-line spec summary, and a
/// command to open that tablet's settings dialog.</summary>
public partial class DashboardTabletViewModel : ObservableObject
{
    private readonly Func<string, Task> _openSettings;
    private readonly Action<string> _setActive;

    public DashboardTabletViewModel(DetectedTablet tablet, Func<string, Task> openSettings,
        Action<string> setActive, bool isActive, bool showSetActive)
    {
        Name = tablet.Name;
        SpecsText = $"{tablet.Area} · {tablet.Pressure} pressure levels · {tablet.Buttons} buttons";
        IsActive = isActive;
        ShowSetActive = showSetActive;
        _openSettings = openSettings;
        _setActive = setActive;
    }

    public string Name { get; }
    public string SpecsText { get; }

    /// <summary>This card is the active tablet (the one the single-target flows act on). Only set when
    /// more than one tablet is connected, so a lone tablet doesn't show a redundant badge (#190).</summary>
    public bool IsActive { get; }

    /// <summary>Show the "Set active" action — i.e. more than one tablet is connected and this isn't
    /// the active one (#190).</summary>
    public bool ShowSetActive { get; }

    [RelayCommand]
    private Task OpenSettings() => _openSettings(Name);

    [RelayCommand]
    private void SetActive() => _setActive(Name);
}
