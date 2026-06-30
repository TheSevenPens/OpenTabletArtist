using System;
using Avalonia.Controls;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Thin Window wrapper that hosts <see cref="TabletDetailView"/> for the tray's focused "Pen
/// Dynamics" editor (#133). The in-app Tablets navigation hosts the same view+VM as a page; this
/// dialog remains only for the tray's quick dynamics access. All settings logic + the view-side
/// lifecycle (live pressure, displays refresh, detection) live in <see cref="TabletDetailViewModel"/>
/// / <see cref="TabletDetailView"/>; this just sizes the window and disposes the VM on close.
/// </summary>
public partial class TabletSettingsDialog : Window
{
    public TabletSettingsDialog(Profile profile, Settings? settings,
        Func<Settings, Task>? onApplyChanges = null,
        Func<Task<(Settings? Settings, Profile? Profile)>>? onRefresh = null,
        (float Width, float Height)? tabletDigitizer = null,
        bool dynamicsOnly = false,
        IDaemonDebugSession? penInput = null,
        Func<bool>? isDetected = null,
        Func<Window, ViewModels.CalibrationOptions, Task>? onCalibrate = null,
        IDeviceData? deviceData = null)
    {
        InitializeComponent();
        var vm = new TabletDetailViewModel(profile, settings, onApplyChanges, onRefresh,
            tabletDigitizer, penInput, isDetected, dynamicsOnly, deviceData);
        DataContext = vm; // inherited by the hosted TabletDetailView
        if (onCalibrate != null)
            // Open the calibration overlay owned by this dialog (#127); reload afterward so the
            // stale-calibration hint and settings stay coherent (#147).
            vm.CalibrationRequested += async () =>
            {
                await onCalibrate(this, vm.CalibrationOptions);
                await vm.RefreshCommand.ExecuteAsync(null);
            };
        if (dynamicsOnly)
        {
            // Focused Pen Dynamics editor (#133): the view preselects/hides tabs via the VM's
            // DynamicsOnly flag; a smaller window suits the single panel.
            Title = "Pen Dynamics";
            Height = 560;
        }
    }

    // Parameterless constructor required by Avalonia XAML loader
    public TabletSettingsDialog() { InitializeComponent(); }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as TabletDetailViewModel)?.Dispose();
    }
}
