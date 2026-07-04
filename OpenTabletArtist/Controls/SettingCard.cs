using System;
using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A settings card: the shared translucent "glass" panel with an optional title + description header
/// above its content. Consolidates the GlassPanel → CardTitle → description-text → content scaffold
/// that opens nearly every settings view. Content goes in the element body (it's a
/// <see cref="ContentControl"/>); the default <c>ControlTheme</c> lives in Themes/Styles.axaml.
/// </summary>
public class SettingCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingCard, string?>(nameof(Title));
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingCard, string?>(nameof(Description));

    public static readonly DirectProperty<SettingCard, bool> HasDescriptionProperty =
        AvaloniaProperty.RegisterDirect<SettingCard, bool>(nameof(HasDescription), o => o.HasDescription);

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
