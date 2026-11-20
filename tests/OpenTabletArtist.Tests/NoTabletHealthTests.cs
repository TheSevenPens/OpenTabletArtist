using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;
using OtdHealth.Collector;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The "no tablet detected" notice (tablet.none). It is Information: nothing is wrong and the app works, but
/// the artist should know we cannot find a tablet. Two things carry it: the evaluator decides when it is
/// shown, and the service decides when "none" has held long enough to be believed.
/// </summary>
public class NoTabletHealthTests
{
    // --- The evaluator: when the card shows ---------------------------------------------------------------

    private static HealthInputs Connected() => new()
    {
        DaemonConnected = true,
        WinInkInstalled = true,
        VMultiInstalled = true,
    };

    private static HealthIssue? NoTabletIssue(HealthInputs i) =>
        HealthEvaluator.Evaluate(i).FirstOrDefault(x => x.Id == "tablet.none");

    [Fact]
    public void NoTablet_IsInformation_WithNoFix()
    {
        var issue = NoTabletIssue(Connected() with { NoTabletDetected = true });

        Assert.NotNull(issue);
        Assert.Equal(HealthSeverity.Information, issue!.Severity);
        Assert.Null(issue.Remediation);   // nothing to fix: plugging one in is picked up on its own
        Assert.Equal("No tablet detected", issue.Title);
    }

    /// <summary>With nothing reported, the catalog stays quiet: this notice is only ever the one thing said.</summary>
    [Fact]
    public void NoTablet_IsTheOnlyFinding_WhenTheAppIsOtherwiseHealthy()
    {
        var issues = HealthEvaluator.Evaluate(Connected() with { NoTabletDetected = true });

        Assert.Equal(["tablet.none"], issues.Select(x => x.Id));
    }

    [Fact]
    public void ATabletBeingPresent_MeansNoNotice()
    {
        Assert.Null(NoTabletIssue(Connected() with { NoTabletDetected = false }));
    }

    /// <summary>A daemon that is not connected has its own card, and "no tablet" would be a guess about a daemon
    /// nobody can ask.</summary>
    [Fact]
    public void NotConnected_SaysNothingAboutTablets()
    {
        Assert.Null(NoTabletIssue(Connected() with { DaemonConnected = false, NoTabletDetected = true }));
    }

    /// <summary>When the platform check already says why none is found, one cause is not announced twice.</summary>
    [Fact]
    public void WhenAPlatformFindingExplainsIt_TheNoticeStaysOut()
    {
        Assert.Null(NoTabletIssue(Connected() with { IsMacOS = true, IsWindows = false,
            DaemonCannotOpenTablet = true, NoTabletDetected = true }));
        Assert.Null(NoTabletIssue(Connected() with { IsLinux = true, IsWindows = false,
            LinuxUdevRulesMissing = true, NoTabletDetected = true }));
        Assert.Null(NoTabletIssue(Connected() with { IsLinux = true, IsWindows = false,
            LinuxHidAccess = LinuxHidAccess.Blocked, NoTabletDetected = true }));
    }

    // --- The service: when "none" is believed -------------------------------------------------------------

    private static (HealthService Service, FakeConnectionState Connection, FakeDeviceData Data) Service(TimeSpan settle)
    {
        var connection = new FakeConnectionState { IsConnected = true };
        var data = new FakeDeviceData();
        // A collection that never answers: these tests are about the tablet list, not the machine probes, and
        // a real collection would read this computer.
        var service = new HealthService(connection, data, new DriverConflictMonitor(),
            collectForTest: _ => new TaskCompletionSource<HealthAnalysisReport>().Task, noTabletSettle: settle);
        return (service, connection, data);
    }

    private static bool ShowsNoTablet(HealthService s) => s.Issues.Any(i => i.Id == "tablet.none");

    private static DetectedTablet Tablet() => new("Wacom PTK-470", "0 x 0 mm", "0", "0");

    [Fact]
    public void AfterTheDataLoads_WithNoTablet_TheNoticeShows()
    {
        var (service, _, data) = Service(TimeSpan.Zero);
        using var _ = service;

        data.RaiseDataLoaded();

        Assert.True(ShowsNoTablet(service));
    }

    /// <summary>Before the first load there is no answer to read: an empty list means "not asked yet", not
    /// "no tablet". Showing it then would put the card up on every launch.</summary>
    [Fact]
    public void BeforeTheDataLoads_ItIsNotShown()
    {
        var (service, _, _) = Service(TimeSpan.Zero);
        using var _ = service;

        service.Refresh();

        Assert.False(ShowsNoTablet(service));
    }

    /// <summary>The settle wait: a freshly started daemon says "none" before it has finished detecting, and
    /// the card must not appear for that moment.</summary>
    [Fact]
    public void WhileTheWaitRuns_ItIsNotShown()
    {
        var (service, _, data) = Service(TimeSpan.FromMinutes(5));
        using var _ = service;

        data.RaiseDataLoaded();

        Assert.False(ShowsNoTablet(service));
    }

    [Fact]
    public void ATabletAppearingWithinTheWait_MeansItNeverShows()
    {
        var (service, _, data) = Service(TimeSpan.Zero);
        using var _ = service;

        data.DetectedTablets = [Tablet()];
        data.RaiseDataLoaded();

        Assert.False(ShowsNoTablet(service));
    }

    [Fact]
    public void WhenTheTabletIsPluggedIn_TheNoticeGoes()
    {
        var (service, _, data) = Service(TimeSpan.Zero);
        using var _ = service;
        data.RaiseDataLoaded();
        Assert.True(ShowsNoTablet(service));

        data.DetectedTablets = [Tablet()];
        data.RaiseDataLoaded();

        Assert.False(ShowsNoTablet(service));
    }

    [Fact]
    public void WhenTheTabletIsUnplugged_TheNoticeComesBack()
    {
        var (service, _, data) = Service(TimeSpan.Zero);
        using var _ = service;
        data.DetectedTablets = [Tablet()];
        data.RaiseDataLoaded();
        Assert.False(ShowsNoTablet(service));

        data.DetectedTablets = [];
        data.RaiseDataLoaded();

        Assert.True(ShowsNoTablet(service));
    }

    /// <summary>When the daemon goes away the connection card takes over, and the answer it gave is stale.</summary>
    [Fact]
    public void WhenTheDaemonDisconnects_TheNoticeGoes()
    {
        var (service, connection, data) = Service(TimeSpan.Zero);
        using var _ = service;
        data.RaiseDataLoaded();
        Assert.True(ShowsNoTablet(service));

        connection.IsConnected = false;

        Assert.False(ShowsNoTablet(service));
    }
}
