namespace OtdHealth;

/// <summary>Evaluates supplied facts without I/O, platform probing, UI, or side effects.</summary>
public static class HealthEvaluator
{
    /// <summary>
    /// Returns independent findings, sorted by descending severity then ordinal instance ID.
    /// An empty list means no findings in the supplied evidence, not that collection was complete.
    /// </summary>
    /// <exception cref="ArgumentException">Tablet identities are blank or duplicate. Supply distinct
    /// Id values when display names repeat; the evaluator never silently combines distinct subjects.</exception>
    public static IReadOnlyList<HealthFinding> Evaluate(HealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tablet in snapshot.Tablets)
        {
            string identity = tablet.Id ?? tablet.Name;
            if (string.IsNullOrWhiteSpace(identity) || !identities.Add(identity))
                throw new ArgumentException("Tablet identities must be nonblank and unique. " +
                    "Supply distinct Id values for tablets with the same display name.", nameof(snapshot));
        }

        var findings = new List<HealthFinding>();
        void Add(string code, HealthSeverity severity, TabletHealthSnapshot? tablet = null, HealthEvidence? evidence = null) =>
            findings.Add(new HealthFinding(code, severity, tablet?.Name, evidence, tablet?.Id ?? tablet?.Name));

        bool windows = snapshot.Platform == HealthPlatform.Windows;
        if (windows)
        {
            if (snapshot.WinInkInstalled == false)
                Add(HealthCheckCodes.WinInkNotInstalled, HealthSeverity.Broken);
            else if (snapshot.WinInkInstalled == true && snapshot.WinInkVersionMismatch)
                Add(HealthCheckCodes.WinInkVersionMismatch, HealthSeverity.Misconfigured);
            if (snapshot.VMultiInstalled == false)
                Add(HealthCheckCodes.VMultiNotInstalled, HealthSeverity.Broken);
            if (snapshot.HasDriverConflict)
                Add(HealthCheckCodes.DriverConflict,
                    snapshot.BlockingDriverConflict ? HealthSeverity.Broken : HealthSeverity.Misconfigured);
        }

        if (snapshot.RunningElevated)
            Add(HealthCheckCodes.ProcessElevated, HealthSeverity.Misconfigured);

        if (snapshot.DaemonConnected)
        {
            if (snapshot.Platform == HealthPlatform.MacOS && snapshot.DaemonCannotOpenTablet)
                Add(HealthCheckCodes.DaemonPermissionsMissing, HealthSeverity.Broken);
            if (snapshot.ForeignDaemon)
                Add(HealthCheckCodes.ForeignDaemon, HealthSeverity.Information,
                    evidence: new HealthEvidence { ManagedButNotSelected = snapshot.DaemonIsManagedButNotSelected });
            if (snapshot.DaemonSourceUnknown)
                Add(HealthCheckCodes.DaemonSourceUnknown, HealthSeverity.Recommendation);
            if (!string.IsNullOrEmpty(snapshot.DaemonVersion) && !string.IsNullOrEmpty(snapshot.ExpectedOtdVersion)
                && !OtdVersion.SameRelease(snapshot.DaemonVersion, snapshot.ExpectedOtdVersion))
                Add(HealthCheckCodes.DaemonVersionMismatch, HealthSeverity.Recommendation,
                    evidence: new HealthEvidence
                    {
                        ActualVersion = snapshot.DaemonVersion,
                        ExpectedVersion = snapshot.ExpectedOtdVersion,
                    });
        }

        foreach (var tablet in snapshot.Tablets)
        {
            if (!tablet.Detected) continue;
            if (windows && snapshot.WinInkInstalled == true && !tablet.OutputModeIsWinInk && !tablet.WinInkOptedOut)
                Add(HealthCheckCodes.TabletNotWinInk, HealthSeverity.Misconfigured, tablet);
            if (windows && tablet.WinInkOptedOut)
                Add(HealthCheckCodes.TabletWinInkOff, HealthSeverity.Recommendation, tablet);
            if (tablet.PenTipDisabled)
                Add(HealthCheckCodes.TabletPenTipDisabled, HealthSeverity.Recommendation, tablet);
            if (tablet.PressureDisabled)
                Add(HealthCheckCodes.TabletPressureDisabled, HealthSeverity.Recommendation, tablet);
            if (tablet.TiltDisabled)
                Add(HealthCheckCodes.TabletTiltDisabled, HealthSeverity.Recommendation, tablet);
            if (tablet.DynamicsWarningRequired)
                Add(HealthCheckCodes.TabletDynamicsOff, HealthSeverity.Recommendation, tablet);
            if (tablet.ConfigIsOverride)
                Add(HealthCheckCodes.TabletConfigOverride, HealthSeverity.Recommendation, tablet);
            if (tablet.Mapping == DisplayMappingStatus.OffScreen)
                Add(HealthCheckCodes.TabletMappingOffScreen, HealthSeverity.Misconfigured, tablet);
            else if (tablet.Mapping == DisplayMappingStatus.Custom)
                Add(HealthCheckCodes.TabletMappingCustom, HealthSeverity.Recommendation, tablet);
            if (tablet.NonCardinalRotation)
                Add(HealthCheckCodes.TabletMappingRotation, HealthSeverity.Misconfigured, tablet);
        }

        // These probes are proxies. A detected tablet is stronger evidence that prerequisites are met.
        if (snapshot.Platform == HealthPlatform.Linux && !snapshot.Tablets.Any(t => t.Detected))
        {
            if (snapshot.LinuxUdevRulesMissing)
                Add(HealthCheckCodes.LinuxUdevRulesMissing, HealthSeverity.Broken);
            if (snapshot.LinuxHidAccess == HidAccessStatus.Blocked)
                Add(HealthCheckCodes.LinuxHidAccessBlocked, HealthSeverity.Broken);
            else if (snapshot.LinuxHidAccess == HidAccessStatus.PendingRestart)
                Add(HealthCheckCodes.LinuxHidAccessPending, HealthSeverity.Information,
                    evidence: new HealthEvidence { UserManagerRunning = snapshot.LinuxUserManagerRunning });
            if (snapshot.LinuxConflictingModulesLoaded.Count > 0)
                Add(HealthCheckCodes.LinuxConflictingModules, HealthSeverity.Misconfigured,
                    evidence: new HealthEvidence
                    {
                        Modules = Array.AsReadOnly(snapshot.LinuxConflictingModulesLoaded.ToArray()),
                        ModulesNotBlacklisted = snapshot.LinuxConflictingModulesNotBlacklisted,
                    });
        }

        return findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Id, StringComparer.Ordinal).ToArray();
    }
}
