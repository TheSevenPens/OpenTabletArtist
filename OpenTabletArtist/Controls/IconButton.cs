using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// An icon-only ghost button: the <c>GhostButton</c> look at a fixed compact size (34×30 by default) with a
/// single centred <see cref="PathIcon"/> whose colour follows the button. Consolidates the boilerplate
/// (`Theme=GhostButton` + Width/Height/Padding + a foreground-bound PathIcon) repeated across the toolbar
/// action rows (#616). Callers set only <see cref="Icon"/>, plus the usual Button properties (Command,
/// ToolTip.Tip, AutomationProperties.Name, IsEnabled, Margin). Override Width/Height for compact variants,
/// or <see cref="IconSize"/> for a larger/smaller glyph. The default theme lives in Themes/Styles.axaml.
/// </summary>
public class IconButton : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<IconButton, Geometry?>(nameof(Icon));

    /// <summary>Size (px) of the centred glyph. Default 16.</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<IconButton, double>(nameof(IconSize), 16);

    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double IconSize { get => GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
}
