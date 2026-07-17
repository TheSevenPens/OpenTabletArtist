using Avalonia;
using Avalonia.Controls;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>Hosts a single tablet's PEN settings (#pen-split) — the pen was split out of the tablet page
/// into its own top-level page. DataContext is a <see cref="TabletDetailViewModel"/> (shared with the
/// tablet page). Owns only the pen-behaviour deep-link (the health "Fix" for output mode → Movement); the
/// pen sections have no live pen-input coupling, so there's no sampling lifecycle here.</summary>
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
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Vm != null) Vm.TabRequested -= OnTabRequested;
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
