using Avalonia;
using Avalonia.Controls.Primitives;

namespace OpenTabletArtist.Controls;

/// <summary>The visual kind of a <see cref="Chip"/>: a muted accent pill, a solid accent pill, or a muted
/// warning pill.</summary>
public enum ChipKind
{
    /// <summary>Translucent accent fill + accent text (e.g. "In use now", "Installed").</summary>
    Accent,
    /// <summary>Solid accent fill + white text (e.g. "In use", "Detected").</summary>
    AccentSolid,
    /// <summary>Muted warning fill + warning outline/text (e.g. "Legacy").</summary>
    Warning,
}

/// <summary>
/// A small text-only status pill (tag). Consolidates the hand-rolled Border+TextBlock pills scattered
/// across the app (#621) into one control with a single canonical padding/typography; the fill/text colour
/// comes from <see cref="Kind"/>. A Chip is a filled rounded label. The default <c>ControlTheme</c> lives in
/// Themes/Styles.axaml.
/// </summary>
public class Chip : TemplatedControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<Chip, string>(nameof(Text), "");
    public static readonly StyledProperty<ChipKind> KindProperty =
        AvaloniaProperty.Register<Chip, ChipKind>(nameof(Kind), ChipKind.Accent);

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ChipKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
}
