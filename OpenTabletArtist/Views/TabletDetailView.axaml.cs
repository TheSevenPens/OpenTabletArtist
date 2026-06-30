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

    public TabletDetailView() => InitializeComponent();

    private TabletDetailViewModel? Vm => DataContext as TabletDetailViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Focused Pen Dynamics editor (#133): open straight on the Dynamics tab.
        if (Vm?.DynamicsOnly == true) DynamicsTab.IsChecked = true;

        _screens = TopLevel.GetTopLevel(this)?.Screens;
        if (_screens != null) _screens.Changed += OnScreensChanged;
        DynamicsTab.IsCheckedChanged += OnDynamicsTabChanged;
        UpdateLivePressure(); // start the live dot if we opened on the Dynamics tab
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_screens != null) { _screens.Changed -= OnScreensChanged; _screens = null; }
        DynamicsTab.IsCheckedChanged -= OnDynamicsTabChanged;
        Vm?.StopLivePressure();
    }

    private void OnScreensChanged(object? sender, EventArgs e) =>
        Vm?.RefreshDisplaysCommand.Execute(null);

    private void OnDynamicsTabChanged(object? sender, RoutedEventArgs e) => UpdateLivePressure();

    private void UpdateLivePressure()
    {
        if (Vm is not { } vm) return;
        if (DynamicsTab.IsChecked == true) vm.StartLivePressure();
        else vm.StopLivePressure();
    }
}
