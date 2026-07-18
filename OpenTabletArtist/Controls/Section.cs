using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The <b>grouping</b> card role (#574): a titled/untitled content box with the shared "glass" look, used
/// purely to group related settings — the border carries no meaning. Owns its own border via template-bound
/// Background/BorderBrush/BorderThickness/CornerRadius/Padding (defaults set in the ControlTheme), so it can
/// be restyled flush/borderless for the cardless redesign (#573) without touching the severity/selectable
/// roles. Content goes in the element body (it's a <see cref="ContentControl"/>); the default
/// <c>ControlTheme</c> lives in Themes/Styles.axaml. Was <c>SettingCard</c>.
/// </summary>
public class Section : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Section, string?>(nameof(Title));
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<Section, string?>(nameof(Description));

    public static readonly DirectProperty<Section, bool> HasDescriptionProperty =
        AvaloniaProperty.RegisterDirect<Section, bool>(nameof(HasDescription), o => o.HasDescription);

    private bool _hasDescription;
    /// <summary>True when <see cref="Description"/> is non-empty (drives the header spacing via a selector).</summary>
    public bool HasDescription
    {
        get => _hasDescription;
        private set => SetAndRaise(HasDescriptionProperty, ref _hasDescription, value);
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DescriptionProperty)
            HasDescription = !string.IsNullOrWhiteSpace(Description);
    }
}
