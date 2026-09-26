using OpenTabletArtist.Domain.Health;

namespace OpenTabletArtist.Tests;

public class HealthReviewRegressionTests
{
    [Theory]
    [InlineData("Tablet B")]
    [InlineData("Sample Tablet")]
    public void SameNamedProfilesKeepSeparatePenCards(string name)
    {
        var issues = HealthEvaluator.Evaluate(new HealthInputs
        {
            WinInkInstalled = true,
            Tablets =
            [
                new(name, true, true, PenTipDisabled: true),
                new(name, true, false, WinInkOptedOut: true, PressureDisabled: true, TiltDisabled: true),
            ],
        });
        Assert.Equal(2, issues.Count);
        var tip = Assert.Single(issues, issue => issue.Links!.Count == 1);
        Assert.Equal("Pen tip is disabled", Assert.Single(tip.Links!).Setting);
        var other = Assert.Single(issues, issue => issue.Links!.Count == 3);
        Assert.Equal(["Windows Ink is off", "Pressure sensitivity is off", "Tilt is disabled"],
            other.Links!.Select(link => link.Setting).ToArray());
        Assert.All(issues, issue =>
        {
            Assert.Equal($"tablet.penBehavior:{name}", issue.Id);
            Assert.Equal(RemediationArea.RestorePenBehavior, issue.Remediation!.Area);
            Assert.Equal(name, issue.Remediation.TabletName);
            Assert.All(issue.Links!, link => Assert.Equal(name, link.TabletName));
        });
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, false)]
    public void PermissionAdviceUsesTheExplicitHostPlatform(bool windows, bool macOS, bool linux, bool expected)
    {
        var issues = HealthEvaluator.Evaluate(new HealthInputs
        {
            IsWindows = windows,
            IsMacOS = macOS,
            IsLinux = linux,
            WinInkInstalled = true,
            DaemonConnected = true,
            DaemonCannotOpenTablet = true,
        });
        Assert.Equal(expected, issues.Any(issue => issue.Id == "otd.permissionsMissing"));
    }

    [Fact]
    public void SameNamedProfilesKeepSeparateUngroupedFindings()
    {
        var issues = HealthEvaluator.Evaluate(new HealthInputs
        {
            WinInkInstalled = true,
            Tablets =
            [
                new("Same", true, true, Mapping: OpenTabletArtist.Domain.DisplayMappingValidity.OffScreen),
                new("Same", true, true, Mapping: OpenTabletArtist.Domain.DisplayMappingValidity.OffScreen),
            ],
        });
        Assert.Equal(2, issues.Count);
        Assert.All(issues, issue => Assert.Equal("tablet.mappingOffScreen:Same", issue.Id));
    }
}
