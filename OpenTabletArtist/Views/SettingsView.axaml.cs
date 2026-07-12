using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>
/// The SETTINGS tabbed page. Fully data-driven: the tab rail is an ItemsControl over the view model's
/// flat <c>Tabs</c> list and the content host binds to <c>SelectedContent</c>, so there's no per-tab
/// code-behind. Hosts OpenTabletArtist's preference subpages (Startup, Developer, Theme).
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}
