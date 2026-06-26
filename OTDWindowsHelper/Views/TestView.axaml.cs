using System.ComponentModel;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.ViewModels;

namespace OtdWindowsHelper.Views;

public partial class TestView : UserControl
{
    private TestViewModel? _vm;
    // In-app dynamics for the split "With dynamics" canvas (#134). Stateful per stroke (EMA smoothing),
    // reset on pen-up so a new stroke starts crisp.
    private readonly PenDynamicsProcessor _processor = new();

    public TestView()
    {
        InitializeComponent();
        PaintCanvas.SampleObserved += OnCanvasSample;
        ProcessedCanvas.IgnorePointer = true; // display-only; fed programmatically from the raw stroke
        DataContextChanged += OnDataContextChanged;
    }

    // Mirror every sample drawn on the raw canvas onto the processed canvas with this tablet's
    // dynamics applied (curve + smoothing). No-op unless the split view is on.
    private void MirrorToProcessed(PenSample s)
    {
        if (_vm is null || !_vm.ShowSplit) return;
        _processor.Settings = _vm.CurrentDynamics;
        if (!s.IsDown)
        {
            _processor.Reset();          // pen up — clear smoothing state for the next stroke
            ProcessedCanvas.AddSample(s); // also ends the processed stroke
            return;
        }
        var (sx, sy) = _processor.ProcessPosition(s.X, s.Y);
        var p = _processor.ProcessPressure(s.Pressure);
        ProcessedCanvas.AddSample(s with { X = sx, Y = sy, Pressure = p });
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.DriverSample -= OnDriverSample;
            _vm.ClearRequested -= OnClearRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as TestViewModel;
        if (_vm != null)
        {
            _vm.DriverSample += OnDriverSample;
            _vm.ClearRequested += OnClearRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
            PaintCanvas.IgnorePointer = _vm.UseDriverInput; // Driver mode: position comes from the daemon
        }
    }

    // App mode only: the pointer paints, and its sample (RawX/RawY = canvas DIPs) drives the readouts
    // + Canvas X/Y. In Driver mode OnDriverSample owns the readouts/position.
    private void OnCanvasSample(PenSample s)
    {
        // Runs for every sample drawn on the raw canvas (pointer in App mode, pushed in Driver mode),
        // so mirror to the processed canvas here regardless of source.
        MirrorToProcessed(s);
        if (_vm is null || _vm.UseDriverInput) return;
        _vm.UpdateCanvasPosition(s.RawX, s.RawY);
        _vm.UpdateReadout(s);
    }

    // Driver stream: update readouts; in an Absolute mode, map the raw tablet position to the canvas
    // and paint under the pen. In a non-mappable (Relative) mode the canvas is disabled (note shown).
    private void OnDriverSample(PenSample s)
    {
        if (_vm is not { UseDriverInput: true }) return; // ignore late samples after toggling off
        _vm.UpdateReadout(s);
        if (!_vm.DriverPositioned) return; // disabled state: readouts only

        if (_vm.MapRawToDesktop(s.RawX, s.RawY) is not { } desktop
            || !TryDesktopToCanvasNormalized(desktop, out var nx, out var ny)
            || nx < 0 || nx > 1 || ny < 0 || ny > 1)
        {
            PaintCanvas.EndStroke(); // pen points outside the canvas region — break the stroke
            return;
        }

        PaintCanvas.AddSample(s with { X = nx, Y = ny, IsDown = s.Pressure > 0 });
        _vm.UpdateCanvasPosition(nx * PaintCanvas.Bounds.Width, ny * PaintCanvas.Bounds.Height);
    }

    // Virtual-desktop pixel → canvas-local normalized (0..1). PointToScreen + RenderScaling are
    // physical px (same space OTD maps into); divide by scaling to DIPs, then by the canvas size.
    private bool TryDesktopToCanvasNormalized(Vector2 desktopPx, out double nx, out double ny)
    {
        nx = ny = 0;
        double w = PaintCanvas.Bounds.Width, h = PaintCanvas.Bounds.Height;
        if (w <= 0 || h <= 0 || TopLevel.GetTopLevel(PaintCanvas) is not { } top) return false;
        var origin = PaintCanvas.PointToScreen(new Point(0, 0));
        nx = (desktopPx.X - origin.X) / top.RenderScaling / w;
        ny = (desktopPx.Y - origin.Y) / top.RenderScaling / h;
        return true;
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
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
    }

    private void OnClearRequested()
    {
        PaintCanvas.Clear();
        ProcessedCanvas.Clear();
        _processor.Reset();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        if (e.PropertyName == nameof(TestViewModel.UseDriverInput))
        {
            PaintCanvas.IgnorePointer = _vm.UseDriverInput;
            PaintCanvas.EndStroke(); // don't connect a stroke across an input-source switch
            ProcessedCanvas.EndStroke();
            _processor.Reset();
        }
        else if (e.PropertyName == nameof(TestViewModel.ShowSplit))
        {
            // Don't carry a stroke across the toggle; start the comparison fresh.
            ProcessedCanvas.EndStroke();
            _processor.Reset();
        }
    }
}
