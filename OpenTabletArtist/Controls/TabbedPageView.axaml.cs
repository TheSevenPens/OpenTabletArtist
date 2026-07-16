using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The shared "tabbed page" host (Zune redesign — see docs/design/zune-redesign.md): a horizontal pivot
/// row over the selected subpage. Backs the SETTINGS and ADVANCED pages, which were byte-for-byte
/// identical hand-copied layouts. The DataContext is the page view model, which must expose: <c>Tabs</c>
/// (items with <c>Label</c> / <c>IsSelected</c> / <c>IsVisible</c>), a <c>SelectTabCommand</c>, and
/// <c>SelectedContent</c>. There's no page-entity title — the highlighted wordmark in the top bar already
/// names the section. Editing this one control re-skins every tabbed page at once.
/// </summary>
public partial class TabbedPageView : UserControl
{
    public TabbedPageView() => AvaloniaXamlLoader.Load(this);
}
