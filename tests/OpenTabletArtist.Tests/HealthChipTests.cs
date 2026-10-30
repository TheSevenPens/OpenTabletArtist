using System.Threading.Tasks;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What the top-bar health chip says (#1050): the worst finding sets the look, every finding is counted,
/// and a daemon that is not connected counts as one broken thing.
/// </summary>
public class HealthChipTests
{
    private static readonly HealthSeverity[] None = [];

    [Fact]
    public void NoFindings_IsHealthy_WithNothingToCount()
    {
        var (tier, count) = HealthChipViewModel.Summarize(None, daemonDown: false);

        Assert.Equal(HealthChipTier.Healthy, tier);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(HealthSeverity.Information, HealthChipTier.Information)]
    [InlineData(HealthSeverity.Recommendation, HealthChipTier.Recommendation)]
    [InlineData(HealthSeverity.Misconfigured, HealthChipTier.Misconfigured)]
    [InlineData(HealthSeverity.Broken, HealthChipTier.Broken)]
    public void OneFinding_ShowsItsOwnTier(HealthSeverity severity, HealthChipTier expected)
    {
        var (tier, count) = HealthChipViewModel.Summarize([severity], daemonDown: false);

        Assert.Equal(expected, tier);
        Assert.Equal(1, count);
    }

    /// <summary>The worst finding sets the look, wherever it is in the list and however many quieter
    /// ones surround it. A pile of information must never hide a broken thing.</summary>
    [Fact]
    public void TheWorstFindingSetsTheLook_AndEveryFindingIsCounted()
    {
        var (tier, count) = HealthChipViewModel.Summarize(
            [HealthSeverity.Information, HealthSeverity.Information, HealthSeverity.Broken,
             HealthSeverity.Recommendation, HealthSeverity.Information], daemonDown: false);

        Assert.Equal(HealthChipTier.Broken, tier);
        Assert.Equal(5, count);
    }

    /// <summary>Information still shows, with its number, but as its own tier -- the one that looks calm.</summary>
    [Fact]
    public void InformationOnly_IsCounted_AndIsNotTheLookOfAProblem()
    {
        var (tier, count) = HealthChipViewModel.Summarize(
            [HealthSeverity.Information, HealthSeverity.Information], daemonDown: false);

        Assert.Equal(HealthChipTier.Information, tier);
        Assert.Equal(2, count);
        Assert.True(tier < HealthChipTier.Recommendation, "information must look quieter than any tier that asks for action");
    }

    [Fact]
    public void ADaemonThatIsDown_IsOneBrokenFinding_OnTopOfTheCatalog()
    {
        var (tier, count) = HealthChipViewModel.Summarize([HealthSeverity.Recommendation], daemonDown: true);

        Assert.Equal(HealthChipTier.Broken, tier);
        Assert.Equal(2, count);
    }

    /// <summary>Four tiers, four words, four summaries: a suggestion and a misconfiguration are not told
    /// apart by colour alone, so the words must differ too.</summary>
    [Fact]
    public void EveryTierHasItsOwnWord_AndItsOwnSummary()
    {
        var tiers = new[]
        {
            HealthChipTier.Healthy, HealthChipTier.Information, HealthChipTier.Recommendation,
            HealthChipTier.Misconfigured, HealthChipTier.Broken,
        };

        Assert.Equal(tiers.Length, tiers.Select(HealthChipViewModel.LabelFor).Distinct().Count());
        Assert.Equal(tiers.Length, tiers.Select(t => HealthChipViewModel.SummaryFor(t, t == HealthChipTier.Healthy ? 0 : 2)).Distinct().Count());
    }

    [Fact]
    public void TheSummaryCountsInTheSingular()
    {
        Assert.Contains("1 finding,", HealthChipViewModel.SummaryFor(HealthChipTier.Broken, 1));
        Assert.Contains("3 findings,", HealthChipViewModel.SummaryFor(HealthChipTier.Broken, 3));
    }

    // --- The view model, following the catalog and the daemon ------------------------------------------

    [Fact]
    public void TheChipFollowsTheCatalog_AsFindingsComeAndGo()
    {
        var (chip, health, _) = Make();
        Assert.Equal(HealthChipTier.Healthy, chip.Tier);

        health.Issues.Add(Issue("a", HealthSeverity.Recommendation));
        Assert.Equal(HealthChipTier.Recommendation, chip.Tier);
        Assert.Equal(1, chip.Count);

        health.Issues.Add(Issue("b", HealthSeverity.Broken));
        Assert.Equal(HealthChipTier.Broken, chip.Tier);
        Assert.Equal(2, chip.Count);

        health.Issues.RemoveAt(1);
        Assert.Equal(HealthChipTier.Recommendation, chip.Tier);

        health.Issues.Clear();
        Assert.Equal(HealthChipTier.Healthy, chip.Tier);
        Assert.Equal("healthy", chip.Label);
    }

    [Fact]
    public void ADisconnectedDaemon_TurnsTheChipBroken_AndConnectingClearsIt()
    {
        var (chip, _, connection) = Make();

        connection.IsConnected = false;
        Assert.Equal(HealthChipTier.Broken, chip.Tier);
        Assert.Equal(1, chip.Count);

        connection.IsConnected = true;
        Assert.Equal(HealthChipTier.Healthy, chip.Tier);
    }

    /// <summary>At every launch the daemon is not connected yet. That is a connect in flight, not a
    /// problem, and the chip must not flash red for it.</summary>
    [Fact]
    public void WhileTheDaemonIsConnecting_TheChipIsNotRed()
    {
        var (chip, _, connection) = Make();
        connection.IsConnected = false;
        Assert.Equal(HealthChipTier.Broken, chip.Tier);

        connection.ShowDaemonActivity = true;

        Assert.Equal(HealthChipTier.Healthy, chip.Tier);
        Assert.Equal(0, chip.Count);
    }

    /// <summary>A refused daemon already has a card of its own in the catalog (#1042); counting the
    /// disconnection as well would report one problem twice.</summary>
    [Fact]
    public void ARefusedDaemon_IsNotCountedTwice()
    {
        var (chip, health, connection) = Make();
        connection.IsConnected = false;
        // Refusing re-evaluates the catalog, so the card the real evaluation would produce arrives after it.
        connection.IsDaemonRefused = true;
        health.Issues.Add(Issue("otd.versionBlocked", HealthSeverity.Broken));

        Assert.Equal(HealthChipTier.Broken, chip.Tier);
        Assert.Equal(1, chip.Count);
    }

    [Fact]
    public void Clicking_OpensTheHealthPage()
    {
        var (chip, _, _) = Make(out var opened);

        chip.OpenCommand.Execute(null);

        Assert.Equal(1, opened.Count);
    }

    private static (HealthChipViewModel Chip, HealthService Health, FakeConnectionState Connection) Make() =>
        Make(out _);

    private static (HealthChipViewModel Chip, HealthService Health, FakeConnectionState Connection) Make(
        out Opened opened)
    {
        var connection = new FakeConnectionState { IsConnected = true };
        // A collection that never finishes, so nothing re-evaluates the catalog under the test and the
        // findings are exactly the ones it adds.
        var health = new HealthService(connection, new FakeDeviceData(), new DriverConflictMonitor(),
            _ => new TaskCompletionSource<OtdHealth.Collector.HealthAnalysisReport>().Task);
        health.Issues.Clear();
        var counter = new Opened();
        opened = counter;
        return (new HealthChipViewModel(health, new DaemonStatusViewModel(connection), () => counter.Count++),
            health, connection);
    }

    private sealed class Opened { public int Count; }

    private static HealthIssue Issue(string id, HealthSeverity severity) =>
        new(id, severity, id, "", Remediation: null);
}
