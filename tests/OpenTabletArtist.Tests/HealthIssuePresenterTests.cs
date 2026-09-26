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
                new HealthEvidence { ActualVersion = "0.6.6", ExpectedVersion = "0.6.7" }),
            new(HealthCheckCodes.DaemonSourceUnknown, CoreSeverity.Recommendation),
            new(HealthCheckCodes.ForeignDaemon, CoreSeverity.Information, Evidence:
                new HealthEvidence { ManagedButNotSelected = false }),
        ]));
        Assert.Equal("otd.driver", card.Id);
        Assert.Equal(UiSeverity.Recommendation, card.Severity);
        Assert.Equal([
            "An OpenTabletDriver you installed, not the bundled copy",
            "Location couldn't be read",
            "Version 0.6.6, untested with this app — it was built against 0.6.7",
        ], card.Links!.Select(l => l.Setting).ToArray());
        Assert.All(card.Links!, l => Assert.Equal(RemediationArea.Daemon, l.Area));
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
