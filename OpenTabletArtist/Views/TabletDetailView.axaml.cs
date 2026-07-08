using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Hosts a single tablet's tabbed settings (DataContext is a <see cref="TabletDetailViewModel"/>).
/// Owns the view-side lifecycle the old dialog used to: stream the live pen-pressure dot only while
/// the Dynamics tab is visible (#102), refresh the display list when monitors change (#95), and
/// preselect the Dynamics tab in the focused editor (#133). Scoped to the view's attach/detach, so it
/// works whether hosted as the in-app page or in the tray dialog.
/// </summary>
public partial class TabletDetailView : UserControl
{
    private Screens? _screens;

    public TabletDetailView()
    {
        InitializeComponent();
    }

    private TabletDetailViewModel? Vm => DataContext as TabletDetailViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Focused Pen Dynamics editor (#133): open straight on the Dynamics tab.
        if (Vm?.DynamicsOnly == true) PressureDynamicsTab.IsChecked = true;

        _screens = TopLevel.GetTopLevel(this)?.Screens;
        if (_screens != null) _screens.Changed += OnScreensChanged;
        // Live device-report stream feeds the pressure dot (Dynamics), the aux-button highlight
        // (ExpressKeys), and the wheel gauge (Wheel), so watch those tabs.
        PressureDynamicsTab.IsCheckedChanged += OnLiveTabChanged;
        PenButtonsTab.IsCheckedChanged += OnLiveTabChanged;
        WheelTab.IsCheckedChanged += OnLiveTabChanged;
        UpdateLiveInput(); // start now if we opened on a live tab
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_screens != null) { _screens.Changed -= OnScreensChanged; _screens = null; }
        PressureDynamicsTab.IsCheckedChanged -= OnLiveTabChanged;
        PenButtonsTab.IsCheckedChanged -= OnLiveTabChanged;
        WheelTab.IsCheckedChanged -= OnLiveTabChanged;
        Vm?.StopLiveInput();
    }

    private void OnScreensChanged(object? sender, EventArgs e) =>
        Vm?.RefreshDisplaysCommand.Execute(null);

    private void OnLiveTabChanged(object? sender, RoutedEventArgs e) => UpdateLiveInput();

    private void UpdateLiveInput()
    {
        if (Vm is not { } vm) return;
        if (PressureDynamicsTab.IsChecked == true || PenButtonsTab.IsChecked == true
            || WheelTab.IsChecked == true) vm.StartLiveInput();
        else vm.StopLiveInput();
    }
}
