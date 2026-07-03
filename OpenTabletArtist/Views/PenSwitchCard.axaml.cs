using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>One switch callout in the pen diagram (#pen-switch-diagram). DataContext is a
/// <see cref="ViewModels.PenSwitchRowViewModel"/> bound to a slot by the diagram.</summary>
public partial class PenSwitchCard : UserControl
{
    public PenSwitchCard() => InitializeComponent();
}
