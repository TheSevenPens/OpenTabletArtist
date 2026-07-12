using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>The single TABLET page (#542): a switcher dropdown over the selected tablet's headerless
/// detail view. DataContext is a <see cref="ViewModels.TabletPageViewModel"/>.</summary>
public partial class TabletPageView : UserControl
{
    public TabletPageView() => InitializeComponent();
}
