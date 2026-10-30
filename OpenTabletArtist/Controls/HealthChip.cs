using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>How the health chip looks: the worst thing the app has to say, or nothing at all (#1050).</summary>
/// <remarks>
/// Least to most severe, with <see cref="Healthy"/> below everything. The four after it are the health
/// severities one for one, so the chip reads the same as the cards it leads to.
/// </remarks>
public enum HealthChipTier
{
    /// <summary>No findings.</summary>
    Healthy,
    /// <summary>Context only. Calm on purpose: nothing here is a problem.</summary>
    Information,
    /// <summary>Works, but is not the recommended setup.</summary>
    Recommendation,
    /// <summary>A feature will not behave as expected.</summary>
    Misconfigured,
    /// <summary>Something core does not work.</summary>
    Broken,
}

/// <summary>
/// The top-bar health indicator (#1050): one small chip, on every page, whose look is the worst finding's
/// tier and whose number is how many findings there are. A button, because it goes to the Health page.
/// </summary>
/// <remarks>
/// Colour follows the cards it leads to (Broken red, Misconfigured amber, Recommendation blue,
/// Information plain), and the icon differs per tier as well, so a suggestion and a misconfiguration are
/// not told apart by colour alone.
/// </remarks>
public class HealthChip : Button
{
    public static readonly StyledProperty<HealthChipTier> TierProperty =
        AvaloniaProperty.Register<HealthChip, HealthChipTier>(nameof(Tier), HealthChipTier.Healthy);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<HealthChip, string>(nameof(Label), "");

    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<HealthChip, int>(nameof(Count));

    public static readonly StyledProperty<bool> HasCountProperty =
        AvaloniaProperty.Register<HealthChip, bool>(nameof(HasCount));

    static HealthChip() =>
        CountProperty.Changed.AddClassHandler<HealthChip>((chip, e) => chip.HasCount = e.GetNewValue<int>() > 0);

    /// <summary>Whether there is a number to show. Derived from <see cref="Count"/>; the template binds it
    /// so no converter is needed for "zero hides the number".</summary>
    public bool HasCount { get => GetValue(HasCountProperty); private set => SetValue(HasCountProperty, value); }

    /// <summary>The worst tier present; drives colour and icon.</summary>
    public HealthChipTier Tier { get => GetValue(TierProperty); set => SetValue(TierProperty, value); }

    /// <summary>One lower-case word for the tier.</summary>
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>How many findings. Zero hides the number.</summary>
    public int Count { get => GetValue(CountProperty); set => SetValue(CountProperty, value); }

    protected override Type StyleKeyOverride => typeof(HealthChip);
}
