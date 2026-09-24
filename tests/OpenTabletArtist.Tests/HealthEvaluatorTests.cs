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
    // --- Version skew against an adopted install (docs/design/official-otd-release.md) ---
    // A user's own OpenTabletDriver is whatever release they installed; OTA links OTD's types and the RPC
    // is loosely typed, so the two versions being different is worth surfacing on Home.

    [Fact]
    public void DaemonFromADifferentRelease_IsRecommendation_AndNamesBothVersions()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with
        {
            DaemonVersion = "0.6.6.2",
            ExpectedOtdVersion = "0.6.7.0",
        }));

        Assert.Equal("otd.driver", issue.Id);
        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
        Assert.Equal(RemediationArea.Daemon, issue.Remediation!.Area);

        // Both numbers still have to be on screen — that pairing is the whole content of the row.
        var row = Assert.Single(issue.Links!);
        Assert.Contains("0.6.6.2", row.Setting);
        Assert.Contains("0.6.7.0", row.Setting);
    }

    // The daemon binary reports "0.6.7" where OTA's assembly version is "0.6.7.0" — same release, and
    // nagging about the fourth component would fire on every healthy install.
    [Fact]
    public void SameReleaseWithADifferentFourthComponent_IsNotAnIssue()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with
        {
            DaemonVersion = "0.6.7",
            ExpectedOtdVersion = "0.6.7.0",
        }));
    }

    [Theory]
    [InlineData("", "0.6.7.0")]   // daemon version unreadable (cross-session / elevated process)
    [InlineData("0.6.6.2", "")]   // our own version unknown
    public void UnknownVersionsRaiseNothing(string daemon, string expected)
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with
        {
            DaemonVersion = daemon,
            ExpectedOtdVersion = expected,
        }));
    }

    [Fact]
    public void VersionMismatchIsNotReportedWhileDisconnected()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with
        {
            DaemonConnected = false,
            DaemonVersion = "0.6.6.2",
            ExpectedOtdVersion = "0.6.7.0",
        }));
    }

    // --- The tablet is there, the driver can't read it (macOS Input Monitoring) ---

    [Fact]
    public void ADaemonThatCannotOpenTheTablet_IsBroken_AndPointsAtSettings()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with
        {
            Tablets = [],                       // nothing detected...
            DaemonCannotOpenTablet = true,      // ...but the daemon can see one
        }));

        Assert.Equal("otd.permissionsMissing", issue.Id);
        Assert.Equal(HealthSeverity.Broken, issue.Severity);
        Assert.Equal(RemediationArea.InputMonitoring, issue.Remediation!.Area);
    }

    // The whole point of the probe: "no tablets" alone is not a permissions problem, it is usually
    // nothing plugged in.
    [Fact]
    public void NoTabletsAlone_DoesNotClaimAPermissionsProblem()
    {
        var issues = HealthEvaluator.Evaluate(Healthy() with { Tablets = [] });

        Assert.False(Has(issues, "otd.permissionsMissing"));
    }

    [Fact]
    public void PermissionsAreNotReportedWhileDisconnected()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with
        {
            DaemonConnected = false,
            Tablets = [],
            DaemonCannotOpenTablet = true,
        }));
    }

    // The state that previously said nothing anywhere: connected, but OTA couldn't read which binary
    // answered — a daemon whose process path it can't see.
    [Fact]
    public void UnreadableDaemonSource_IsReported()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { DaemonSourceUnknown = true }));

        Assert.Equal("otd.driver", issue.Id);
        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
        Assert.Equal(RemediationArea.Daemon, issue.Remediation!.Area);
    }

    [Fact]
    public void UnreadableDaemonSourceIsNotReportedWhileDisconnected()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Healthy() with
        {
            DaemonConnected = false,
            DaemonSourceUnknown = true,
        }));
    }

    // Adoption is a supported mode, not a defect: driving the user's own OTD install states itself and
    // offers a Review, never a Fix that would undo their choice (docs/design/official-otd-release.md).
    [Fact]
    public void AdoptedInstallAlone_IsInformationAndOffersReviewNotFix()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { ForeignDaemon = true }));

        Assert.Equal("otd.driver", issue.Id);
        Assert.Equal(HealthSeverity.Information, issue.Severity);
        Assert.Equal("Review", issue.Remediation!.ActionLabel);
        Assert.Equal(RemediationArea.Daemon, issue.Remediation!.Area);
        Assert.Equal("An OpenTabletDriver you installed, not the bundled copy",
            Assert.Single(issue.Links!).Setting);
    }

    // A daemon that IS one of OTA's own, but not the one OTA would start, is also External -- and
    // telling that person "not the bundled copy" is a false sentence about the very thing they are
    // running (#882). The classification is unchanged; only what it says is. Since #daemon-bundled-only
    // this is a second copy of OTA's own daemon rather than a chosen location: a dev tree beside a
    // bundled release, or Debug beside Release.
    [Fact]
    public void OneOfOtasOwnCopiesThatIsNotTheOneItWouldStart_IsNotDescribedAsSomethingElse()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(
            Healthy() with { ForeignDaemon = true, DaemonIsManagedButNotSelected = true }));

        var row = Assert.Single(issue.Links!).Setting;

        Assert.DoesNotContain("not the bundled copy", row);
        Assert.Equal("Another copy of the OTD daemon this app ships", row);

        // Short enough to survive the row's clipping: the first attempt lost "is answering" on screen,
        // which is the half that explained it. Checked against the widest row already shipping.
        //
        // A wording guard, not proof: characters are not rendered width in a proportional font, and this
        // cannot know the row's real bounds. What it catches is the next person making the message
        // longer than one already known to fit.
        Assert.True(row.Length <= "An OpenTabletDriver you installed, not the bundled copy".Length,
            $"row is longer than the widest one already shipping, and will clip: '{row}'");

        // Still the same card, the same severity and the same remedy: nothing about privileges moved.
        Assert.Equal("otd.driver", issue.Id);
        Assert.Equal(HealthSeverity.Information, issue.Severity);
        Assert.Equal("Review", issue.Remediation!.ActionLabel);
    }

    // The point of the merge: three facts about one driver are one card with three rows, not three cards
    // each offering the same Review.
    [Fact]
    public void EverythingAtOnce_IsStillOneCard()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with
        {
            ForeignDaemon = true,
            DaemonSourceUnknown = true,
            DaemonVersion = "0.6.6.2",
            ExpectedOtdVersion = "0.6.7.0",
        }));

        Assert.Equal("otd.driver", issue.Id);
        Assert.Equal(3, issue.Links!.Count);
    }

    // Severity is the worst contributing row, so a quiet adopted install doesn't mute a real difference.
    [Fact]
    public void AVersionDifferenceLiftsTheCardAboveInformation()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with
        {
            ForeignDaemon = true,
            DaemonVersion = "0.6.6.2",
            ExpectedOtdVersion = "0.6.7.0",
        }));

        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
    }

    // Rows all lead to the same page, so they carry no destination — repeating "Daemon ›" three times
    // would be noise rather than orientation.
    [Fact]
    public void RowsReadAsPlainStatements()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { DaemonSourceUnknown = true }));

        var row = Assert.Single(issue.Links!);
        Assert.Equal("Location couldn't be read", row.Label);
        Assert.Equal(RemediationArea.Daemon, row.Area);
    }

    [Fact]
    public void SettingsPreserved_IsMisconfigured_NamesBackup_NoFix()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with
        {
            SettingsLoad = SettingsLoadStatus.Preserved,
            SettingsBackupName = "settings.json.corrupt-20260101-120000",
        }));
        Assert.Equal("settings.unreadable", issue.Id);
        Assert.Equal(HealthSeverity.Misconfigured, issue.Severity);
        Assert.Contains("settings.json.corrupt-20260101-120000", issue.Detail); // the copy names the real backup
        Assert.Null(issue.Remediation); // recovery is restoring the backup; nothing to click here
    }

    [Fact]
    public void SettingsNotPreserved_IsBroken_AndDoesNotClaimABackup()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with
        {
            SettingsLoad = SettingsLoadStatus.NotPreserved,
        }));
        Assert.Equal("settings.unreadable", issue.Id);
        Assert.Equal(HealthSeverity.Broken, issue.Severity); // more urgent: the file may be overwritten
        Assert.DoesNotContain("set aside", issue.Detail);    // must not claim a backup was made
        Assert.Null(issue.Remediation);
    }

    [Fact]
    public void SettingsOk_ProducesNoSettingsIssue()
    {
        // Healthy() leaves SettingsLoad at its Ok default.
        Assert.False(Has(HealthEvaluator.Evaluate(Healthy()), "settings.unreadable"));
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
        // Primary "Fix" switches the tablet to Windows Ink in place; "Review" opens the Pen Behavior tab.
        Assert.Equal("Fix", issue.Remediation!.ActionLabel);
        Assert.Equal(RemediationArea.RestorePenBehavior, issue.Remediation.Area);
        Assert.Equal("Wacom PTH-660", issue.Remediation.TabletName);
        Assert.Equal("Review", issue.Secondary!.ActionLabel);
        Assert.Equal(RemediationArea.TabletPenBehavior, issue.Secondary.Area);
        Assert.Equal("Wacom PTH-660", issue.Secondary.TabletName);
    }

    [Fact]
    public void DetectedTablet_WinInkOptedOut_FiresArtistBundle_AbsorbingTheFyiNote()
    {
        // "Don't use Windows Ink" is on (#549) → the artist-pen-behavior bundle fires (Windows Ink off is
        // enough on its own) and absorbs the standalone FYI note so the two don't double up.
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Wacom PTH-660", Detected: true, OutputModeIsWinInk: false, WinInkOptedOut: true),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.penBehavior:Wacom PTH-660", issue.Id);
        Assert.Equal(HealthSeverity.Recommendation, issue.Severity);
        Assert.Equal(RemediationArea.RestorePenBehavior, issue.Remediation!.Area);
        Assert.Equal("Wacom PTH-660", issue.Remediation.TabletName);
        var link = Assert.Single(issue.Links!);
        Assert.Equal(RemediationArea.TabletPenBehavior, link.Area);
        // The old standalone notes must not also fire.
        Assert.False(Has(HealthEvaluator.Evaluate(input), "tablet.winInkOff:Wacom PTH-660"));
        Assert.False(Has(HealthEvaluator.Evaluate(input), "tablet.notWinInk:Wacom PTH-660"));
    }

    [Fact]
    public void ArtistBundle_FiresOnTwoOffenders_WithWindowsInkOn()
    {
        // Windows Ink is on, but pressure + tilt are both disabled → two offenders, so the bundle fires
        // with a review link for each and no Windows-Ink link.
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Tablet A", Detected: true, OutputModeIsWinInk: true,
                    PressureDisabled: true, TiltDisabled: true),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.penBehavior:Tablet A", issue.Id);
        Assert.Equal(2, issue.Links!.Count);
        Assert.Contains(issue.Links!, l => l.Area == RemediationArea.TabletPenInputs);   // pressure
        Assert.Contains(issue.Links!, l => l.Area == RemediationArea.TabletPenTilt);      // tilt
        Assert.DoesNotContain(issue.Links!, l => l.Area == RemediationArea.TabletPenBehavior);
    }

    [Fact]
    public void ArtistBundle_FiresOnASingleOffender()
    {
        // Even one offender (tilt off, Windows Ink on) is enough to surface the card, with a single link.
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Tablet A", Detected: true, OutputModeIsWinInk: true, TiltDisabled: true),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.penBehavior:Tablet A", issue.Id);
        var link = Assert.Single(issue.Links!);
        Assert.Equal(RemediationArea.TabletPenTilt, link.Area);
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
    public void TrayHostUnavailable_IsInformation_WithNoFix()
    {
        var issue = Assert.Single(HealthEvaluator.Evaluate(Healthy() with { TrayHostUnavailable = true }));
        Assert.Equal("tray.gnomeNoSni", issue.Id);
        Assert.Equal(HealthSeverity.Information, issue.Severity);
        Assert.Null(issue.Remediation); // heads-up only — the GNOME extension is a manual install
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
    public void OffScreenMapping_FixMapsToPrimary_ReviewOpensMapping()
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
        // Primary "Fix" re-maps to the primary display directly.
        Assert.Equal("Fix", issue.Remediation!.ActionLabel);
        Assert.Equal(RemediationArea.TabletMapToPrimary, issue.Remediation.Area);
        Assert.Equal("Wacom PTK-670", issue.Remediation.TabletName);
        // Secondary "Review" just navigates to the Display Mapping tab.
        Assert.Equal("Review", issue.Secondary!.ActionLabel);
        Assert.Equal(RemediationArea.TabletDisplayMapping, issue.Secondary.Area);
        Assert.Equal("Wacom PTK-670", issue.Secondary.TabletName);
    }

    [Fact]
    public void CustomMapping_FixMapsToPrimary_ReviewOpensMapping()
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
        Assert.Equal("Fix", issue.Remediation!.ActionLabel);
        Assert.Equal(RemediationArea.TabletMapToPrimary, issue.Remediation.Area);
        Assert.Equal("Review", issue.Secondary!.ActionLabel);
        Assert.Equal(RemediationArea.TabletDisplayMapping, issue.Secondary.Area);
    }

    [Fact]
    public void NonCardinalRotation_FixResetsRotation_ReviewOpensMapping()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Wacom PTK-870", Detected: true, OutputModeIsWinInk: true,
                    NonCardinalRotation: true),
            },
        };
        var issue = Assert.Single(HealthEvaluator.Evaluate(input));
        Assert.Equal("tablet.mappingRotation:Wacom PTK-870", issue.Id);
        Assert.Equal(HealthSeverity.Misconfigured, issue.Severity);
        Assert.Equal("Fix", issue.Remediation!.ActionLabel);
        Assert.Equal(RemediationArea.TabletResetRotation, issue.Remediation.Area);
        Assert.Equal("Wacom PTK-870", issue.Remediation.TabletName);
        Assert.Equal("Review", issue.Secondary!.ActionLabel);
        Assert.Equal(RemediationArea.TabletDisplayMapping, issue.Secondary.Area);
        Assert.Equal("Wacom PTK-870", issue.Secondary.TabletName);
    }

    [Fact]
    public void NonCardinalRotation_AndOffScreen_BothFlagged()
    {
        var input = Healthy() with
        {
            Tablets = new List<TabletHealthInput>
            {
                new("Tablet A", Detected: true, OutputModeIsWinInk: true,
                    Mapping: DisplayMappingValidity.OffScreen, NonCardinalRotation: true),
            },
        };
        var ids = HealthEvaluator.Evaluate(input).Select(x => x.Id).ToList();
        Assert.Contains("tablet.mappingOffScreen:Tablet A", ids);
        Assert.Contains("tablet.mappingRotation:Tablet A", ids);
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
            ForeignDaemon = true,   // → otd.driver (needs DaemonConnected, which Healthy() sets)
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
        Assert.True(Has(issues, "otd.driver"));
        Assert.False(Has(issues, "tablet.notWinInk:Tablet A")); // still a Windows-only concept
    }

    // --- Linux tablet prerequisites (#779) ------------------------------------------------

    /// <summary>Linux, connected, and the daemon has found nothing — the state these checks explain.</summary>
    private static HealthInputs LinuxNoTablet() => new()
    {
        IsWindows = false,
        IsLinux = true,
        DaemonConnected = true,
        VMultiInstalled = true,
        Tablets = new List<TabletHealthInput>(),
    };

    [Fact]
    public void Linux_MissingUdevRules_IsBroken()
    {
        var issues = HealthEvaluator.Evaluate(LinuxNoTablet() with { LinuxUdevRulesMissing = true });

        var issue = Assert.Single(issues, x => x.Id == "linux.udevRules");
        Assert.Equal(HealthSeverity.Broken, issue.Severity);
    }

    /// <summary>
    /// The gate that keeps these from nagging. Every one of them is a proxy — rules can live somewhere the
    /// probe doesn't look, and a distro can grant HID access by another route — so a working tablet is
    /// proof the prerequisites were met, however they were met.
    /// </summary>
    [Fact]
    public void Linux_PrerequisitesAreSilent_WhenATabletIsDetected()
    {
        var issues = HealthEvaluator.Evaluate(LinuxNoTablet() with
        {
            LinuxUdevRulesMissing = true,
            LinuxHidAccess = LinuxHidAccess.Blocked,
            LinuxConflictingModulesLoaded = new List<string> { "wacom" },
            LinuxConflictingModulesNotBlacklisted = true,
            Tablets = new List<TabletHealthInput> { new("Tablet A", Detected: true, OutputModeIsWinInk: false) },
        });

        Assert.False(Has(issues, "linux.udevRules"));
        Assert.False(Has(issues, "linux.hidAccess"));
        Assert.False(Has(issues, "linux.conflictingModules"));
    }

    [Fact]
    public void Linux_ChecksDoNotRunOnOtherPlatforms()
    {
        var issues = HealthEvaluator.Evaluate(LinuxNoTablet() with
        {
            IsLinux = false,
            LinuxUdevRulesMissing = true,
            LinuxHidAccess = LinuxHidAccess.Blocked,
        });

        Assert.False(Has(issues, "linux.udevRules"));
        Assert.False(Has(issues, "linux.hidAccess"));
    }

    [Fact]
    public void Linux_BlockedHidAccess_IsBroken()
    {
        var issue = Assert.Single(
            HealthEvaluator.Evaluate(LinuxNoTablet() with { LinuxHidAccess = LinuxHidAccess.Blocked }),
            x => x.Id == "linux.hidAccess");

        Assert.Equal(HealthSeverity.Broken, issue.Severity);
        Assert.Contains("usermod", issue.Detail);
    }

    /// <summary>
    /// A granted-but-not-live group is not a refusal. Reporting it as one sends the user to re-run a
    /// command that already worked, which is how the old tool's users lost an afternoon.
    /// </summary>
    [Fact]
    public void Linux_PendingHidAccess_IsInformationalAndDoesNotAskForTheCommandAgain()
    {
        var issue = Assert.Single(
            HealthEvaluator.Evaluate(LinuxNoTablet() with { LinuxHidAccess = LinuxHidAccess.PendingReboot }),
            x => x.Id == "linux.hidAccess");

        Assert.Equal(HealthSeverity.Information, issue.Severity);
        Assert.DoesNotContain("usermod", issue.Detail);
    }

    [Fact]
    public void Linux_PendingHidAccess_SaysRebootWhenTheUserManagerWouldSurviveALogout()
    {
        var withManager = Assert.Single(
            HealthEvaluator.Evaluate(LinuxNoTablet() with
            {
                LinuxHidAccess = LinuxHidAccess.PendingReboot,
                LinuxUserManagerRunning = true,
            }), x => x.Id == "linux.hidAccess");

        var without = Assert.Single(
            HealthEvaluator.Evaluate(LinuxNoTablet() with
            {
                LinuxHidAccess = LinuxHidAccess.PendingReboot,
                LinuxUserManagerRunning = false,
            }), x => x.Id == "linux.hidAccess");

        Assert.Contains("systemd", withManager.Detail);
        Assert.DoesNotContain("Log out", withManager.Detail);
        Assert.Contains("Log out", without.Detail);
    }

    [Fact]
    public void Linux_LoadedConflictingModules_AreNamedInTheAdvice()
    {
        var issue = Assert.Single(
            HealthEvaluator.Evaluate(LinuxNoTablet() with
            {
                LinuxConflictingModulesLoaded = new List<string> { "wacom", "hid_uclogic" },
                LinuxConflictingModulesNotBlacklisted = true,
            }), x => x.Id == "linux.conflictingModules");

        Assert.Equal(HealthSeverity.Misconfigured, issue.Severity);
        Assert.Contains("rmmod wacom hid_uclogic", issue.Detail);
        Assert.Contains("blacklist", issue.Detail);
    }

    [Fact]
    public void Linux_AlreadyBlacklistedModules_DoNotAskForBlacklisting()
    {
        var issue = Assert.Single(
            HealthEvaluator.Evaluate(LinuxNoTablet() with
            {
                LinuxConflictingModulesLoaded = new List<string> { "wacom" },
                LinuxConflictingModulesNotBlacklisted = false,
            }), x => x.Id == "linux.conflictingModules");

        Assert.Contains("already blacklisted", issue.Detail);
    }

    /// <summary>Blacklisting only matters as a way to stop a conflict recurring, so with nothing loaded
    /// there is nothing to say — otherwise every Linux machine without a tablet plugged in gets a warning
    /// about kernel modules it has never had trouble with.</summary>
    [Fact]
    public void Linux_UnblacklistedModulesAlone_RaiseNothing()
    {
        var issues = HealthEvaluator.Evaluate(LinuxNoTablet() with
        {
            LinuxConflictingModulesNotBlacklisted = true,
        });

        Assert.False(Has(issues, "linux.conflictingModules"));
    }
}
