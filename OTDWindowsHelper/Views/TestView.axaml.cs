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
        // Canvas → VM: every sample (app or driver) updates the readouts.
        PaintCanvas.SampleObserved += OnCanvasSample;
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
            _vm.DriverSample += OnDriverSample;       // VM → canvas (driver mode)
            _vm.ClearRequested += OnClearRequested;
        }
    }

    private void OnCanvasSample(PenSample s) => _vm?.UpdateReadout(s);
    private void OnDriverSample(PenSample s) => PaintCanvas.AddSample(s);
    private void OnClearRequested() => PaintCanvas.Clear();
}
