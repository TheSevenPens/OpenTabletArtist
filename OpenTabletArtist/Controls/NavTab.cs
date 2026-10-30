using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>How a health badge looks: the worst thing the app has to say, or nothing at all (#1050).</summary>
/// <remarks>
/// Least to most severe, with <see cref="Healthy"/> below everything. The four after it are the health
/// severities one for one, so the badge reads the same as the cards it leads to.
/// </remarks>
public enum HealthBadgeTier
{
    /// <summary>No findings. Nothing is drawn.</summary>
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
/// A top-bar wordmark that can carry a small badge beside its word (#1050): how many findings the Health
/// page has, tinted by the worst of them.
/// </summary>
/// <remarks>
/// <para>
/// The badge is part of the tab rather than a separate control next to it, so it is one target for the
/// pointer and the keyboard, and it sits wherever the word does. It is drawn only while there is something
/// to count: a healthy app shows a plain word.
/// </para>
/// <para>
/// Colour follows the cards the page leads to (Broken red, Misconfigured amber, Recommendation blue,
/// Information plain), and the icon differs per tier as well, so a suggestion and a misconfiguration are
/// not told apart by colour alone.
/// </para>
/// </remarks>
public class NavTab : RadioButton
{
    public static readonly StyledProperty<HealthBadgeTier> BadgeTierProperty =
        AvaloniaProperty.Register<NavTab, HealthBadgeTier>(nameof(BadgeTier), HealthBadgeTier.Healthy);

    public static readonly StyledProperty<int> BadgeCountProperty =
        AvaloniaProperty.Register<NavTab, int>(nameof(BadgeCount));

    public static readonly StyledProperty<bool> HasBadgeProperty =
        AvaloniaProperty.Register<NavTab, bool>(nameof(HasBadge));

    static NavTab() =>
        BadgeCountProperty.Changed.AddClassHandler<NavTab>((tab, e) => tab.HasBadge = e.GetNewValue<int>() > 0);

    /// <summary>The worst tier present; drives the badge's colour and icon.</summary>
    public HealthBadgeTier BadgeTier { get => GetValue(BadgeTierProperty); set => SetValue(BadgeTierProperty, value); }

    /// <summary>How many findings. Zero draws no badge.</summary>
    public int BadgeCount { get => GetValue(BadgeCountProperty); set => SetValue(BadgeCountProperty, value); }

    /// <summary>Whether there is a badge to draw. Derived from <see cref="BadgeCount"/>; the template binds
    /// it so no converter is needed for "zero hides the badge".</summary>
    public bool HasBadge { get => GetValue(HasBadgeProperty); private set => SetValue(HasBadgeProperty, value); }
}
