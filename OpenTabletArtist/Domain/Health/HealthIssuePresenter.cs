using OtdHealth;

namespace OpenTabletArtist.Domain.Health;

/// <summary>Maps diagnostic facts to OTA's copy, grouped cards, and explicit user actions.</summary>
internal static class HealthIssuePresenter
{
    internal static IReadOnlyList<HealthIssue> Present(IReadOnlyList<HealthFinding> findings)
    {
        var issues = new List<HealthIssue>();
        var daemon = findings.Where(f => f.Code is HealthCheckCodes.ForeignDaemon
            or HealthCheckCodes.DaemonSourceUnknown or HealthCheckCodes.DaemonVersionMismatch).ToList();
        if (daemon.Count > 0) issues.Add(DaemonCard(daemon));

        foreach (var tablet in findings.Where(f => IsPenBehavior(f.Code)).GroupBy(f => f.TabletName))
            issues.Add(PenBehaviorCard(tablet.Key!, tablet.ToList()));

        foreach (var f in findings)
        {
            if (daemon.Contains(f) || IsPenBehavior(f.Code)) continue;
            issues.Add(PresentSingle(f));
        }
        return issues;
    }

    private static HealthSeverity Severity(OtdHealth.HealthSeverity severity) => severity switch
    {
        OtdHealth.HealthSeverity.Information => HealthSeverity.Information,
        OtdHealth.HealthSeverity.Recommendation => HealthSeverity.Recommendation,
        OtdHealth.HealthSeverity.Misconfigured => HealthSeverity.Misconfigured,
        OtdHealth.HealthSeverity.Broken => HealthSeverity.Broken,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static HealthIssue PresentSingle(HealthFinding f) => f.Code switch
    {
        HealthCheckCodes.WinInkNotInstalled => new HealthIssue("winink.notInstalled", Severity(f.Severity),
                "Windows Ink plugin not installed",
                "Without the plugin, you will not get pressure or tilt",
                new Remediation("Fix", RemediationArea.WindowsInk)),
        HealthCheckCodes.WinInkVersionMismatch => new HealthIssue("winink.versionMismatch", Severity(f.Severity),
                "Windows Ink plugin may be incompatible",
                "Not the plugin version OTA expected",
                new Remediation("Fix", RemediationArea.WindowsInk)),
        HealthCheckCodes.VMultiNotInstalled => new HealthIssue("vmulti.notInstalled", Severity(f.Severity),
                "VMulti driver not installed",
                "Without VMulti you will not be able to use Windows Ink and get pressure and tilt",
                new Remediation("Fix", RemediationArea.VMulti)),
        HealthCheckCodes.DriverConflict => new HealthIssue("driver.conflict",
            Severity(f.Severity),
            "Conflicting tablet driver detected",
            "Another tablet driver is installed and may interfere with OpenTabletDriver",
            new Remediation("Fix", RemediationArea.DriverCleanup)),
        HealthCheckCodes.ProcessElevated => new HealthIssue("app.elevated", Severity(f.Severity),
            "Running as administrator",
            "OpenTabletDriver does not work correctly if it is running with Administrator permissions",
            Remediation: null),
        HealthCheckCodes.DaemonPermissionsMissing => new HealthIssue("otd.permissionsMissing", Severity(f.Severity),
            "OpenTabletDriver can't read your tablet",
            "Grant Input Monitoring permission to allow OpenTabletDriver to connect to your tablet",
            new Remediation("Open Settings", RemediationArea.InputMonitoring)),
        HealthCheckCodes.TabletNotWinInk => new HealthIssue($"tablet.notWinInk:{f.TabletName}", Severity(f.Severity),
                    $"{f.TabletName}: not using Windows Ink",
                    "This tablet's pen behavior isn't set to a Windows Ink mode, so pressure and tilt " +
                    "won't work.",
                    new Remediation("Fix", RemediationArea.RestorePenBehavior, f.TabletName),
                    Secondary: new Remediation("Review", RemediationArea.TabletPenBehavior, f.TabletName)),
        HealthCheckCodes.TabletDynamicsOff => new HealthIssue($"tablet.dynamicsOff:{f.TabletName}", Severity(f.Severity),
                $"{f.TabletName}: Pen Dynamics filter is off",
                "The Pen Dynamics filter is required for pressure curve and smoothing.",
                new Remediation("Fix", RemediationArea.TabletPenDynamics, f.TabletName)),
        HealthCheckCodes.TabletConfigOverride => new HealthIssue($"tablet.configOverride:{f.TabletName}", Severity(f.Severity),
                $"{f.TabletName}: using a custom tablet config",
                "This tablet is driven by a custom configuration file that replaces OpenTabletDriver's " +
                "built-in, vetted config of the same name. That's fine if you did it on purpose, but if " +
                "the pen behaves oddly, removing the override to restore the built-in is worth trying.",
                new Remediation("Review", RemediationArea.Configs, f.TabletName)),
        HealthCheckCodes.TabletMappingOffScreen => new HealthIssue($"tablet.mappingOffScreen:{f.TabletName}", Severity(f.Severity),
                    $"{f.TabletName}: mapped area is partly off-screen",
                    "This tablet's mapped area extends beyond your displays.",
                    new Remediation("Fix", RemediationArea.TabletMapToPrimary, f.TabletName),
                    Secondary: new Remediation("Review", RemediationArea.TabletDisplayMapping, f.TabletName)),
        HealthCheckCodes.TabletMappingCustom => new HealthIssue($"tablet.mappingCustom:{f.TabletName}", Severity(f.Severity),
                    $"{f.TabletName}: custom display mapping",
                    "This tablet isn't mapped to a single whole display (a custom or multi-display " +
                    "area).",
                    new Remediation("Fix", RemediationArea.TabletMapToPrimary, f.TabletName),
                    Secondary: new Remediation("Review", RemediationArea.TabletDisplayMapping, f.TabletName)),
        HealthCheckCodes.TabletMappingRotation => new HealthIssue($"tablet.mappingRotation:{f.TabletName}", Severity(f.Severity),
                $"{f.TabletName}: unusual active-area rotation",
                "This tablet's active area is rotated by an angle that isn't 0°, 90°, 180°, or 270°",
                new Remediation("Fix", RemediationArea.TabletResetRotation, f.TabletName),
                Secondary: new Remediation("Review", RemediationArea.TabletDisplayMapping, f.TabletName)),
        HealthCheckCodes.LinuxUdevRulesMissing => new HealthIssue("linux.udevRules", Severity(f.Severity),
                "Tablet access rules aren't installed",
                "Linux only lets a program read a tablet if a udev rule grants access to it, and " +
                "OpenTabletDriver's rules aren't installed — so no tablet will be detected no matter " +
                "what else is set up. Installing OpenTabletDriver from your distribution's package " +
                "manager puts them in place; otherwise run its generate-rules.sh and install the output " +
                "to /etc/udev/rules.d/, then replug the tablet.",
                Remediation: null),
        HealthCheckCodes.LinuxHidAccessBlocked => new HealthIssue("linux.hidAccess", Severity(f.Severity),
                "No permission to read tablet devices",
                "The HID devices tablets appear as can't be opened by your user account. Adding " +
                "yourself to the \"input\" group grants that: sudo usermod -aG input $USER, then " +
                "reboot.",
                Remediation: null),
        HealthCheckCodes.LinuxHidAccessPending => new HealthIssue("linux.hidAccess", Severity(f.Severity),
                "Tablet permissions need a restart to take effect",
                "You're in the \"input\" group, but this session started before that was granted, so it " +
                "isn't active yet. " + ((f.Evidence!.UserManagerRunning == true)
                    ? "Reboot to pick it up — logging out likely won't be enough, because your systemd " +
                      "user manager keeps running and hands its old group list to everything it starts."
                    : "Log out and back in, or reboot, to pick it up."),
                Remediation: null),
        HealthCheckCodes.LinuxConflictingModules => new HealthIssue("linux.conflictingModules", Severity(f.Severity),
                "A kernel driver may have claimed your tablet",
                $"The {string.Join(" and ", f.Evidence!.Modules!)} kernel module is loaded. These bind tablets before OpenTabletDriver can, " +
                "which is a common reason a tablet is plugged in and still not detected. " +
                $"sudo rmmod {string.Join(" ", f.Evidence!.Modules!)} unloads them for now; " +
                ((f.Evidence!.ModulesNotBlacklisted == true)
                    ? "blacklisting them in /etc/modprobe.d/ keeps them from returning on the next boot."
                    : "they're already blacklisted, so they won't be back after a reboot."),
                Remediation: null),
        _ => throw new ArgumentOutOfRangeException(nameof(f), f.Code, "No OTA presentation for this health finding."),
    };

    private static HealthIssue DaemonCard(IReadOnlyList<HealthFinding> findings)
    {
        var rows = new List<HealthLink>();
        var foreign = findings.FirstOrDefault(f => f.Code == HealthCheckCodes.ForeignDaemon);
        if (foreign is not null)
            rows.Add(new HealthLink(foreign.Evidence!.ManagedButNotSelected == true
                ? "Another copy of the OTD daemon this app ships"
                : "An OpenTabletDriver you installed, not the bundled copy", "", RemediationArea.Daemon));
        if (findings.Any(f => f.Code == HealthCheckCodes.DaemonSourceUnknown))
            rows.Add(new HealthLink("Location couldn't be read", "", RemediationArea.Daemon));
        var version = findings.FirstOrDefault(f => f.Code == HealthCheckCodes.DaemonVersionMismatch);
        if (version is not null)
            rows.Add(new HealthLink(
                $"Version {version.Evidence!.ActualVersion}, untested with this app — it was built against "
                + $"{version.Evidence.ExpectedVersion}", "", RemediationArea.Daemon));

        return new HealthIssue("otd.driver", Severity(findings.Max(f => f.Severity)),
            "User-supplied OTD Daemon", "OTA using a user-supplied OTD daemon",
            new Remediation("Review", RemediationArea.Daemon), Links: rows);
    }

    private static bool IsPenBehavior(string code) => code is HealthCheckCodes.TabletWinInkOff
        or HealthCheckCodes.TabletPenTipDisabled or HealthCheckCodes.TabletPressureDisabled
        or HealthCheckCodes.TabletTiltDisabled;

    private static HealthIssue PenBehaviorCard(string tablet, IReadOnlyList<HealthFinding> findings)
    {
        var links = new List<HealthLink>();
        if (findings.Any(f => f.Code == HealthCheckCodes.TabletWinInkOff))
            links.Add(new HealthLink("Windows Ink is off", "Pen › basics", RemediationArea.TabletPenBehavior, tablet));
        if (findings.Any(f => f.Code == HealthCheckCodes.TabletPenTipDisabled))
            links.Add(new HealthLink("Pen tip is disabled", "Pen › basics", RemediationArea.TabletPenInputs, tablet));
        if (findings.Any(f => f.Code == HealthCheckCodes.TabletPressureDisabled))
            links.Add(new HealthLink("Pressure sensitivity is off", "Pen › basics", RemediationArea.TabletPenInputs, tablet));
        if (findings.Any(f => f.Code == HealthCheckCodes.TabletTiltDisabled))
            links.Add(new HealthLink("Tilt is disabled", "Pen › pressure", RemediationArea.TabletPenTilt, tablet));

        return new HealthIssue($"tablet.penBehavior:{tablet}", Severity(findings.Max(f => f.Severity)),
            $"{tablet}: pen isn't set up for drawing",
            "Settings artists rely on are turned off. Restore them all in one click, or review each " + "below.",
            new Remediation("Fix", RemediationArea.RestorePenBehavior, tablet), Links: links);
    }
}
