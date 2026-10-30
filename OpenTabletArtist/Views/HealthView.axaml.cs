using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class HealthView : UserControl
{
    public HealthView()
    {
        InitializeComponent();
        // Hidden developer affordance: right-clicking a synthetic ("developer-induced") card offers a
        // Dismiss. Real warnings raise no menu, so they can't be dismissed this way.
        AddHandler(ContextRequestedEvent, OnCardContextRequested, RoutingStrategies.Bubble);
    }

    private void OnCardContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control src || src.DataContext is not HealthIssue { IsDeveloperInduced: true } issue)
            return;
        if (DataContext is not HealthPageViewModel vm) return;

        var menu = new ContextMenu
        {
            ItemsSource = new[]
            {
                new MenuItem
                {
                    Header = "Dismiss (developer)",
                    Command = vm.DismissDeveloperIssueCommand,
                    CommandParameter = issue,
                },
            },
        };
        menu.Open(src);
        e.Handled = true;
    }
}
