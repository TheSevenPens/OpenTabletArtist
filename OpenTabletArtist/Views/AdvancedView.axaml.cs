using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>
/// The ADVANCED tabbed page. Fully data-driven (#477): the tab rail is an ItemsControl over the view
/// model's tab lists and the content host binds to <c>SelectedContent</c>, so there's no per-tab
/// code-behind. The daemon debug stream is stopped by the view model when the Diagnostics tab is left,
/// and by the shell when the page is left.
/// </summary>
public partial class AdvancedView : UserControl
{
    public AdvancedView() => InitializeComponent();
}
