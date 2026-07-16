using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The shared "tabbed page" host (Zune redesign, Phase 0 — see docs/design/zune-redesign.md): a
/// <see cref="ComplexHeader"/> title, a vertical data-driven tab rail, and a content host. Backs the
/// SETTINGS and ADVANCED pages, which were byte-for-byte identical hand-copied layouts. The DataContext
/// is the page view model, which must expose: <c>Tabs</c> (items with <c>Label</c> / <c>IsSelected</c> /
/// <c>IsVisible</c>), a <c>SelectTabCommand</c>, <c>SelectedContent</c>, and <c>CurrentTabTitle</c>. The
/// page entity name (e.g. "SETTINGS") is supplied by the host view via <see cref="Title"/>.
///
/// This is the single seam the redesign later swaps from a vertical rail to a horizontal Zune pivot —
/// changing this one control re-skins every tabbed page at once. Behaviour here is unchanged from the
/// old per-page views.
/// </summary>
public partial class TabbedPageView : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TabbedPageView, string?>(nameof(Title));

    /// <summary>The page entity name shown in the header (e.g. "SETTINGS", "ADVANCED").</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public TabbedPageView() => AvaloniaXamlLoader.Load(this);
}
