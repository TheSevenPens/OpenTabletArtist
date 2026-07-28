using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The unified colour picker (#622): one control replacing the fixed-palette <c>SwatchPicker</c> and the
/// freeform <see cref="HueTriangleWheel"/>. A compact trigger (swatch + hex/RGB readout) opens a flyout with
/// a Wheel tab (hue ring + saturation/value triangle, any colour), a Palette tab (the curated
/// <see cref="ColorPalette"/> quick-picks), and a shared hex field. All three edit the same two-way
/// <see cref="Color"/>, so they track each other. Alpha isn't editable — every consumer is opaque.
/// </summary>
public partial class ColorPicker : UserControl
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorPicker, Color>(nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    public Color Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    /// <summary>The curated palette shown under the Palette tab.</summary>
    public IReadOnlyList<Color> Palette => ColorPalette.All;

    public ColorPicker() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // The palette's SelectedItem binding is OneWay (highlight only), so apply the pick here and dismiss the
    // flyout. Tapped only fires on real interaction, so restoring the highlight when the flyout opens doesn't
    // close it.
    private void OnSwatchTapped(object? sender, TappedEventArgs e)
    {
        if (this.FindControl<ListBox>("Swatches")?.SelectedItem is Color c) Color = c;
        this.FindControl<Button>("Trigger")?.Flyout?.Hide();
    }
}
