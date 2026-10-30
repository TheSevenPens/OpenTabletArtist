using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// What the badge on the Health tab says (#1050): the worst finding's tier, how many findings there are,
/// and a sentence for the tooltip and screen readers.
/// </summary>
/// <remarks>
/// <para>
/// The worst finding sets the look, so a broken thing is never hidden by a pile of quiet ones. A finding
/// that is only information still counts and still shows, but it looks calm: the badge says something is
/// worth reading without saying something is wrong.
/// </para>
/// <para>
/// A daemon that is not connected counts as one broken finding, because that is how Home has always
/// treated it (it is deliberately not a health issue, #317). It is left out while a connect is in flight,
/// which is the state at every launch and would flash the badge red, and while the daemon is refused,
/// because that already has a card of its own in the catalog (#1042).
/// </para>
/// </remarks>
public sealed partial class HealthBadgeViewModel : ObservableObject, IDisposable
{
    private readonly HealthService _health;
    private readonly DaemonStatusViewModel _daemon;

    public HealthBadgeViewModel(HealthService health, DaemonStatusViewModel daemon)
    {
        _health = health;
        _daemon = daemon;
        _health.Issues.CollectionChanged += OnIssuesChanged;
        _health.PropertyChanged += OnSourceChanged;
        _daemon.PropertyChanged += OnSourceChanged;
        Recompute();
    }

    [ObservableProperty] private HealthBadgeTier _tier;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFindings))]
    private int _count;

    [ObservableProperty] private string _summary = "";

    /// <summary>There is at least one finding. Home's health card is a tinted alert when there is, and a
    /// plain all-clear card when there is not.</summary>
    public bool HasFindings => Count > 0;

    // What Home's health card says (#1050). One card, one message per tier, no list: the card is the same
    // size in every state, and the Health page it leads to has the findings themselves.
    [ObservableProperty] private string _cardEyebrow = "";
    [ObservableProperty] private string _cardHeadline = "";
    [ObservableProperty] private string _cardLine = "";
    [ObservableProperty] private string _cardButton = "";

    /// <summary>The card asks for action (broken or misconfigured), so its button is the loud one.</summary>
    [ObservableProperty] private bool _cardIsSevere;

    /// <summary>Which alert tint the card wears; the same mapping the Health page's cards use.</summary>
    [ObservableProperty] private AlertSeverity _cardSeverity = AlertSeverity.Information;

    private void OnIssuesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Recompute();

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HealthService.HasIssues)
            or nameof(DaemonStatusViewModel.ShowDaemonProblem)
            or nameof(DaemonStatusViewModel.ShowDaemonActivity)
            or nameof(DaemonStatusViewModel.IsDaemonRefused)
            or nameof(DaemonStatusViewModel.IsConnected))
            Recompute();
    }

    private void Recompute()
    {
        var badge = Summarize(_health.Issues.Select(i => i.Severity), DaemonDown(_daemon));
        Tier = badge.Tier;
        Count = badge.Count;
        Summary = SummaryFor(badge.Tier, badge.Count);
        var card = CardFor(badge.Tier, badge.Count);
        CardEyebrow = card.Eyebrow;
        CardHeadline = card.Headline;
        CardLine = card.Line;
        CardButton = card.Button;
        CardIsSevere = card.Severe;
        CardSeverity = card.Severity;
    }

    /// <summary>The words on Home's health card for a tier. The product speaks, never "I" or "we": what is
    /// wrong, how strongly to act on it, and nothing about which finding it is.</summary>
    internal static (string Eyebrow, string Headline, string Line, string Button, bool Severe, AlertSeverity Severity)
        CardFor(HealthBadgeTier tier, int count)
    {
        var n = count == 1 ? "1 FINDING" : $"{count} FINDINGS";
        return tier switch
        {
            HealthBadgeTier.Broken => ($"BROKEN \u00B7 {n}", "Action needed",
                "Something isn't working. Fixing it is strongly recommended.", "Review and fix", true, AlertSeverity.Error),
            HealthBadgeTier.Misconfigured => ($"MISCONFIGURED \u00B7 {n}", "Action recommended",
                "Something isn't set up the way it should be. It's worth fixing soon.", "Review and fix", true, AlertSeverity.Warning),
            HealthBadgeTier.Recommendation => ($"SUGGESTION \u00B7 {n}", "There is a way to improve things",
                "Nothing is wrong. Take a look when you have a moment.", "Review", false, AlertSeverity.Neutral),
            HealthBadgeTier.Information => ($"INFORMATION \u00B7 {n}", "Worth a look",
                "Something's going on. Review it to make sure it's all right with you.", "Review", false, AlertSeverity.Information),
            _ => ("ALL CLEAR", "Everything's working fine", "Nothing needs attention.", "Open health page", false,
                AlertSeverity.Information),
        };
    }

    /// <summary>Whether the daemon being unreachable is a finding of its own right now.</summary>
    internal static bool DaemonDown(DaemonStatusViewModel daemon) =>
        daemon.ShowDaemonProblem && !daemon.ShowDaemonActivity && !daemon.IsDaemonRefused;

    /// <summary>The badge for a set of findings. The worst severity wins; the count is all of them.</summary>
    internal static (HealthBadgeTier Tier, int Count) Summarize(IEnumerable<HealthSeverity> severities, bool daemonDown)
    {
        var count = 0;
        HealthSeverity? worst = null;
        foreach (var s in severities)
        {
            count++;
            if (worst is null || s > worst) worst = s;
        }
        if (daemonDown)
        {
            count++;
            worst = HealthSeverity.Broken;
        }
        return (worst is { } w ? TierFor(w) : HealthBadgeTier.Healthy, count);
    }

    internal static HealthBadgeTier TierFor(HealthSeverity severity) => severity switch
    {
        HealthSeverity.Broken => HealthBadgeTier.Broken,
        HealthSeverity.Misconfigured => HealthBadgeTier.Misconfigured,
        HealthSeverity.Recommendation => HealthBadgeTier.Recommendation,
        _ => HealthBadgeTier.Information,
    };

    /// <summary>What the tab is announced and tooltipped as. It replaces the tab's own name for a screen
    /// reader, so it starts with it.</summary>
    internal static string SummaryFor(HealthBadgeTier tier, int count)
    {
        if (count == 0) return "Health: all clear.";
        var findings = count == 1 ? "1 finding" : $"{count} findings";
        var worst = tier switch
        {
            HealthBadgeTier.Broken => "something is broken",
            HealthBadgeTier.Misconfigured => "something is misconfigured",
            HealthBadgeTier.Recommendation => "the worst is a suggestion",
            _ => "none of them is a problem",
        };
        return $"Health: {findings}, {worst}.";
    }

    public void Dispose()
    {
        _health.Issues.CollectionChanged -= OnIssuesChanged;
        _health.PropertyChanged -= OnSourceChanged;
        _daemon.PropertyChanged -= OnSourceChanged;
    }
}
