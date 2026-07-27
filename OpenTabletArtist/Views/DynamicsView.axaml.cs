using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class DynamicsView : UserControl
{
    private TabletDetailViewModel? _vm;

    public DynamicsView()
    {
        InitializeComponent();
        // The preview canvas paints from the daemon's pen stream (PreviewSample), not the OS pointer — so
        // it reflects the raw driver signal shaped by the curve + smoothing, and works even when Windows
        // Ink is off. Ignore pointer input so the two sources don't both draw.
        PreviewCanvas.IgnorePointer = true;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.PreviewSample -= OnPreviewSample;
        _vm = DataContext as TabletDetailViewModel;
        if (_vm != null) _vm.PreviewSample += OnPreviewSample;
    }

    // Mirror the Scribble page's Driver mode (TestView.OnDriverSample): map the raw tablet position to a
    // virtual-desktop pixel, then to this canvas's on-screen rectangle, so the stroke lands under the pen.
    // Pressure is already curve + smoothing shaped by the view model. Off-canvas points break the stroke.
    private void OnPreviewSample(PenSample s)
    {
        if (_vm?.MapRawToDesktop(s.RawX, s.RawY) is not { } desktop
            || !PenSampleMapping.TryDesktopToCanvasNormalized(PreviewCanvas, desktop, out var nx, out var ny)
            || nx < 0 || nx > 1 || ny < 0 || ny > 1)
        {
            PreviewCanvas.EndStroke();
            return;
        }

        PreviewCanvas.AddSample(s with { X = nx, Y = ny });
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_vm != null)
        {
            _vm.PreviewSample -= OnPreviewSample;
            _vm = null;
        }
    }

    // Clear/Copy mirror the Scribble page's buttons.
    private void OnClearPreview(object? sender, RoutedEventArgs e) => PreviewCanvas.Clear();

    // Copy the pressure-preview drawing to the clipboard. A clipboard failure surfaces a toast rather than
    // silently doing nothing — most likely no wl-copy/xclip on a minimal Linux install (#609).
    private void OnCopyPreview(object? sender, RoutedEventArgs e)
    {
        if (PreviewCanvas.Snapshot() is not { } snap) return;
        if (!ClipboardImage.CopyBgra(snap.Bgra, snap.Width, snap.Height))
            ProfileToast.Show(ClipboardImage.FailureHint, "IconAlert");
    }
}
