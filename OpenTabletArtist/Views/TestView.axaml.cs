using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class TestView : UserControl
{
    private TestViewModel? _vm;

    public TestView()
    {
        InitializeComponent();
        // The pen position always comes from the daemon, never the OS pointer (#scribble-driver-only).
        PaintCanvas.IgnorePointer = true;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.DriverSample -= OnDriverSample;
            _vm.ClearRequested -= OnClearRequested;
        }
        _vm = DataContext as TestViewModel;
        if (_vm != null)
        {
            _vm.DriverSample += OnDriverSample;
            _vm.ClearRequested += OnClearRequested;
        }
    }

    // Driver stream: update readouts; in an Absolute mode, map the raw tablet position to the canvas
    // and paint under the pen. In a non-mappable (Relative) mode the canvas is disabled (note shown).
    private void OnDriverSample(PenSample s)
    {
        if (_vm is null) return;
        _vm.UpdateReadout(s);
        if (!_vm.DriverPositioned) return; // disabled state: readouts only

        if (_vm.MapRawToDesktop(s.RawX, s.RawY) is not { } desktop
            || !PenSampleMapping.TryDesktopToCanvasNormalized(PaintCanvas, desktop, out var nx, out var ny)
            || nx < 0 || nx > 1 || ny < 0 || ny > 1)
        {
            PaintCanvas.EndStroke(); // pen points outside the canvas region — break the stroke
            return;
        }

        PaintCanvas.AddSample(s with { X = nx, Y = ny, IsDown = s.Pressure > 0 });
        _vm.UpdateCanvasPosition(nx * PaintCanvas.Bounds.Width, ny * PaintCanvas.Bounds.Height);
    }

    // Typed nav creates a fresh TestView each visit; detach from the long-lived VM on unload so
    // discarded views don't keep handling driver samples / clears (Codex #94).
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_vm != null)
        {
            _vm.DriverSample -= OnDriverSample;
            _vm.ClearRequested -= OnClearRequested;
        }
    }

    private void OnClearRequested() => PaintCanvas.Clear();

    // Copy the current drawing to the clipboard as an image (Windows CF_DIB), so it can be pasted into
    // another app. A snapshot/clipboard failure surfaces a toast instead of silently doing nothing (#609).
    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (PaintCanvas.Snapshot() is not { } snap) return;
        if (!ClipboardImage.CopyBgra(snap.Bgra, snap.Width, snap.Height))
            ProfileToast.Show(ClipboardImage.FailureHint, "IconAlert");
    }
}
