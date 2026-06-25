using System.ComponentModel;
using Avalonia.Controls;
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

    // The canvas always draws from the pointer; its samples update the readouts only in App mode
    // (in Driver mode the daemon stream drives the readouts instead).
    private void OnCanvasSample(PenSample s)
    {
        if (_vm is { UseDriverInput: false }) _vm.UpdateReadout(s);
    }

    // Driver stream: update the readouts and feed the canvas the daemon's pressure/tilt so the
    // (pointer-positioned) stroke reflects the raw driver signal.
    private void OnDriverSample(PenSample s)
    {
        if (_vm is not { UseDriverInput: true }) return; // ignore late samples after toggling off
        _vm.UpdateReadout(s);
        PaintCanvas.SetDriverDynamics(s.Pressure, s.TiltX, s.TiltY, s.Twist);
    }

    private void OnClearRequested() => PaintCanvas.Clear();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TestViewModel.UseDriverInput) && _vm is { UseDriverInput: false })
            PaintCanvas.ClearDriverDynamics();
    }
}
