using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain.Health;
using Xunit;

namespace OpenTabletArtist.Tests;

public class HealthEvaluatorTests
{
    // A fully-healthy baseline: connected, app-owned daemon, WinInk installed + compatible, one
    // detected tablet using a Windows Ink mode.
    private static HealthInputs Healthy() => new()
    {
        DaemonConnected = true,
        DaemonConnecting = false,
        DaemonExeMissing = false,
        ForeignDaemon = false,
        WinInkInstalled = true,
        WinInkVersionMismatch = false,
        Tablets = new List<TabletHealthInput> { new("Tablet A", Detected: true, OutputModeIsWinInk: true) },
    };

    private static bool Has(IReadOnlyList<HealthIssue> issues, string id) => issues.Any(x => x.Id == id);

    [Fact]
    public void Healthy_ProducesNoIssues()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy()));
    }

    [Fact]
    public void DaemonExeMissing_IsBroken()
    {
        var issues = HealthEvaluator.Evaluate(Healthy() with { DaemonConnected = false, DaemonExeMissing = true });
        var issue = Assert.Single(issues, x => x.Id == "daemon.missing");
        Assert.Equal(HealthSeverity.Broken, issue.Severity);
        Assert.Equal(RemediationArea.Daemon, issue.Remediation!.Area);
    }

    [Fact]
    public void Disconnected_WhileConnecting_IsSuppressed()
    {
        var issues = HealthEvaluator.Evaluate(Healthy() with { DaemonConnected = false, DaemonConnecting = true });
        Assert.False(Has(issues, "daemon.disconnected"));
    }

    [Fact]
    public void Disconnected_NotConnecting_IsBroken()
    {
        var issues = HealthEvaluator.Evaluate(Healthy() with { DaemonConnected = false, DaemonConnecting = false });
        Assert.True(Has(issues, "daemon.disconnected"));
    }

    [Fact]
    public void WinInkNotInstalled_IsBroken_AndSuppressesPerTabletCheck()
    {
        // With one detected non-WinInk tablet: installing WinInk is the root fix, so we don't also
        // nag per-tablet until the plugin exists.
        var input = Healthy() with
        {
            WinInkInstalled = false,
            Tablets = new List<TabletHealthInput> { new("Tablet A", Detected: true, OutputModeIsWinInk: false) },
        };
        var issues = HealthEvaluator.Evaluate(input);
        Assert.True(Has(issues, "winink.notInstalled"));
        Assert.False(Has(issues, "tablet.notWinInk:Tablet A"));
    }

    [Fact]
    public void DetectedTablet_NotWinInk_IsMisconfigured_WithTabletTarget()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput> { new("Wacom PTH-660", Detected: true, OutputModeIsWinInk: false) },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.notWinInk:Wacom PTH-660", issue.Id);
        Assert.Equal(HealthSeverity.Misconfigured, issue.Severity);
        Assert.Equal(RemediationArea.TabletPenBehavior, issue.Remediation!.Area);
        Assert.Equal("Wacom PTH-660", issue.Remediation.TabletName);
    }

    [Fact]
    public void UndetectedTablet_NotWinInk_IsIgnored()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput> { new("Old tablet", Detected: false, OutputModeIsWinInk: false) },
        };
        Assert.Empty(HealthEvaluator.Evaluate(input));
    }

    [Fact]
    public void ForeignDaemon_IsRecommendation()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { ForeignDaemon = true }));
        Assert.Equal("daemon.foreign", issue.Id);
        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
    }

    [Fact]
    public void Issues_SortedBySeverity_WorstFirst()
    {
        var input = Healthy() with
        {
            WinInkInstalled = false,          // Broken
            ForeignDaemon = true,             // Recommendation
        };
        var issues = HealthEvaluator.Evaluate(input);
        Assert.True(issues.Count >= 2);
        Assert.Equal(HealthSeverity.Broken, issues.First().Severity);
        Assert.True(issues.First().Severity >= issues.Last().Severity);
    }
}
