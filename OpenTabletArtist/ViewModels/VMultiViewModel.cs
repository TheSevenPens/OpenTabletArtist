using System;
using System.ComponentModel;
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
    private readonly VMultiInstaller _vmultiInstaller = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>The page enumerates nothing itself: it shows what the health collector's VMulti probe saw
    /// (see <see cref="VMultiStatus"/>), so it says what Home says. It starts as "Checking..." and only
    /// moves once a report arrives, so it never claims "Not installed" before anyone has looked.</summary>
    public VMultiViewModel(IDialogService dialogs, HealthService health)
    {
        _dialogs = dialogs;
        _health = health;
        _health.PropertyChanged += OnHealthChanged;
        Apply(VMultiStatus.From(_health.Analysis));
    }

    [ObservableProperty] private bool _vmultiInstalled;
    [ObservableProperty] private string _vmultiMessage = "Checking...";
    [ObservableProperty] private string _vmultiHidStatus = "Checking...";
    [ObservableProperty] private string _vmultiSetupApiStatus = "Checking...";
    [ObservableProperty] private bool _vmultiInstalling;
    [ObservableProperty] private string _vmultiInstallStatus = "";
    [ObservableProperty] private bool _showInstallButton;
    [ObservableProperty] private bool _showUninstallButton;
    /// <summary>True only when VMulti is known not to be installed, not while the answer is pending.</summary>
    [ObservableProperty] private bool _showNotInstalledNote;

    partial void OnVmultiInstallingChanged(bool value) => UpdateVmultiButtons();

    // Known is what keeps both buttons away while the answer is pending or unreadable: neither "install"
    // nor "uninstall" is right when we haven't seen the state.
    private bool _vmultiKnown;

    private void UpdateVmultiButtons()
    {
        ShowInstallButton = _vmultiKnown && !VmultiInstalled && !VmultiInstalling;
        ShowUninstallButton = _vmultiKnown && VmultiInstalled && !VmultiInstalling;
        ShowNotInstalledNote = _vmultiKnown && !VmultiInstalled;
    }

    private void OnHealthChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A null Analysis (the daemon went away) raises nothing, so the last answer stays on screen
        // rather than flashing "Checking..." for a driver that has not changed.
        if (e.PropertyName == nameof(HealthService.Analysis) && _health.Analysis is { } report)
            Apply(VMultiStatus.From(report));
    }

    private void Apply(VMultiStatus status)
    {
        _vmultiKnown = status.Known;
        VmultiInstalled = status.Installed;
        VmultiMessage = status.Summary;
        VmultiSetupApiStatus = status.SetupApiText;
        VmultiHidStatus = status.HidText;
        // Recompute explicitly: no property hook fires when a value stays the same, and Known can change
        // while Installed does not (pending -> not installed).
        UpdateVmultiButtons();
    }

    /// <summary>Re-collect health and show the result. Also what runs after an install or uninstall, so
    /// the page waits for a collection that started after the change rather than reading an older one.</summary>
    [RelayCommand]
    private async Task RefreshVmultiDetection()
    {
        Apply(VMultiStatus.Pending);
        var report = await _health.RefreshAsync();
        Apply(report == null ? VMultiStatus.Unreadable("the health check did not complete") : VMultiStatus.From(report));
    }

    [RelayCommand]
    private void OpenFolder(string path)
    {
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
        _health.PropertyChanged -= OnHealthChanged;
        _cts.Cancel();
        _cts.Dispose();
    }
}
