using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using OpenTabletArtist.Controls;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// The pen page's buttons tab (#1015): the pen picture and one card per barrel button. The only logic here is
/// the breakpoint: wide enough, the cards sit side by side and the picture draws a line to each; too narrow, they
/// stack and the lines go. The rule itself is <see cref="PenDiagramGeometry.Columns"/>, which is tested.
/// </summary>
public partial class PenButtonsView : UserControl
{
    private TabletDetailViewModel? _vm;

    public PenButtonsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Relayout();
        DataContextChanged += (_, _) => Watch(DataContext as TabletDetailViewModel);
    }

    private void Watch(TabletDetailViewModel? vm)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        Relayout();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabletDetailViewModel.PenButtonRows)) Relayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = null;
    }

    private void Relayout()
    {
        int count = _vm?.PenButtonRows.Count ?? 0;
        double width = Bounds.Width;
        bool sideBySide = width > 0 && PenDiagramGeometry.IsSideBySide(width, count);

        Diagram.ShowConnectors = sideBySide;
    }
}
