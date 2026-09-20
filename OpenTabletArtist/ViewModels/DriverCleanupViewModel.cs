using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Driver Cleanup page (the TabletDriverCleanup tool). Page-VM split
/// (#14 phase 2): owns its own <see cref="TabletDriverCleanupRunner"/> and cancellation
/// source rather than borrowing the shell's, so the page is self-contained and its
/// lifetime is explicit. Confirm/message flows go through <see cref="IDialogService"/> (#37).
/// </summary>
public partial class DriverCleanupViewModel : ObservableObject, IDisposable
{
    private readonly TabletDriverCleanupRunner _cleanupRunner = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly IDialogService _dialogs;

    [ObservableProperty] private bool _cleanupInstalled;
    [ObservableProperty] private bool _cleanupBusy;
    [ObservableProperty] private string _cleanupStatus = "";

    /// <summary>Conflicting drivers the daemon flagged (#245) — shared with the Home alert.</summary>
    public DriverConflictMonitor Conflicts { get; }

    public string CleanupInstallPath => TabletDriverCleanupRunner.InstallDir;

    public DriverCleanupViewModel(IDialogService dialogs, DriverConflictMonitor? conflicts = null)
    {
        _dialogs = dialogs;
        Conflicts = conflicts ?? new DriverConflictMonitor();
        CleanupInstalled = TabletDriverCleanupRunner.IsInstalled();
    }

    [RelayCommand]
    private void OpenFolder(string path)
    {
        Services.PlatformShell.RevealInFileManager(path);
    }

    /// <summary>Opens a detection's FAQ link. Restricted to the OTD wiki domain so a crafted log line
    /// can't turn this into an arbitrary-URL launcher.</summary>
    /// <remarks>
    /// The domain restriction stays here, not in the helper (#889). It exists because of where this
    /// particular URL comes from -- a detection, which is text this application did not author -- and the
    /// helper's own https-only rule is a floor under every caller, not a replacement for this one.
    /// </remarks>
    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (url is null) return;
        if (!url.StartsWith("https://opentabletdriver.net/", StringComparison.OrdinalIgnoreCase)) return;

        Services.PlatformShell.OpenUrl(url);
    }

    [RelayCommand]
    private async Task InstallCleanup()
    {
        var confirmed = await _dialogs.ShowConfirmAsync(
            "Install TabletDriverCleanup",
            "This will download TabletDriverCleanup — a tool by the OpenTabletDriver " +
            "team that removes leftover bits of manufacturer tablet drivers " +
            "(Wacom, Huion, XP-Pen, etc.) — and install it to:\n\n" +
            CleanupInstallPath + "\n\n" +
            "No admin permission is required for installation. " +
            "Admin will be required later when you run the tool.\n\n" +
            "Do you want to proceed?");

        if (!confirmed)
            return;

        CleanupBusy = true;
        CleanupStatus = "Starting...";

        Action<string> onStatus = status =>
            Dispatcher.UIThread.InvokeAsync(() => CleanupStatus = status);
        _cleanupRunner.StatusChanged += onStatus;

        try
        {
            var result = await Task.Run(() => _cleanupRunner.InstallAsync(_cts.Token));
            CleanupStatus = result.Message;
            CleanupInstalled = TabletDriverCleanupRunner.IsInstalled();
            await _dialogs.ShowMessageAsync("TabletDriverCleanup", result.Message);
        }
        catch (Exception ex)
        {
            CleanupStatus = $"Error: {ex.Message}";
        }
        finally
        {
            _cleanupRunner.StatusChanged -= onStatus;
            CleanupBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunCleanup()
    {
        if (!TabletDriverCleanupRunner.IsInstalled())
            return;

        var confirmed = await _dialogs.ShowConfirmAsync(
            "Run Driver Cleanup",
            "TabletDriverCleanup will open a terminal window and scan for leftover " +
            "manufacturer tablet drivers.\n\n" +
            "You'll be asked for admin permission. A restart may be needed afterward.\n\n" +
            "Do you want to proceed?");

        if (!confirmed)
            return;

        CleanupBusy = true;
        CleanupStatus = "Running...";

        Action<string> onStatus = status =>
            Dispatcher.UIThread.InvokeAsync(() => CleanupStatus = status);
        _cleanupRunner.StatusChanged += onStatus;

        try
        {
            var result = await Task.Run(() => _cleanupRunner.RunAsync(_cts.Token));
            CleanupStatus = result.Message;
            await _dialogs.ShowMessageAsync("Driver Cleanup", result.Message);
        }
        catch (Exception ex)
        {
            CleanupStatus = $"Error: {ex.Message}";
        }
        finally
        {
            _cleanupRunner.StatusChanged -= onStatus;
            CleanupBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallCleanup()
    {
        var confirmed = await _dialogs.ShowConfirmAsync(
            "Uninstall TabletDriverCleanup",
            $"This will remove the TabletDriverCleanup tool from:\n\n{CleanupInstallPath}\n\n" +
            "Do you want to proceed?");

        if (!confirmed)
            return;

        var result = _cleanupRunner.Uninstall();
        CleanupStatus = result.Message;
        CleanupInstalled = TabletDriverCleanupRunner.IsInstalled();
        await _dialogs.ShowMessageAsync("TabletDriverCleanup", result.Message);
    }

    public void Dispose()
    {
        // The shared DriverConflictMonitor is owned/disposed by the shell (MainViewModel).
        _cts.Cancel();
        _cts.Dispose();
    }
}
