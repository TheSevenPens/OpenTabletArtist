using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>Hotkeys page (#89): manages the global monitor-cycle hotkey and the per-snapshot
/// profile-switch hotkeys. DataContext is a <see cref="ViewModels.HotkeysViewModel"/>.</summary>
public partial class HotkeysView : UserControl
{
    public HotkeysView() => InitializeComponent();
}
