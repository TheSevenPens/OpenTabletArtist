using System.Collections.Generic;
using System.Linq;
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
public partial class PenDetailView : UserControl, ITabbedContent
{
    public PenDetailView()
    {
        InitializeComponent();
    }

    private TabletDetailViewModel? Vm => DataContext as TabletDetailViewModel;

    /// <inheritdoc />
    public IReadOnlyList<RadioButton> VisibleTabButtons() =>
        TabRail.Children.OfType<RadioButton>().Where(r => r.IsVisible).ToList();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Vm != null) Vm.TabRequested += OnTabRequested;
        if (Vm?.ConsumePendingTab() is { } pending) SelectTab(pending);

        // Live pen-pressure stream feeds the pressure dot + live bar on the Dynamics pivot; run it only
        // while that pivot is selected (#102).
        DynamicsTab.IsCheckedChanged += OnDynamicsTabChanged;
        ButtonsTab.PropertyChanged += OnButtonsTabPropertyChanged;
        LeaveButtonsTabIfHidden();
        UpdateLiveInput(); // start now if we opened on the dynamics pivot
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Vm != null) Vm.TabRequested -= OnTabRequested;
        DynamicsTab.IsCheckedChanged -= OnDynamicsTabChanged;
        ButtonsTab.PropertyChanged -= OnButtonsTabPropertyChanged;
        Vm?.StopLiveInput();
    }

    // The buttons pivot hides on a pen with no barrel buttons (switching tablets can do that while it is the
    // selected one). Its content is gated on the pivot being CHECKED, not visible, so a hidden-but-checked
    // pivot would leave a page with no visible tab selected: fall back to basics.
    private void OnButtonsTabPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty) LeaveButtonsTabIfHidden();
    }

    private void LeaveButtonsTabIfHidden()
    {
        if (!ButtonsTab.IsVisible && ButtonsTab.IsChecked == true) MovementTab.IsChecked = true;
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

    // The pen page carries the pen deep-links (#artist-pen-health): the pen-behaviour "Fix" (output mode)
    // and the tip/pressure health links both land on BASICS, which holds the movement and pressing choices
    // AND the pen-input opt-outs. Tilt still goes to pressure. Nothing deep-links to the buttons tab.
    private void SelectTab(TabletDetailTab tab)
    {
        switch (tab)
        {
            case TabletDetailTab.PenBehavior:
            case TabletDetailTab.PenInputs: MovementTab.IsChecked = true; break;
            case TabletDetailTab.PenDynamics: DynamicsTab.IsChecked = true; break;
        }
    }
}
