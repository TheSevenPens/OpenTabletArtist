using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

public partial class DynamicsView : UserControl
{
    public DynamicsView() => InitializeComponent();

    // The right-column preview canvas draws from the OS pointer (App mode), so its Windows Ink pressure is
    // already shaped by the pressure filter — letting you feel the curve you're editing. Clear/Copy mirror
    // the Scribble page's buttons.
    private void OnClearPreview(object? sender, RoutedEventArgs e) => PreviewCanvas.Clear();

    private void OnCopyPreview(object? sender, RoutedEventArgs e)
    {
        if (PreviewCanvas.Snapshot() is { } snap)
            ClipboardImage.CopyBgra(snap.Bgra, snap.Width, snap.Height);
    }
}
