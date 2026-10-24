using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>One pen barrel button on the PEN tab (#1015). DataContext is a
/// <see cref="ViewModels.PenButtonRowViewModel"/> bound to a slot by the pen view.</summary>
public partial class PenButtonCard : UserControl
{
    public PenButtonCard() => InitializeComponent();
}
