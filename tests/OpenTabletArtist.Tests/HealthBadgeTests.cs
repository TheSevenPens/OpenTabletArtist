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

    private static (HealthBadgeViewModel Badge, HealthService Health, FakeConnectionState Connection) Make()
    {
        var connection = new FakeConnectionState { IsConnected = true };
        // A collection that never finishes, so nothing re-evaluates the catalog under the test and the
        // findings are exactly the ones it adds.
        var health = new HealthService(connection, new FakeDeviceData(), new DriverConflictMonitor(),
            _ => new TaskCompletionSource<OtdHealth.Collector.HealthAnalysisReport>().Task);
        health.Issues.Clear();
        return (new HealthBadgeViewModel(health, new DaemonStatusViewModel(connection)), health, connection);
    }

    private static HealthIssue Issue(string id, HealthSeverity severity) =>
        new(id, severity, id, "", Remediation: null);
}
