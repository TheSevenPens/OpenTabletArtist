using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A boolean setting: a checkbox with a caption and an optional muted description line beneath it.
/// Consolidates the CheckBox + description-subtext pattern used for toggles like "Falling petals".
/// The default <c>ControlTheme</c> lives in Themes/Styles.axaml. <see cref="IsChecked"/> binds two-way.
/// </summary>
public class ToggleSetting : TemplatedControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ToggleSetting, string>(nameof(Label), "");
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<ToggleSetting, string?>(nameof(Description));
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ToggleSetting, bool>(nameof(IsChecked), defaultBindingMode: BindingMode.TwoWay);
    /// <summary>When true, the description sits inline after the label (one line) instead of on a
    /// muted line beneath it. Use for short hints that fit on a single row.</summary>
    public static readonly StyledProperty<bool> InlineDescriptionProperty =
        AvaloniaProperty.Register<ToggleSetting, bool>(nameof(InlineDescription));

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public bool IsChecked { get => GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
    public bool InlineDescription { get => GetValue(InlineDescriptionProperty); set => SetValue(InlineDescriptionProperty, value); }
}
