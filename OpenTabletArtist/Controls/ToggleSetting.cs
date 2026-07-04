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

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public bool IsChecked { get => GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
}
