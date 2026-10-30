using OpenTabletArtist.Domain.Health;
using OtdHealth;
using CoreSeverity = OtdHealth.HealthSeverity;
using UiSeverity = OpenTabletArtist.Domain.Health.HealthSeverity;

namespace OpenTabletArtist.Tests;

public class HealthIssuePresenterTests
{
    [Fact]
    public void EveryLibraryCodeHasAPresentation()
    {
        var evidence = new HealthEvidence
        {
            ActualVersion = "0.6.6",
            ExpectedVersion = "0.6.7",
            Modules = ["wacom"],
            ManagedButNotSelected = true,
            UserManagerRunning = true,
            ModulesNotBlacklisted = true,
        };
        foreach (var field in typeof(HealthCheckCodes).GetFields())
        {
            var code = (string)field.GetRawConstantValue()!;
            var card = Assert.Single(HealthIssuePresenter.Present([new(code, CoreSeverity.Recommendation, "Tablet A", evidence)]));
            Assert.False(string.IsNullOrWhiteSpace(card.Title));
            Assert.False(string.IsNullOrWhiteSpace(card.Detail));
            Assert.Equal(UiSeverity.Recommendation, card.Severity);
        }
    }

    [Fact]
    public void CombinedDaemonCardPreservesRowOrderAndWorstSeverity()
    {
        var card = Assert.Single(HealthIssuePresenter.Present([
            new(HealthCheckCodes.DaemonVersionMismatch, CoreSeverity.Recommendation, Evidence:
                new HealthEvidence { ActualVersion = "0.6.7", ExpectedVersion = "0.6.7.1" }),
            new(HealthCheckCodes.DaemonSourceUnknown, CoreSeverity.Recommendation),
            new(HealthCheckCodes.ForeignDaemon, CoreSeverity.Information, Evidence:
                new HealthEvidence { ManagedButNotSelected = false }),
        ]));
        Assert.Equal("otd.driver", card.Id);
        Assert.Equal(UiSeverity.Recommendation, card.Severity);
        Assert.Equal([
            "An OpenTabletDriver you installed, not the bundled copy",
            "Location couldn't be read",
            "This daemon is 0.6.7, older than the 0.6.7.1 this version of OpenTabletArtist ships with. It may be missing fixes.",
        ], card.Links!.Select(l => l.Setting).ToArray());
        Assert.All(card.Links!, l => Assert.Equal(RemediationArea.Daemon, l.Area));
    }

    // A daemon the app refuses is the reason nothing works, so it is not one row among the user-supplied
    // daemon's: it gets its own card, ahead of that one, and says what to do (#1042).
    [Fact]
    public void ARefusedVersionIsItsOwnCardAheadOfTheDaemonCard()
    {
        var cards = HealthIssuePresenter.Present([
            new(HealthCheckCodes.DaemonVersionMismatch, CoreSeverity.Broken, Evidence:
                new HealthEvidence { ActualVersion = "0.6.7", ExpectedVersion = "0.6.8" }),
            new(HealthCheckCodes.ForeignDaemon, CoreSeverity.Information, Evidence:
                new HealthEvidence { ManagedButNotSelected = false }),
        ]);

        Assert.Equal(["otd.versionBlocked", "otd.driver"], cards.Select(c => c.Id).ToArray());
        var blocked = cards[0];
        Assert.Equal(UiSeverity.Broken, blocked.Severity);
        Assert.Contains("0.6.7", blocked.Detail);
        Assert.Contains("0.6.8", blocked.Detail);
        Assert.Contains("Stop that daemon", blocked.Detail);
        Assert.Equal(RemediationArea.Daemon, blocked.Remediation!.Area);
        // The version is not repeated as a row in the other card.
        Assert.DoesNotContain(cards[1].Links!, l => l.Setting.Contains("0.6.7"));
    }

    [Fact]
    public void AnUnreadableVersionIsRefusedToo_AndSaysItCouldNotBeRead()
    {
        var card = Assert.Single(HealthIssuePresenter.Present([
            new(HealthCheckCodes.DaemonVersionMismatch, CoreSeverity.Broken, Evidence:
                new HealthEvidence { ActualVersion = "", ExpectedVersion = "0.6.8" }),
        ]));
        Assert.Equal("otd.versionBlocked", card.Id);
        Assert.Contains("couldn't read", card.Detail);
    }

    [Fact]
    public void PenRowsStayOnTheirOwnTabletAndInTheExistingOrder()
    {
        var cards = HealthIssuePresenter.Present([
            new(HealthCheckCodes.TabletTiltDisabled, CoreSeverity.Recommendation, "A"),
            new(HealthCheckCodes.TabletPressureDisabled, CoreSeverity.Recommendation, "B"),
            new(HealthCheckCodes.TabletPenTipDisabled, CoreSeverity.Recommendation, "A"),
            new(HealthCheckCodes.TabletWinInkOff, CoreSeverity.Recommendation, "A"),
        ]);
        Assert.Equal(2, cards.Count);
        var a = Assert.Single(cards, c => c.Id == "tablet.penBehavior:A");
        Assert.Equal(["Windows Ink is off", "Pen tip is disabled", "Tilt is disabled"], a.Links!.Select(l => l.Setting).ToArray());
        Assert.All(a.Links!, l => Assert.Equal("A", l.TabletName));
        Assert.Equal(RemediationArea.RestorePenBehavior, a.Remediation!.Area);
        Assert.Equal("A", a.Remediation.TabletName);
        var b = Assert.Single(cards, c => c.Id == "tablet.penBehavior:B");
        Assert.Equal("Pressure sensitivity is off", Assert.Single(b.Links!).Setting);
        Assert.Equal("B", b.Remediation!.TabletName);
    }

    [Fact]
    public void NewCodesCannotBeSilentlyDropped() => Assert.Throws<ArgumentOutOfRangeException>(() =>
        HealthIssuePresenter.Present([new("future.check", CoreSeverity.Broken)]));
}
