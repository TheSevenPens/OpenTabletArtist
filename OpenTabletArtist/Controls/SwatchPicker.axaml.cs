using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A fixed-palette colour picker (#555): a compact trigger showing the current colour and its hex/RGB
/// (#563); clicking it opens the palette grid in a flyout, so the swatches don't take up page space
/// inline. <see cref="Color"/> binds two-way. If the bound colour isn't in the palette, no swatch is
/// highlighted but the readout still shows it.
/// </summary>
public partial class SwatchPicker : UserControl
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<SwatchPicker, Color>(nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    public Color Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    /// <summary>The palette shown as swatches (the shared curated set).</summary>
    public IReadOnlyList<Color> Palette => ColorPalette.All;

    public SwatchPicker() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Dismiss the flyout once the user taps a swatch (the colour itself is set by the ListBox's two-way
    // SelectedItem binding). Tapped only fires on user interaction, so opening the flyout — which
    // programmatically restores the current selection — doesn't close it immediately.
    private void OnSwatchTapped(object? sender, TappedEventArgs e)
        => this.FindControl<Button>("Trigger")?.Flyout?.Hide();
}
