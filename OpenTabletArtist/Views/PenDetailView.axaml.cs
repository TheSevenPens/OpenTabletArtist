using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>Hosts a single tablet's PEN settings (#pen-split) — the pen was split out of the tablet page
/// into its own top-level page. DataContext is a <see cref="TabletDetailViewModel"/> (shared with the
/// tablet page). Owns the pen-behaviour deep-link (the health "Fix" for output mode → Movement) and, since
/// the Dynamics pivot moved here (#pen-dynamics-move), the live pen-pressure stream lifecycle while that
/// pivot is visible (same treatment the tablet page used for its Dynamics tab, #102).</summary>
public partial class PenDetailView : UserControl
{
    public PenDetailView()
    {
        InitializeComponent();
    }

    private TabletDetailViewModel? Vm => DataContext as TabletDetailViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Vm != null) Vm.TabRequested += OnTabRequested;
        if (Vm?.ConsumePendingTab() is { } pending) SelectTab(pending);

        // Live pen-pressure stream feeds the pressure dot + live bar on the Dynamics pivot; run it only
        // while that pivot is selected (#102).
        DynamicsTab.IsCheckedChanged += OnDynamicsTabChanged;
        UpdateLiveInput(); // start now if we opened on the dynamics pivot
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Vm != null) Vm.TabRequested -= OnTabRequested;
        DynamicsTab.IsCheckedChanged -= OnDynamicsTabChanged;
        Vm?.StopLiveInput();
    }

    private void OnDynamicsTabChanged(object? sender, RoutedEventArgs e) => UpdateLiveInput();

    private void UpdateLiveInput()
    {
        if (Vm is not { } vm) return;
        if (DynamicsTab.IsChecked == true) vm.StartLiveInput();
        else vm.StopLiveInput();
    }

    // A deep-link arriving while the page is already shown — clear the pending flag and switch pivots.
    private void OnTabRequested(TabletDetailTab tab)
    {
        Vm?.ConsumePendingTab();
        SelectTab(tab);
    }

    // The pen page carries the pen-behaviour deep-link (health "Fix" for output mode) → the Movement pivot.
    private void SelectTab(TabletDetailTab tab)
    {
        switch (tab)
        {
            case TabletDetailTab.PenBehavior: MovementTab.IsChecked = true; break;
        }
    }
}
