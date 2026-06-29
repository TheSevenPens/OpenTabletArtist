using Avalonia;
using Avalonia.Controls.Primitives;

namespace OtdArtist.Controls;

/// <summary>
/// A compact status banner showing whether a tablet is detected/connected, with its status text and
/// (optionally) a "Dynamics on" chip. Shared by the Test view (#128/#129/#130) and the Tablet
/// Settings dialog (#132) so the look stays consistent.
///
/// Consumers compute <see cref="Text"/>; <see cref="IsDetected"/> drives the status mark (green check
/// vs. amber warning), and <see cref="DynamicsOn"/> reveals the dynamics chip. The default
/// <c>ControlTheme</c> lives in Themes/Styles.axaml.
/// </summary>
public class TabletStatusBanner : TemplatedControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TabletStatusBanner, string>(nameof(Text), "");

    /// <summary>True → green check (connected); false → amber warning (not connected).</summary>
    public static readonly StyledProperty<bool> IsDetectedProperty =
        AvaloniaProperty.Register<TabletStatusBanner, bool>(nameof(IsDetected));

    /// <summary>Shows the "Dynamics on" chip when true.</summary>
    public static readonly StyledProperty<bool> DynamicsOnProperty =
        AvaloniaProperty.Register<TabletStatusBanner, bool>(nameof(DynamicsOn));

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsDetected { get => GetValue(IsDetectedProperty); set => SetValue(IsDetectedProperty, value); }
    public bool DynamicsOn { get => GetValue(DynamicsOnProperty); set => SetValue(DynamicsOnProperty, value); }
}
