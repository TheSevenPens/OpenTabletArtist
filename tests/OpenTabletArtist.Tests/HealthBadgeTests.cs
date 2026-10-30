using System.Threading.Tasks;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What the badge on the Health tab says (#1050): the worst finding sets the look, every finding is counted,
/// and a daemon that is not connected counts as one broken thing.
/// </summary>
public class HealthBadgeTests
{
    private static readonly HealthSeverity[] None = [];

    [Fact]
    public void NoFindings_IsHealthy_WithNothingToCount()
    {
        var (tier, count) = HealthBadgeViewModel.Summarize(None, daemonDown: false);

        Assert.Equal(HealthBadgeTier.Healthy, tier);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(HealthSeverity.Information, HealthBadgeTier.Information)]
    [InlineData(HealthSeverity.Recommendation, HealthBadgeTier.Recommendation)]
    [InlineData(HealthSeverity.Misconfigured, HealthBadgeTier.Misconfigured)]
    [InlineData(HealthSeverity.Broken, HealthBadgeTier.Broken)]
    public void OneFinding_ShowsItsOwnTier(HealthSeverity severity, HealthBadgeTier expected)
    {
        var (tier, count) = HealthBadgeViewModel.Summarize([severity], daemonDown: false);

        Assert.Equal(expected, tier);
        Assert.Equal(1, count);
    }

    /// <summary>The worst finding sets the look, wherever it is in the list and however many quieter
    /// ones surround it. A pile of information must never hide a broken thing.</summary>
    [Fact]
    public void TheWorstFindingSetsTheLook_AndEveryFindingIsCounted()
    {
        var (tier, count) = HealthBadgeViewModel.Summarize(
            [HealthSeverity.Information, HealthSeverity.Information, HealthSeverity.Broken,
             HealthSeverity.Recommendation, HealthSeverity.Information], daemonDown: false);

        Assert.Equal(HealthBadgeTier.Broken, tier);
        Assert.Equal(5, count);
    }

    /// <summary>Information still shows, with its number, but as its own tier -- the one that looks calm.</summary>
    [Fact]
    public void InformationOnly_IsCounted_AndIsNotTheLookOfAProblem()
    {
        var (tier, count) = HealthBadgeViewModel.Summarize(
            [HealthSeverity.Information, HealthSeverity.Information], daemonDown: false);

        Assert.Equal(HealthBadgeTier.Information, tier);
        Assert.Equal(2, count);
        Assert.True(tier < HealthBadgeTier.Recommendation, "information must look quieter than any tier that asks for action");
    }

    [Fact]
    public void ADaemonThatIsDown_IsOneBrokenFinding_OnTopOfTheCatalog()
    {
        var (tier, count) = HealthBadgeViewModel.Summarize([HealthSeverity.Recommendation], daemonDown: true);

        Assert.Equal(HealthBadgeTier.Broken, tier);
        Assert.Equal(2, count);
    }

    /// <summary>Every tier says something different to a screen reader: a suggestion and a misconfiguration
    /// are not told apart by colour alone.</summary>
    [Fact]
    public void EveryTier_HasItsOwnSummary()
    {
        var tiers = new[]
        {
            HealthBadgeTier.Healthy, HealthBadgeTier.Information, HealthBadgeTier.Recommendation,
            HealthBadgeTier.Misconfigured, HealthBadgeTier.Broken,
        };

        Assert.Equal(tiers.Length, tiers.Select(t => HealthBadgeViewModel.SummaryFor(t, t == HealthBadgeTier.Healthy ? 0 : 2)).Distinct().Count());
    }

    /// <summary>The summary replaces the tab's own name for a screen reader, so it has to start with it.</summary>
    [Fact]
    public void TheSummary_StartsWithTheTabsOwnName()
    {
        Assert.StartsWith("Health", HealthBadgeViewModel.SummaryFor(HealthBadgeTier.Healthy, 0));
        Assert.StartsWith("Health", HealthBadgeViewModel.SummaryFor(HealthBadgeTier.Broken, 3));
    }

    [Fact]
    public void TheSummaryCountsInTheSingular()
    {
        Assert.Contains("1 finding,", HealthBadgeViewModel.SummaryFor(HealthBadgeTier.Broken, 1));
        Assert.Contains("3 findings,", HealthBadgeViewModel.SummaryFor(HealthBadgeTier.Broken, 3));
    }

    // --- The view model, following the catalog and the daemon ------------------------------------------

    [Fact]
    public void TheBadgeFollowsTheCatalog_AsFindingsComeAndGo()
    {
        var (badge, health, _) = Make();
        Assert.Equal(HealthBadgeTier.Healthy, badge.Tier);

        health.Issues.Add(Issue("a", HealthSeverity.Recommendation));
        Assert.Equal(HealthBadgeTier.Recommendation, badge.Tier);
        Assert.Equal(1, badge.Count);

        health.Issues.Add(Issue("b", HealthSeverity.Broken));
        Assert.Equal(HealthBadgeTier.Broken, badge.Tier);
        Assert.Equal(2, badge.Count);

        health.Issues.RemoveAt(1);
        Assert.Equal(HealthBadgeTier.Recommendation, badge.Tier);

        health.Issues.Clear();
        Assert.Equal(HealthBadgeTier.Healthy, badge.Tier);
        Assert.Equal(0, badge.Count);
    }

    [Fact]
    public void ADisconnectedDaemon_TurnsTheBadgeBroken_AndConnectingClearsIt()
    {
        var (badge, _, connection) = Make();

        connection.IsConnected = false;
        Assert.Equal(HealthBadgeTier.Broken, badge.Tier);
        Assert.Equal(1, badge.Count);

        connection.IsConnected = true;
        Assert.Equal(HealthBadgeTier.Healthy, badge.Tier);
    }

    /// <summary>At every launch the daemon is not connected yet. That is a connect in flight, not a
    /// problem, and the badge must not flash red for it.</summary>
    [Fact]
    public void WhileTheDaemonIsConnecting_TheBadgeIsNotRed()
    {
        var (badge, _, connection) = Make();
        connection.IsConnected = false;
        Assert.Equal(HealthBadgeTier.Broken, badge.Tier);

        connection.ShowDaemonActivity = true;

        Assert.Equal(HealthBadgeTier.Healthy, badge.Tier);
        Assert.Equal(0, badge.Count);
    }

    /// <summary>A refused daemon already has a card of its own in the catalog (#1042); counting the
    /// disconnection as well would report one problem twice.</summary>
    [Fact]
    public void ARefusedDaemon_IsNotCountedTwice()
    {
        var (badge, health, connection) = Make();
        connection.IsConnected = false;
        // Refusing re-evaluates the catalog, so the card the real evaluation would produce arrives after it.
        connection.IsDaemonRefused = true;
        health.Issues.Add(Issue("otd.versionBlocked", HealthSeverity.Broken));

        Assert.Equal(HealthBadgeTier.Broken, badge.Tier);
        Assert.Equal(1, badge.Count);
    }

    // --- Home's health card: one message per tier, no list -----------------------------------------------

    /// <summary>Only the two tiers that ask for action get the loud button. A suggestion or a note must not
    /// look like something to hurry.</summary>
    [Theory]
    [InlineData(HealthBadgeTier.Broken, true)]
    [InlineData(HealthBadgeTier.Misconfigured, true)]
    [InlineData(HealthBadgeTier.Recommendation, false)]
    [InlineData(HealthBadgeTier.Information, false)]
    [InlineData(HealthBadgeTier.Healthy, false)]
    public void OnlyTheTiersThatAskForAction_GetTheLoudButton(HealthBadgeTier tier, bool severe) =>
        Assert.Equal(severe, HealthBadgeViewModel.CardFor(tier, tier == HealthBadgeTier.Healthy ? 0 : 1).Severe);

    /// <summary>Every state says its own thing: the headline and the sentence differ, so the card can be read
    /// without the colour.</summary>
    [Fact]
    public void EveryTierHasItsOwnHeadlineAndSentence()
    {
        var cards = new[]
        {
            HealthBadgeViewModel.CardFor(HealthBadgeTier.Healthy, 0),
            HealthBadgeViewModel.CardFor(HealthBadgeTier.Information, 1),
            HealthBadgeViewModel.CardFor(HealthBadgeTier.Recommendation, 1),
            HealthBadgeViewModel.CardFor(HealthBadgeTier.Misconfigured, 1),
            HealthBadgeViewModel.CardFor(HealthBadgeTier.Broken, 1),
        };

        Assert.Equal(cards.Length, cards.Select(c => c.Headline).Distinct().Count());
        Assert.Equal(cards.Length, cards.Select(c => c.Line).Distinct().Count());
    }

    /// <summary>The card never says which finding it is about: that is the Health page's job, and listing
    /// them is what would make the card grow.</summary>
    [Fact]
    public void TheCard_NamesNoSpecificFinding_ButCountsThem()
    {
        var one = HealthBadgeViewModel.CardFor(HealthBadgeTier.Misconfigured, 1);
        var many = HealthBadgeViewModel.CardFor(HealthBadgeTier.Misconfigured, 4);

        Assert.Equal(one.Headline, many.Headline);
        Assert.Equal(one.Line, many.Line);
        Assert.EndsWith("1 FINDING", one.Eyebrow);
        Assert.EndsWith("4 FINDINGS", many.Eyebrow);
        Assert.StartsWith("MISCONFIGURED", many.Eyebrow);
    }

    [Fact]
    public void WithNothingToReport_TheCardSaysEverythingIsWorking_WithNoCount()
    {
        var card = HealthBadgeViewModel.CardFor(HealthBadgeTier.Healthy, 0);

        Assert.Equal("ALL CLEAR", card.Eyebrow);
        Assert.Equal("Everything's working fine", card.Headline);
    }

    [Fact]
    public void TheCardTintFollowsTheTierTheSameWayTheHealthPageDoes()
    {
        Assert.Equal(AlertSeverity.Error, HealthBadgeViewModel.CardFor(HealthBadgeTier.Broken, 1).Severity);
        Assert.Equal(AlertSeverity.Warning, HealthBadgeViewModel.CardFor(HealthBadgeTier.Misconfigured, 1).Severity);
        Assert.Equal(AlertSeverity.Neutral, HealthBadgeViewModel.CardFor(HealthBadgeTier.Recommendation, 1).Severity);
        Assert.Equal(AlertSeverity.Information, HealthBadgeViewModel.CardFor(HealthBadgeTier.Information, 1).Severity);
    }

    /// <summary>
    /// Before the health check has answered there is no finding and also no all-clear: "everything's working
    /// fine" is a claim that a check came back empty (#1060).
    /// </summary>
    [Fact]
    public void BeforeTheHealthCheckHasAnswered_NothingIsClaimedAboutHealth()
    {
        var (badge, _, _) = Make(reported: false);

        Assert.True(badge.IsChecking);
        Assert.Equal("Checking your setup", badge.CardHeadline);
        Assert.Equal("CHECKING", badge.CardEyebrow);
        Assert.Equal("Health: checking.", badge.Summary);
        Assert.Equal(HealthBadgeTier.Healthy, badge.Tier);   // no badge is drawn for it
    }

    [Fact]
    public void OnceTheHealthCheckHasAnswered_ANothingFoundIsAnAllClear()
    {
        var (badge, _, _) = Make(reported: true);

        Assert.False(badge.IsChecking);
        Assert.Equal("Everything's working fine", badge.CardHeadline);
        Assert.Equal("Health: all clear.", badge.Summary);
    }

    /// <summary>A finding is a finding whether or not the check has finished: it is never "checking".</summary>
    [Fact]
    public void AFindingIsNotChecking_EvenBeforeTheHealthCheckHasAnswered()
    {
        var (badge, health, _) = Make(reported: false);

        health.Issues.Add(Issue("a", HealthSeverity.Misconfigured));

        Assert.False(badge.IsChecking);
        Assert.Equal("Action recommended", badge.CardHeadline);
    }

    [Fact]
    public void TheCardFollowsTheCatalog_AsFindingsComeAndGo()
    {
        var (badge, health, _) = Make();
        Assert.False(badge.HasFindings);
        Assert.Equal("Everything's working fine", badge.CardHeadline);

        health.Issues.Add(Issue("a", HealthSeverity.Broken));
        Assert.True(badge.HasFindings);
        Assert.Equal("Action needed", badge.CardHeadline);
        Assert.True(badge.CardIsSevere);

        health.Issues.Clear();
        Assert.Equal("Everything's working fine", badge.CardHeadline);
        Assert.False(badge.CardIsSevere);
    }

    private static (HealthBadgeViewModel Badge, HealthService Health, FakeConnectionState Connection) Make(
        bool reported = true)
    {
        var connection = new FakeConnectionState { IsConnected = true };
        // A collection that has answered with nothing to report, or (when asked) one that never finishes, so
        // nothing re-evaluates the catalog under the test and the findings are exactly the ones it adds.
        var health = new HealthService(connection, new FakeDeviceData(), new DriverConflictMonitor(),
            reported
                ? _ => Task.FromResult(new OtdHealth.Collector.HealthAnalysisReport(new OtdHealth.HealthSnapshot(), [], []))
                : _ => new TaskCompletionSource<OtdHealth.Collector.HealthAnalysisReport>().Task);
        health.Issues.Clear();
        return (new HealthBadgeViewModel(health, new DaemonStatusViewModel(connection)), health, connection);
    }

    private static HealthIssue Issue(string id, HealthSeverity severity) =>
        new(id, severity, id, "", Remediation: null);
}
