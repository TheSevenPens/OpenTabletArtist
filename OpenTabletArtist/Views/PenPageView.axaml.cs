using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>Host for the PEN page (#pen-split): shows the selected tablet's <see cref="PenDetailView"/>,
/// or a hero when no tablet is known. DataContext is a PenPageViewModel.</summary>
public partial class PenPageView : UserControl
{
    public PenPageView()
    {
        InitializeComponent();
    }
}
