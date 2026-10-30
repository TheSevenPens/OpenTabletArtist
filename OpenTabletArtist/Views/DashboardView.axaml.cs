using Avalonia.Controls;
using Avalonia.Input;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    /// <summary>Double-clicking a tablet card opens that tablet's settings — the per-card "edit" button
    /// was removed in favour of this (#tablet-card-mapping). A double-click on the trash icon is
    /// handled by the button itself and won't reach here as a card activation.</summary>
    private void OnTabletCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: TabletOverviewItemViewModel item } && item.OpenCommand.CanExecute(null))
        {
            item.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }
}
