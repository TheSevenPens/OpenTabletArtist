using Avalonia;
using Avalonia.Controls.Primitives;

namespace OtdWindowsHelper.Controls;

/// <summary>
/// The rounded icon container used in every card header: a fixed 48×48 square holding a centered
/// Segoe MDL2 glyph. <see cref="IsActive"/> switches it to the success (green) treatment.
///
/// Replaces the hand-rolled Border + glyph + inline active-state &lt;Style&gt; block that was
/// copy-pasted into each card (#24). Callers set <see cref="Glyph"/> and bind
/// <see cref="IsActive"/>; layout (Grid.Column, Margin, alignment) is set on the control as usual.
/// The default <c>ControlTheme</c> lives in Themes/Styles.axaml.
/// </summary>
public class IconBox : TemplatedControl
{
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<IconBox, string>(nameof(Glyph), "");

    /// <summary>When true, the box uses the success (green) background + glyph color.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<IconBox, bool>(nameof(IsActive));

    public static readonly StyledProperty<double> GlyphSizeProperty =
        AvaloniaProperty.Register<IconBox, double>(nameof(GlyphSize), 20d);

    public string Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public double GlyphSize { get => GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
}
