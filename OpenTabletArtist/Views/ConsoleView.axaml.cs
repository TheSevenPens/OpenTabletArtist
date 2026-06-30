using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class ConsoleView : UserControl
{
    private ConsoleViewModel? _vm;

    public ConsoleView() => InitializeComponent();

    // Hook the VM's scroll request for the dialog/page lifetime. The page view is rebuilt each time
    // the Console tab is navigated to, so attach/detach pair cleanly with no lingering subscription.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ConsoleViewModel vm)
        {
            _vm = vm;
            vm.ScrollToEndRequested += ScrollToEnd;
            ScrollToEnd(); // catch up to whatever's already buffered
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm != null)
        {
            _vm.ScrollToEndRequested -= ScrollToEnd;
            _vm = null;
        }
    }

    private void ScrollToEnd()
    {
        if (LogList.ItemCount > 0)
            LogList.ScrollIntoView(LogList.ItemCount - 1);
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConsoleViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.BuildLogText());
    }
}
