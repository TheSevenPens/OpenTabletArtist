using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class LogView : UserControl
{
    private LogViewModel? _vm;

    public LogView() => InitializeComponent();

    // Hook the VM's scroll request for the dialog/page lifetime. The page view is rebuilt each time
    // the Console tab is navigated to, so attach/detach pair cleanly with no lingering subscription.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is LogViewModel vm)
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

    private LogViewModel? Vm => DataContext as LogViewModel;

    private async void CopyText_Click(object? sender, RoutedEventArgs e) => await CopyAsync(Vm?.BuildLogText());
    private async void CopyMarkdown_Click(object? sender, RoutedEventArgs e) => await CopyAsync(Vm?.BuildLogMarkdown());
    private async void CopyHtml_Click(object? sender, RoutedEventArgs e) => await CopyAsync(Vm?.BuildLogHtml());

    private async Task CopyAsync(string? text)
    {
        if (text != null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
