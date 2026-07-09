using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using OpenTabletArtist.Controls;
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

    /// <summary>The tab rail's currently-visible tab buttons, in order (#437: the screenshot sweep checks
    /// each in turn to capture every sub-tab of a connected tablet).</summary>
    public IReadOnlyList<RadioButton> VisibleTabButtons() =>
        TabRail.Children.OfType<RadioButton>().Where(r => r.IsVisible).ToList();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Focused Pen Dynamics editor (#133): open straight on the Dynamics tab.
        if (Vm?.DynamicsOnly == true) PressureDynamicsTab.IsChecked = true;

        // Health-issue "Fix" deep-link: open on the tab that carries the fix (e.g. Display Mapping for an
        // off-screen mapping) instead of the default About tab. Also handle a request that arrives while
        // the page is already shown (re-navigation wouldn't re-attach the view).
        if (Vm != null) Vm.TabRequested += OnTabRequested;
        if (Vm?.ConsumePendingTab() is { } pending) SelectTab(pending);

        _screens = TopLevel.GetTopLevel(this)?.Screens;
        if (_screens != null) _screens.Changed += OnScreensChanged;
        // Live device-report stream feeds the pressure dot (Dynamics), the aux-button highlight
        // (ExpressKeys), and the wheel gauge (Wheel), so watch those tabs.
        PressureDynamicsTab.IsCheckedChanged += OnLiveTabChanged;
        PenButtonsTab.IsCheckedChanged += OnLiveTabChanged;
        WheelTab.IsCheckedChanged += OnLiveTabChanged;
        UpdateLiveInput(); // start now if we opened on a live tab

        // Interactive active-area edits from the diagram → persist through the VM (#199).
        ActiveAreaDiagramControl.AreaCommitted += OnActiveAreaCommitted;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Vm != null) Vm.TabRequested -= OnTabRequested;
        if (_screens != null) { _screens.Changed -= OnScreensChanged; _screens = null; }
        PressureDynamicsTab.IsCheckedChanged -= OnLiveTabChanged;
        PenButtonsTab.IsCheckedChanged -= OnLiveTabChanged;
        WheelTab.IsCheckedChanged -= OnLiveTabChanged;
        ActiveAreaDiagramControl.AreaCommitted -= OnActiveAreaCommitted;
        Vm?.StopLiveInput();
    }

    private void OnActiveAreaCommitted(object? sender, ActiveAreaEdit e) =>
        _ = Vm?.CommitActiveArea(e.Width, e.Height, e.CenterX, e.CenterY);

    private void OnScreensChanged(object? sender, EventArgs e) =>
        Vm?.RefreshDisplaysCommand.Execute(null);

    // A deep-link arriving while the page is already shown — clear the pending flag (we're handling it
    // live) and switch tabs.
    private void OnTabRequested(TabletDetailTab tab)
    {
        Vm?.ConsumePendingTab();
        SelectTab(tab);
    }

    // Check the RadioButton for a deep-linked tab. Its content ScrollViewer is gated on IsChecked, so
    // checking the tab shows it (and unchecks whatever was selected — they share the rail's group).
    private void SelectTab(TabletDetailTab tab)
    {
        switch (tab)
        {
            case TabletDetailTab.DisplayMapping: DisplayMappingTab.IsChecked = true; break;
            case TabletDetailTab.PenBehavior: OutputModeTab.IsChecked = true; break;
        }
    }

    private void OnLiveTabChanged(object? sender, RoutedEventArgs e) => UpdateLiveInput();

    private void UpdateLiveInput()
    {
        if (Vm is not { } vm) return;
        if (PressureDynamicsTab.IsChecked == true || PenButtonsTab.IsChecked == true
            || WheelTab.IsChecked == true) vm.StartLiveInput();
        else vm.StopLiveInput();
    }
}
