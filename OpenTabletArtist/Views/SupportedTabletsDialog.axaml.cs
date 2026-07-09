using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>The Supported Tablets dialog (#155): the built-in OTD catalog, searchable, with the
/// connected tablet highlighted and scrolled into view.</summary>
public partial class SupportedTabletsDialog : Window
{
    public SupportedTabletsDialog()
    {
        InitializeComponent();
    }

    private SupportedTabletsDialog(SupportedTabletsDialogViewModel vm) : this()
    {
        DataContext = vm;
        Opened += (_, _) => ScrollDetectedIntoView(vm);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // A column header was clicked → sort by it (the Tag names the column).
    private void OnHeaderClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string column } && DataContext is SupportedTabletsDialogViewModel vm)
            vm.SortByCommand.Execute(column);
    }

    private void ScrollDetectedIntoView(SupportedTabletsDialogViewModel vm)
    {
        int idx = vm.Rows.FindIndex(r => r.IsDetected);
        if (idx < 0) return;
        // Let the item containers realize (the list isn't virtualized) before bringing the row into view.
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ItemsControl>("List")?.ContainerFromIndex(idx) is { } container)
                container.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Open the dialog modally over <paramref name="owner"/>. Pass the connected tablet's name
    /// to highlight (and scroll to) it in the list.</summary>
    public static async Task ShowAsync(Window owner, string? detectedName)
    {
        var dialog = new SupportedTabletsDialog(new SupportedTabletsDialogViewModel(detectedName));
        await dialog.ShowDialog(owner);
    }
}
