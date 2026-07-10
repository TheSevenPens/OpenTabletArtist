using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The "VMulti Driver" page (Advanced): install / uninstall the VMulti virtual-pen driver and show its
/// detection status. VMulti is a prerequisite for the Windows Ink output mode — the Windows Ink plugin
/// injects pen pressure/tilt through VMulti's virtual HID device — so its state feeds the health-check
/// catalog (#317). Moved off Home so Home just flags a missing driver and directs here.
/// </summary>
public partial class VMultiViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly HealthService _health;
    private readonly VMultiDetector _vmulti = new();
    private readonly VMultiInstaller _vmultiInstaller = new();
    private readonly CancellationTokenSource _cts = new();

    public VMultiViewModel(IDialogService dialogs, HealthService health)
    {
        _dialogs = dialogs;
        _health = health;
        _ = InitVmultiAsync();
    }

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

        // Feed the health catalog so the "VMulti not installed" check reflects the latest detection.
        _health.SetVMultiInstalled(VmultiInstalled);
    }

    [RelayCommand]
    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Services.PlatformShell.RevealInFileManager(path);
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

    // User-initiated Windows restart (offered after a VMulti install/uninstall, #112). Best-effort.
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
        _cts.Cancel();
        _cts.Dispose();
    }
}
