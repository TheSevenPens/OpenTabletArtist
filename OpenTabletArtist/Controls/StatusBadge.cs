using Avalonia;
using Avalonia.Controls.Primitives;

namespace OpenTabletArtist.Controls;

/// <summary>The severity of a <see cref="StatusBadge"/>, driving its glyph + colour.</summary>
public enum BadgeKind
{
    Success,
    Warning,
    Error,
}

/// <summary>
/// A small inline status line: a PathIcon (check / alert / error, per <see cref="Kind"/>) plus text,
/// coloured by <see cref="Kind"/> using the shared status brushes. Consolidates the "recommended /
/// not recommended" rows used in the pen switch cards. The default <c>ControlTheme</c> — which maps each
/// Kind to its icon geometry — lives in Themes/Styles.axaml.
/// </summary>
public class StatusBadge : TemplatedControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Text), "");
    public static readonly StyledProperty<BadgeKind> KindProperty =
        AvaloniaProperty.Register<StatusBadge, BadgeKind>(nameof(Kind), BadgeKind.Success);

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public BadgeKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
}
