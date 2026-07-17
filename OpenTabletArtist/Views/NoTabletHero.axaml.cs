using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>The shared "no tablet yet" empty-state hero for the Tablet + Pen pages (#580). Only the
/// <see cref="Description"/> line differs between them.</summary>
public partial class NoTabletHero : UserControl
{
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<NoTabletHero, string?>(nameof(Description));

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public NoTabletHero() => InitializeComponent();
}
