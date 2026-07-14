using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
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
        ForeignDaemon = false,
        WinInkInstalled = true,
        WinInkVersionMismatch = false,
        VMultiInstalled = true,
        HasDriverConflict = false,
        BlockingDriverConflict = false,
        Tablets = new List<TabletHealthInput> { new("Tablet A", Detected: true, OutputModeIsWinInk: true) },
    };

    private static bool Has(IReadOnlyList<HealthIssue> issues, string id) => issues.Any(x => x.Id == id);

    [Fact]
    public void Healthy_ProducesNoIssues()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy()));
    }

    // Daemon reachability (not-connected / exe-missing) is no longer a health issue — it's owned by the
    // Home daemon problem card + Daemon page. Only the "external daemon" recommendation remains here.
    [Fact]
    public void ExternalDaemon_IsRecommendation()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { ForeignDaemon = true }));
        Assert.Equal("daemon.foreign", issue.Id);
        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
        Assert.Equal(RemediationArea.Daemon, issue.Remediation!.Area);
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
    public void DetectedTablet_NotWinInk_ButOptedOut_IsInformation_NotMisconfigured()
    {
        // "Don't use Windows Ink" is on (#549): a deliberate mouse-compatibility choice, so it's an FYI,
        // not a misconfiguration to fix. The blunt "not using Windows Ink" issue must not also fire.
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Wacom PTH-660", Detected: true, OutputModeIsWinInk: false, WinInkOptedOut: true),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.winInkOff:Wacom PTH-660", issue.Id);
        Assert.Equal(HealthSeverity.Information, issue.Severity);
        Assert.Equal(RemediationArea.TabletPenBehavior, issue.Remediation!.Area);
        Assert.False(Has(HealthEvaluator.Evaluate(input), "tablet.notWinInk:Wacom PTH-660"));
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
    public void VMultiNotInstalled_IsBroken_WithVMultiTarget()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { VMultiInstalled = false }));
        Assert.Equal("vmulti.notInstalled", issue.Id);
        Assert.Equal(HealthSeverity.Broken, issue.Severity);
        Assert.Equal(RemediationArea.VMulti, issue.Remediation!.Area);
    }

    [Fact]
    public void VMultiUnknown_RaisesNoIssue()
    {
        // Null = detection hasn't reported yet → no false "not installed" at startup.
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with { VMultiInstalled = null }));
    }

    [Fact]
    public void RunningElevated_IsMisconfigured_WithNoFix()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { RunningElevated = true }));
        Assert.Equal("app.elevated", issue.Id);
        Assert.Equal(HealthSeverity.Misconfigured, issue.Severity);
        Assert.Null(issue.Remediation); // informational — no in-app fix, so no Fix button
    }

    [Fact]
    public void MissingVMultiAndWinInk_BothSurfaceAtOnce()
    {
        var issues = HealthEvaluator.Evaluate(Healthy() with { VMultiInstalled = false, WinInkInstalled = false });
        Assert.True(Has(issues, "vmulti.notInstalled"));
        Assert.True(Has(issues, "winink.notInstalled"));
    }

    [Fact]
    public void DriverConflict_NonBlocking_IsMisconfigured_WithCleanupTarget()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { HasDriverConflict = true }));
        Assert.Equal("driver.conflict", issue.Id);
        Assert.Equal(HealthSeverity.Misconfigured, issue.Severity);
        Assert.Equal(RemediationArea.DriverCleanup, issue.Remediation!.Area);
    }

    [Fact]
    public void DriverConflict_Blocking_IsBroken()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(
            Healthy() with { HasDriverConflict = true, BlockingDriverConflict = true }));
        Assert.Equal("driver.conflict", issue.Id);
        Assert.Equal(HealthSeverity.Broken, issue.Severity);
    }

    [Fact]
    public void OffScreenMapping_IsMisconfigured_WithDisplayMappingTarget()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Wacom PTK-670", Detected: true, OutputModeIsWinInk: true,
                    Mapping: DisplayMappingValidity.OffScreen),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.mappingOffScreen:Wacom PTK-670", issue.Id);
        Assert.Equal(HealthSeverity.Misconfigured, issue.Severity);
        Assert.Equal(RemediationArea.TabletDisplayMapping, issue.Remediation!.Area);
        Assert.Equal("Wacom PTK-670", issue.Remediation.TabletName);
    }

    [Fact]
    public void CustomMapping_IsRecommendation_WithDisplayMappingTarget()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Tablet A", Detected: true, OutputModeIsWinInk: true,
                    Mapping: DisplayMappingValidity.Custom),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.mappingCustom:Tablet A", issue.Id);
        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
        Assert.Equal(RemediationArea.TabletDisplayMapping, issue.Remediation!.Area);
    }

    [Fact]
    public void ConfigOverride_IsRecommendation_WithConfigsTarget()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Wacom PTH-660", Detected: true, OutputModeIsWinInk: true, ConfigIsOverride: true),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.configOverride:Wacom PTH-660", issue.Id);
        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
        Assert.Equal(RemediationArea.Configs, issue.Remediation!.Area);
        Assert.Equal("Wacom PTH-660", issue.Remediation.TabletName);
    }

    [Fact]
    public void ConfigOverride_OnUndetectedTablet_IsIgnored()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Old tablet", Detected: false, OutputModeIsWinInk: true, ConfigIsOverride: true),
            },
        };
        Assert.Empty(HealthEvaluator.Evaluate(input));
    }

    [Fact]
    public void CleanOrNoMapping_RaisesNoMappingIssue()
    {
        foreach (var validity in new[] { DisplayMappingValidity.Clean, DisplayMappingValidity.None })
        {
            var input = Healthy() with
            {
                Tablets = new List<TabletHealthInput>
                {
                    new("Tablet A", Detected: true, OutputModeIsWinInk: true, Mapping: validity),
                },
            };
            Assert.Empty(HealthEvaluator.Evaluate(input));
        }
    }

    [Fact]
    public void InducedSeverities_EmitSyntheticIssues_WithClearRemediation()
    {
        var input = Healthy() with
        {
            InducedSeverities = new[] { HealthSeverity.Recommendation, HealthSeverity.Broken },
        };
        var issues = HealthEvaluator.Evaluate(input);

        var broken = Assert.Single(issues, x => x.Id == "dev.induced.Broken");
        Assert.Equal(HealthSeverity.Broken, broken.Severity);
        Assert.Equal(RemediationArea.DeveloperInducedWarning, broken.Remediation!.Area);
        Assert.True(Has(issues, "dev.induced.Recommendation"));
        // Sorted worst-first: the Broken synthetic issue leads.
        Assert.Equal("dev.induced.Broken", issues.First().Id);
    }

    [Fact]
    public void NoInducedSeverities_AddNothing()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy())); // Healthy() induces nothing
    }

    [Fact]
    public void DynamicsFilterOff_OnDetectedTablet_IsRecommendation_WithTabletTarget()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Wacom PTH-660", Detected: true, OutputModeIsWinInk: true, DynamicsFilterActive: false),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.dynamicsOff:Wacom PTH-660", issue.Id);
        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
        Assert.Equal(RemediationArea.TabletPenDynamics, issue.Remediation!.Area);
        Assert.Equal("Wacom PTH-660", issue.Remediation.TabletName);
    }

    [Fact]
    public void DynamicsFilterOff_OnUndetectedTablet_IsIgnored()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Old tablet", Detected: false, OutputModeIsWinInk: true, DynamicsFilterActive: false),
            },
        };
        Assert.Empty(HealthEvaluator.Evaluate(input));
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

    // --- Platform gating (#140): the Windows-only pen-delivery stack (Windows Ink + VMulti) and the
    //     Windows manufacturer-driver-conflict check are suppressed off-Windows, where they don't apply. ---

    [Fact]
    public void NonWindows_SuppressesWindowsOnlyChecks()
    {
        // A state that on Windows would raise WinInk-not-installed, VMulti-not-installed, driver-conflict,
        // and per-tablet not-WinInk — all meaningless on macOS/Linux (native output, no VMulti/Ink).
        var input = Healthy() with
        {
            IsWindows = false,
            WinInkInstalled = false,
            VMultiInstalled = false,
            HasDriverConflict = true,
            BlockingDriverConflict = true,
            Tablets = new List<TabletHealthInput> { new("Tablet A", Detected: true, OutputModeIsWinInk: false) },
        };

        var issues = HealthEvaluator.Evaluate(input);

        Assert.False(Has(issues, "winink.notInstalled"));
        Assert.False(Has(issues, "vmulti.notInstalled"));
        Assert.False(Has(issues, "driver.conflict"));
        Assert.False(Has(issues, "tablet.notWinInk:Tablet A"));
        Assert.Empty(issues);
    }

    [Fact]
    public void NonWindows_StillFlagsCrossPlatformIssues()
    {
        // Every check the plan calls out as cross-platform must still surface off-Windows: display mapping,
        // pen dynamics, config override, and the external-daemon recommendation.
        var input = Healthy() with
        {
            IsWindows = false,
            ForeignDaemon = true,   // → daemon.foreign (needs DaemonConnected, which Healthy() sets)
            Tablets = new List<TabletHealthInput>
            {
                new("Tablet A", Detected: true, OutputModeIsWinInk: false,
                    Mapping: DisplayMappingValidity.OffScreen, DynamicsFilterActive: false,
                    ConfigIsOverride: true),
            },
        };

        var issues = HealthEvaluator.Evaluate(input);

        Assert.True(Has(issues, "tablet.mappingOffScreen:Tablet A"));
        Assert.True(Has(issues, "tablet.dynamicsOff:Tablet A"));
        Assert.True(Has(issues, "tablet.configOverride:Tablet A"));
        Assert.True(Has(issues, "daemon.foreign"));
        Assert.False(Has(issues, "tablet.notWinInk:Tablet A")); // still a Windows-only concept
    }
}
