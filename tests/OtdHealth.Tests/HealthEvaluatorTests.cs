using System.Text.Json;

namespace OtdHealth.Tests;

public class HealthEvaluatorTests
{
    private static HealthSnapshot Healthy(HealthPlatform platform = HealthPlatform.Windows) => new()
    {
        Platform = platform,
        DaemonConnected = true,
        WinInkInstalled = true,
        VMultiInstalled = true,
        Tablets = [new("Tablet A", true, true)],
    };

    [Theory]
    [InlineData(HealthPlatform.Windows)]
    [InlineData(HealthPlatform.MacOS)]
    [InlineData(HealthPlatform.Linux)]
    public void HealthySnapshotsHaveNoFindings(HealthPlatform platform) =>
        Assert.Empty(HealthEvaluator.Evaluate(Healthy(platform)));

    [Fact]
    public void UncollectedInstallationFactsAreNotReportedAsMissing()
    {
        Assert.Empty(HealthEvaluator.Evaluate(new HealthSnapshot { Platform = HealthPlatform.Windows }));
    }

    public static IEnumerable<object[]> IndividualFindings()
    {
        yield return [Healthy() with { WinInkInstalled = false }, "winink.notInstalled", HealthSeverity.Broken];
        yield return [Healthy() with { WinInkVersionMismatch = true }, "winink.versionMismatch", HealthSeverity.Misconfigured];
        yield return [Healthy() with { VMultiInstalled = false }, "vmulti.notInstalled", HealthSeverity.Broken];
        yield return [Healthy() with { HasDriverConflict = true }, "driver.conflict", HealthSeverity.Misconfigured];
        yield return [Healthy() with { HasDriverConflict = true, BlockingDriverConflict = true }, "driver.conflict", HealthSeverity.Broken];
        yield return [Healthy() with { RunningElevated = true }, "process.elevated", HealthSeverity.Misconfigured];
        yield return [Healthy(HealthPlatform.MacOS) with { DaemonCannotOpenTablet = true }, "otd.permissionsMissing", HealthSeverity.Broken];
        yield return [Healthy() with { ForeignDaemon = true }, "daemon.foreign", HealthSeverity.Information];
        yield return [Healthy() with { DaemonSourceUnknown = true }, "daemon.sourceUnknown", HealthSeverity.Recommendation];
        yield return [Healthy() with { DaemonVersion = "0.6.6", ExpectedOtdVersion = "0.6.7" }, "daemon.versionMismatch", HealthSeverity.Recommendation];

        var linux = new HealthSnapshot { Platform = HealthPlatform.Linux };
        yield return [linux with { LinuxUdevRulesMissing = true }, "linux.udevRules", HealthSeverity.Broken];
        yield return [linux with { LinuxHidAccess = HidAccessStatus.Blocked }, "linux.hidAccessBlocked", HealthSeverity.Broken];
        yield return [linux with { LinuxHidAccess = HidAccessStatus.PendingRestart }, "linux.hidAccessPending", HealthSeverity.Information];
        yield return [linux with { LinuxConflictingModulesLoaded = ["wacom"] }, "linux.conflictingModules", HealthSeverity.Misconfigured];
    }

    [Theory]
    [MemberData(nameof(IndividualFindings))]
    public void ReportsStableCodesAndSeverity(HealthSnapshot input, string code, HealthSeverity severity)
    {
        var finding = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal(code, finding.Code);
        Assert.Equal(code, finding.Id);
        Assert.Equal(severity, finding.Severity);
        Assert.Null(finding.TabletName);
    }

    public static IEnumerable<object[]> TabletFindings()
    {
        var tablet = new TabletHealthSnapshot("Tablet A", true, true);
        yield return [tablet with { OutputModeIsWinInk = false }, "tablet.notWinInk", HealthSeverity.Misconfigured];
        yield return [tablet with { OutputModeIsWinInk = false, WinInkOptedOut = true }, "tablet.winInkOff", HealthSeverity.Recommendation];
        yield return [tablet with { PenTipDisabled = true }, "tablet.penTipDisabled", HealthSeverity.Recommendation];
        yield return [tablet with { PressureDisabled = true }, "tablet.pressureDisabled", HealthSeverity.Recommendation];
        yield return [tablet with { TiltDisabled = true }, "tablet.tiltDisabled", HealthSeverity.Recommendation];
        yield return [tablet with { DynamicsWarningRequired = true }, "tablet.dynamicsOff", HealthSeverity.Recommendation];
        yield return [tablet with { ConfigIsOverride = true }, "tablet.configOverride", HealthSeverity.Recommendation];
        yield return [tablet with { Mapping = DisplayMappingStatus.Custom }, "tablet.mappingCustom", HealthSeverity.Recommendation];
        yield return [tablet with { Mapping = DisplayMappingStatus.OffScreen }, "tablet.mappingOffScreen", HealthSeverity.Misconfigured];
        yield return [tablet with { NonCardinalRotation = true }, "tablet.mappingRotation", HealthSeverity.Misconfigured];
    }

    [Theory]
    [MemberData(nameof(TabletFindings))]
    public void TabletFactsHaveSeparateCodesAndSubjects(TabletHealthSnapshot tablet, string code, HealthSeverity severity)
    {
        var finding = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { Tablets = [tablet] }));
        Assert.Equal(code, finding.Code);
        Assert.Equal("Tablet A", finding.TabletName);
        Assert.Equal(code + ":Tablet A", finding.Id);
        Assert.Equal(severity, finding.Severity);
    }

    public static IEnumerable<object[]> UndetectedTabletFacts() =>
        TabletFindings().Select(row => new object[] { row[0] });

    [Theory]
    [MemberData(nameof(UndetectedTabletFacts))]
    public void UndetectedTabletsHaveNoFindings(TabletHealthSnapshot tablet) =>
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with { Tablets = [tablet with { Detected = false }] }));

    [Theory]
    [InlineData(HealthPlatform.MacOS)]
    [InlineData(HealthPlatform.Linux)]
    [InlineData(HealthPlatform.Unspecified)]
    public void WindowsStackIsNotAssessedOnOtherPlatforms(HealthPlatform platform)
    {
        var input = Healthy(platform) with
        {
            WinInkInstalled = false,
            WinInkVersionMismatch = true,
            VMultiInstalled = false,
            HasDriverConflict = true,
            BlockingDriverConflict = true,
            Tablets = [new("Tablet A", true, false, WinInkOptedOut: true)],
        };
        Assert.Empty(HealthEvaluator.Evaluate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void PluginMustBeInstalledBeforeVersionOrOutputModeIsAssessed(bool? installed)
    {
        var findings = HealthEvaluator.Evaluate(Healthy() with
        {
            WinInkInstalled = installed,
            WinInkVersionMismatch = true,
            Tablets = [new("Tablet A", true, false)],
        });
        Assert.DoesNotContain(findings, f => f.Code == "winink.versionMismatch" || f.Code == "tablet.notWinInk");
        Assert.Equal(installed == false ? 1 : 0, findings.Count);
    }

    [Fact]
    public void BothMissingWindowsPrerequisitesAreReported()
    {
        var findings = HealthEvaluator.Evaluate(Healthy() with { WinInkInstalled = false, VMultiInstalled = false });
        Assert.Equal(["vmulti.notInstalled", "winink.notInstalled"], findings.Select(f => f.Code).ToArray());
    }

    [Fact]
    public void PenSettingsAreIndependentEvenWhenThePluginIsMissing()
    {
        var findings = HealthEvaluator.Evaluate(Healthy() with
        {
            WinInkInstalled = false,
            Tablets = [new("Tablet A", true, false, WinInkOptedOut: true,
                PenTipDisabled: true, PressureDisabled: true, TiltDisabled: true)],
        });
        Assert.Equal(["winink.notInstalled", "tablet.penTipDisabled", "tablet.pressureDisabled",
            "tablet.tiltDisabled", "tablet.winInkOff"], findings.Select(f => f.Code).ToArray());
    }

    [Fact]
    public void DisconnectionSuppressesStaleDaemonObservations()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with
        {
            DaemonConnected = false,
            ForeignDaemon = true,
            DaemonSourceUnknown = true,
            DaemonCannotOpenTablet = true,
            DaemonVersion = "0.6.6",
            ExpectedOtdVersion = "0.6.7",
        }));
    }

    [Theory]
    [InlineData("", "0.6.7")]
    [InlineData("0.6.7", "")]
    [InlineData("0.6.7", "0.6.7.0")]
    [InlineData("0.6.7+abc", "0.6.7.2")]
    public void UnknownOrEquivalentVersionsDoNotRaiseFindings(string actual, string expected) =>
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with { DaemonVersion = actual, ExpectedOtdVersion = expected }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DaemonFindingsCarryBothVersionsAndOwnershipEvidence(bool managed)
    {
        var findings = HealthEvaluator.Evaluate(Healthy() with
        {
            ForeignDaemon = true,
            DaemonIsManagedButNotSelected = managed,
            DaemonSourceUnknown = true,
            DaemonVersion = "0.6.6",
            ExpectedOtdVersion = "0.6.7",
        });
        Assert.Equal(3, findings.Count);
        var version = Assert.Single(findings, f => f.Code == "daemon.versionMismatch").Evidence!;
        Assert.Equal("0.6.6", version.ActualVersion);
        Assert.Equal("0.6.7", version.ExpectedVersion);
        Assert.Equal(managed, Assert.Single(findings, f => f.Code == "daemon.foreign").Evidence!.ManagedButNotSelected);
    }

    [Theory]
    [InlineData(HealthPlatform.Windows, false)]
    [InlineData(HealthPlatform.MacOS, false)]
    [InlineData(HealthPlatform.Unspecified, false)]
    [InlineData(HealthPlatform.Linux, true)]
    public void LinuxProxiesDoNotOverrideAWorkingTabletOrOtherPlatform(HealthPlatform platform, bool detected)
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy(platform) with
        {
            LinuxUdevRulesMissing = true,
            LinuxHidAccess = HidAccessStatus.Blocked,
            LinuxConflictingModulesLoaded = ["wacom", "hid_uclogic"],
            Tablets = [new("Tablet A", detected, true)],
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingPermissionEvidenceDistinguishesRestartAdvice(bool manager)
    {
        var finding = Assert.Single(HealthEvaluator.Evaluate(new HealthSnapshot
        {
            Platform = HealthPlatform.Linux,
            LinuxHidAccess = HidAccessStatus.PendingRestart,
            LinuxUserManagerRunning = manager,
        }));
        Assert.Equal(HealthSeverity.Information, finding.Severity);
        Assert.Equal(manager, finding.Evidence!.UserManagerRunning);
    }

    [Fact]
    public void EvidenceIsDetachedFromTheCollectorsMutableModuleList()
    {
        var modules = new List<string> { "wacom", "hid_uclogic" };
        var finding = Assert.Single(HealthEvaluator.Evaluate(new HealthSnapshot
        {
            Platform = HealthPlatform.Linux,
            LinuxConflictingModulesLoaded = modules,
            LinuxConflictingModulesNotBlacklisted = true,
        }));
        modules.Clear();
        Assert.Equal(["wacom", "hid_uclogic"], finding.Evidence!.Modules);
        Assert.True(finding.Evidence.ModulesNotBlacklisted);
    }

    [Fact]
    public void OrderAndEvaluationAreStableWithoutChangingInput()
    {
        var tablets = new List<TabletHealthSnapshot>
        {
            new("Z", true, true, TiltDisabled: true), new("A", true, false),
        };
        var input = Healthy() with { Tablets = tablets, VMultiInstalled = false, ForeignDaemon = true };
        var first = HealthEvaluator.Evaluate(input);
        Assert.Equal(["vmulti.notInstalled", "tablet.notWinInk:A", "tablet.tiltDisabled:Z", "daemon.foreign"],
            first.Select(f => f.Id).ToArray());
        Assert.Equal(first, HealthEvaluator.Evaluate(input));
        Assert.Equal(["Z", "A"], tablets.Select(t => t.Name).ToArray());
        Assert.Equal(first, HealthEvaluator.Evaluate(input with { Tablets = tablets.AsEnumerable().Reverse().ToArray() }));
    }

    [Fact]
    public void JsonConsumerCanRoundTripSnapshotAndFindingsWithoutAppTypes()
    {
        var input = new HealthSnapshot
        {
            Platform = HealthPlatform.Linux,
            DaemonConnected = true,
            DaemonVersion = "0.6.6",
            ExpectedOtdVersion = "0.6.7",
            LinuxConflictingModulesLoaded = ["wacom"],
            LinuxConflictingModulesNotBlacklisted = true,
        };
        var snapshot = JsonSerializer.Deserialize<HealthSnapshot>(JsonSerializer.Serialize(input))!;
        var json = JsonSerializer.Serialize(HealthEvaluator.Evaluate(snapshot));
        var findings = JsonSerializer.Deserialize<HealthFinding[]>(json)!;
        Assert.Equal(2, findings.Length);
        Assert.Equal("0.6.6", Assert.Single(findings, f => f.Code == "daemon.versionMismatch").Evidence!.ActualVersion);
        Assert.Equal(["wacom"], Assert.Single(findings, f => f.Code == "linux.conflictingModules").Evidence!.Modules);
        Assert.Contains("Misconfigured", json);
        Assert.DoesNotContain("Remediation", json);
        Assert.DoesNotContain("OpenTabletArtist", json);
    }

    [Fact]
    public void RejectsNullSnapshot() => Assert.Throws<ArgumentNullException>(() => HealthEvaluator.Evaluate(null!));
}
