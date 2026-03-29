using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabletDriverUX.ViewModels;

namespace TabletDriverUX.Views;

public partial class TabletSettingsView : UserControl
{
    public TabletSettingsView()
    {
        InitializeComponent();
    }

    private void ProfileCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is { } profile)
        {
            var itemsControl = border.FindAncestorOfType<ItemsControl>();
            var vm = itemsControl?.DataContext as MainViewModel;
            vm?.OpenTabletSettingsCommand.Execute(profile);
        }
    }
}
