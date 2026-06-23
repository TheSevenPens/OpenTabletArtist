using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using OtdWindowsHelper.ViewModels;

namespace OtdWindowsHelper.Views;

public partial class TabletSettingsView : UserControl
{
    public TabletSettingsView()
    {
        InitializeComponent();
    }

    private void ProfileCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is { } profileItem)
        {
            var itemsControl = border.FindAncestorOfType<ItemsControl>();
            var vm = itemsControl?.DataContext as TabletSettingsViewModel;
            vm?.OpenTabletSettingsCommand.Execute(profileItem);
        }
    }
}
