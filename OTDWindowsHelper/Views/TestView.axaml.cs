using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.ViewModels;

namespace OtdWindowsHelper.Views;

public partial class TestView : UserControl
{
    private TestViewModel? _vm;

    public TestView()
    {
        InitializeComponent();
        PaintCanvas.SampleObserved += OnCanvasSample;
        DataContextChanged += OnDataContextChanged;
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
        }
    }

    // The canvas always draws from the pointer. Its samples drive the Canvas X/Y readout in both
    // modes (that's where the stroke lands); the source-dependent readouts come from the canvas
    // sample only in App mode — in Driver mode the daemon stream drives them.
    private void OnCanvasSample(PenSample s)
    {
        if (_vm is null) return;
        _vm.UpdateCanvasPosition(s.RawX, s.RawY); // RawX/RawY of a pointer sample = canvas DIPs
        if (!_vm.UseDriverInput) _vm.UpdateReadout(s);
    }

    // Driver stream: update the readouts and feed the canvas the daemon's pressure/tilt so the
    // (pointer-positioned) stroke reflects the raw driver signal.
    private void OnDriverSample(PenSample s)
    {
        if (_vm is not { UseDriverInput: true }) return; // ignore late samples after toggling off
        _vm.UpdateReadout(s);
        PaintCanvas.SetDriverDynamics(s.Pressure, s.TiltX, s.TiltY, s.Twist);
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

    private void OnClearRequested() => PaintCanvas.Clear();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TestViewModel.UseDriverInput) && _vm is { UseDriverInput: false })
            PaintCanvas.ClearDriverDynamics();
    }
}
