using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A form row: a caption in a fixed-width left column and an arbitrary control on the right.
/// Consolidates the "label + control" grid rows (e.g. the Theme page's dropdown / colour / image rows)
/// and standardizes the label-column width. Content goes in the element body (it's a
/// <see cref="ContentControl"/>); the default <c>ControlTheme</c> lives in Themes/Styles.axaml.
/// </summary>
public class LabeledField : ContentControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<LabeledField, string>(nameof(Label), "");
    public static readonly StyledProperty<double> LabelWidthProperty =
        AvaloniaProperty.Register<LabeledField, double>(nameof(LabelWidth), 120);

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double LabelWidth { get => GetValue(LabelWidthProperty); set => SetValue(LabelWidthProperty, value); }
}
