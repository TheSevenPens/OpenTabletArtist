using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The header of a <em>tabbed page</em> (see <c>docs/design/ux-terminology.md</c> — the "complex
/// header"). It always shows a title on the left; a page may replace the plain title with custom
/// <see cref="TitleContent"/> (e.g. a tablet's brand/model), and may add trailing actions/status via
/// the element body (<see cref="ContentControl.Content"/>). The default <c>ControlTheme</c> lives in
/// Themes/Styles.axaml.
/// </summary>
public class ComplexHeader : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ComplexHeader, string?>(nameof(Title));

    public static readonly StyledProperty<object?> TitleContentProperty =
        AvaloniaProperty.Register<ComplexHeader, object?>(nameof(TitleContent));

    public static readonly DirectProperty<ComplexHeader, bool> HasTitleContentProperty =
        AvaloniaProperty.RegisterDirect<ComplexHeader, bool>(nameof(HasTitleContent), o => o.HasTitleContent);

    public static readonly DirectProperty<ComplexHeader, bool> HasTitleTextProperty =
        AvaloniaProperty.RegisterDirect<ComplexHeader, bool>(nameof(HasTitleText), o => o.HasTitleText);

    private bool _hasTitleContent;
    private bool _hasTitleText;

    /// <summary>The plain title text — shown when <see cref="TitleContent"/> isn't set.</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Optional custom title area (e.g. a two-line brand/model). Overrides <see cref="Title"/>.</summary>
    public object? TitleContent { get => GetValue(TitleContentProperty); set => SetValue(TitleContentProperty, value); }

    /// <summary>True when <see cref="TitleContent"/> is set (drives the title slot's visibility).</summary>
    public bool HasTitleContent { get => _hasTitleContent; private set => SetAndRaise(HasTitleContentProperty, ref _hasTitleContent, value); }

    /// <summary>True when the plain <see cref="Title"/> should show (no <see cref="TitleContent"/>, non-empty title).</summary>
    public bool HasTitleText { get => _hasTitleText; private set => SetAndRaise(HasTitleTextProperty, ref _hasTitleText, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty || change.Property == TitleContentProperty)
        {
            HasTitleContent = TitleContent != null;
            HasTitleText = TitleContent == null && !string.IsNullOrEmpty(Title);
        }
    }
}
