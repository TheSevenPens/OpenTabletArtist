using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Thin Window wrapper that hosts <see cref="TabletDetailView"/> for the tray's focused "Pen
/// Dynamics" editor (#133). The in-app Tablets navigation hosts the same view+VM as a page; this
/// dialog remains only for the tray's quick dynamics access. (Settings logic lives in
/// <see cref="TabletDetailViewModel"/>.)
/// </summary>
public partial class TabletSettingsDialog : Window
{
    // Session device-data source, used to live-refresh the detection banner while the dialog is open
    // (#177). Null in design-time / test paths, in which case detection is only read at open + Refresh.
    private readonly IDeviceData? _deviceData;

    public TabletSettingsDialog(Profile profile, Settings? settings,
        Func<Settings, Task>? onApplyChanges = null,
        Func<Task<(Settings? Settings, Profile? Profile)>>? onRefresh = null,
        (float Width, float Height)? tabletDigitizer = null,
        bool dynamicsOnly = false,
        OpenTabletArtist.Services.IDaemonDebugSession? penInput = null,
        Func<bool>? isDetected = null,
        Func<Window, ViewModels.CalibrationOptions, Task>? onCalibrate = null,
        IDeviceData? deviceData = null)
    {
        InitializeComponent();
        _deviceData = deviceData;
        var vm = new TabletDetailViewModel(profile, settings, onApplyChanges, onRefresh, tabletDigitizer, penInput, isDetected, dynamicsOnly);
        DataContext = vm;
        if (onCalibrate != null)
            // Open the calibration overlay owned by this dialog (#127). The VM raises the request;
            // the View owns window creation. After it closes, reload so the stale-calibration hint
            // and settings stay coherent (#147).
            vm.CalibrationRequested += async () =>
            {
                await onCalibrate(this, vm.CalibrationOptions);
                await vm.RefreshCommand.ExecuteAsync(null);
            };
        if (dynamicsOnly)
        {
            // Focused Pen Dynamics editor (#133): preselect the Dynamics tab; the tab bar is hidden
            // via the VM's DynamicsOnly flag, and a smaller window suits the single panel.
            DynamicsTab.IsChecked = true;
            Title = "Pen Dynamics";
            Height = 560;
        }
    }

    // Parameterless constructor required by Avalonia XAML loader
    public TabletSettingsDialog() { InitializeComponent(); }

    // Live-refresh the display list when a monitor is added/removed/rearranged while the dialog is
    // open (#95 follow-up). Scoped to the dialog's lifetime — no lingering hooks.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Screens != null) Screens.Changed += OnScreensChanged;
        DynamicsTab.IsCheckedChanged += OnDynamicsTabChanged;
        // Live-refresh the detection banner + tablet-dependent actions when the daemon reports a
        // tablet add/remove while the dialog is open (#177). DataLoaded is raised on the UI thread.
        if (_deviceData != null) _deviceData.DataLoaded += OnSessionDataLoaded;
        UpdateLivePressure(); // start the live dot if the dialog opened on the Dynamics tab
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (Screens != null) Screens.Changed -= OnScreensChanged;
        DynamicsTab.IsCheckedChanged -= OnDynamicsTabChanged;
        if (_deviceData != null) _deviceData.DataLoaded -= OnSessionDataLoaded;
        (DataContext as TabletDetailViewModel)?.StopLivePressure();
    }

    private void OnSessionDataLoaded() =>
        (DataContext as TabletDetailViewModel)?.RefreshDetectionStatus();

    private void OnScreensChanged(object? sender, EventArgs e) =>
        (DataContext as TabletDetailViewModel)?.RefreshDisplaysCommand.Execute(null);

    // Only stream the live pen-pressure dot while the Dynamics tab is visible (#102).
    private void OnDynamicsTabChanged(object? sender, RoutedEventArgs e) => UpdateLivePressure();

    private void UpdateLivePressure()
    {
        if (DataContext is not TabletDetailViewModel vm) return;
        if (DynamicsTab.IsChecked == true) vm.StartLivePressure();
        else vm.StopLivePressure();
    }
}
