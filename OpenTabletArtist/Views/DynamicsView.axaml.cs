using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.Controls;
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
        PreviewCanvas2.IgnorePointer = true;
        // Delete/Backspace on one canvas clears the other too — they are one drawing shown twice.
        PreviewCanvas.Cleared += PreviewCanvas2.Clear;
        PreviewCanvas2.Cleared += PreviewCanvas.Clear;
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
    // Off-canvas points break the stroke.
    //
    // There are two same-sized canvases and the pen is only ever over one of them, so the sample is mapped
    // against whichever one it lands on and the same normalized position is then stamped on BOTH — drawing
    // in either canvas does the same thing to the other. They differ only in pressure: the first always
    // draws the raw pressure, the second the pressure after the curve + smoothing.
    private void OnPreviewSample(PenSample s, double processedPressure)
    {
        if (_vm?.MapRawToDesktop(s.RawX, s.RawY) is { } desktop
            && (TryLand(PreviewCanvas, desktop, out var nx, out var ny)
                || TryLand(PreviewCanvas2, desktop, out nx, out ny)))
        {
            var raw = s with { X = nx, Y = ny };
            PreviewCanvas.AddSample(WithInk(raw, raw.Pressure));
            PreviewCanvas2.AddSample(WithInk(raw, processedPressure));
            return;
        }

        PreviewCanvas.EndStroke();
        PreviewCanvas2.EndStroke();
    }

    // The brush never draws thinner than 1px, so a pen-down sample at zero pressure would still leave a mark.
    // Some pens report pressure while hovering, and the curve's dead zone (bottom-left node moved right) is how
    // that gets silenced — so a canvas's pressure of zero must mean no ink, not a hairline. Treating it as
    // pen-up also ends the stroke, so the line doesn't join across the gap.
    private static PenSample WithInk(PenSample s, double pressure) =>
        s with { Pressure = pressure, IsDown = s.IsDown && pressure > 0 };

    private static bool TryLand(PenTestCanvas canvas, Vector2 desktop, out double nx, out double ny) =>
        PenSampleMapping.TryDesktopToCanvasNormalized(canvas, desktop, out nx, out ny)
        && nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1;

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
    private void OnClearPreview(object? sender, RoutedEventArgs e)
    {
        PreviewCanvas.Clear();
        PreviewCanvas2.Clear();
    }

    // Copy the pressure-preview drawing to the clipboard (the two canvases hold the same drawing). A clipboard failure surfaces a toast rather than
    // silently doing nothing — most likely no wl-copy/xclip on a minimal Linux install (#609).
    private void OnCopyPreview(object? sender, RoutedEventArgs e)
    {
        if (PreviewCanvas.Snapshot() is not { } snap) return;
        if (!ClipboardImage.CopyBgra(snap.Bgra, snap.Width, snap.Height))
            ProfileToast.Show(ClipboardImage.FailureHint, "IconAlert");
    }
}
