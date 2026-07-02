using System.Collections.Generic;

namespace OpenTabletArtist.Domain.Health;

/// <summary>
/// How serious a health issue is. Higher value = worse; the "Needs attention" list sorts by this
/// (worst first) and colors the card accordingly. The three tiers mirror #317:
/// <list type="bullet">
/// <item><see cref="Recommendation"/> — works, but isn't the recommended configuration.</item>
/// <item><see cref="Misconfigured"/> — set up incorrectly; a feature won't behave as expected.</item>
/// <item><see cref="Broken"/> — a prerequisite is missing; core functionality won't work.</item>
/// </list>
/// </summary>
public enum HealthSeverity
{
    Recommendation = 0,
    Misconfigured = 1,
    Broken = 2,
}

/// <summary>Where a fix is performed. Drives the Fix button's action and where the issue surfaces
/// locally (the same issue appears on Home and at the top of the page that owns the fix).</summary>
public enum RemediationArea
{
    /// <summary>The Home daemon card (Start/Restart/Refresh).</summary>
    Daemon,
    /// <summary>The Windows Ink Plugin page under Advanced (install / update). (#317)</summary>
    WindowsInk,
    /// <summary>The VMulti Driver page under Advanced (install / uninstall). (#317)</summary>
    VMulti,
    /// <summary>The Driver cleanup page (remove a conflicting manufacturer driver). (#317)</summary>
    DriverCleanup,
    /// <summary>A specific tablet's Pen Behavior (output mode) tab.</summary>
    TabletPenBehavior,
}

/// <summary>A fix action for an issue: a button label + where it leads. <see cref="TabletName"/> is set
/// for the per-tablet areas so the shell can deep-link to that tablet's tab.</summary>
public sealed record Remediation(string ActionLabel, RemediationArea Area, string? TabletName = null);

/// <summary>One detected configuration/health problem. <see cref="Id"/> is a stable key used to dedupe
/// and to keep the list steady across re-evaluations (and for tests).</summary>
public sealed record HealthIssue(
    string Id,
    HealthSeverity Severity,
    string Title,
    string Detail,
    Remediation? Remediation);

/// <summary>Per-tablet inputs the checks read.</summary>
public sealed record TabletHealthInput(string Name, bool Detected, bool OutputModeIsWinInk);

/// <summary>
/// Snapshot of everything the health checks read. The Dashboard already holds all of this state, so it
/// gathers a snapshot and hands it to <see cref="HealthEvaluator.Evaluate"/>. Keeping it as pure data
/// makes evaluation deterministic and unit-testable with no UI or daemon.
/// </summary>
public sealed record HealthInputs
{
    /// <summary>Connected to the daemon right now.</summary>
    public bool DaemonConnected { get; init; }
    /// <summary>A connect attempt is in flight (suppresses the "not connected" issue during startup).</summary>
    public bool DaemonConnecting { get; init; }
    /// <summary>The daemon exe wasn't found and nothing is running — a hard blocker.</summary>
    public bool DaemonExeMissing { get; init; }
    /// <summary>Connected, but to a daemon this app didn't launch.</summary>
    public bool ForeignDaemon { get; init; }
    /// <summary>The Windows Ink plugin is installed in the daemon's plugin directory.</summary>
    public bool WinInkInstalled { get; init; }
    /// <summary>The installed Windows Ink plugin doesn't declare support for the running driver version.</summary>
    public bool WinInkVersionMismatch { get; init; }
    /// <summary>The VMulti virtual-pen driver is installed. Null = not yet detected (no issue raised until
    /// detection reports), so startup doesn't flash a false "not installed".</summary>
    public bool? VMultiInstalled { get; init; }
    /// <summary>The daemon flagged a conflicting manufacturer tablet driver.</summary>
    public bool HasDriverConflict { get; init; }
    /// <summary>At least one conflicting driver blocks tablet detection (→ Broken instead of Misconfigured).</summary>
    public bool BlockingDriverConflict { get; init; }
    public IReadOnlyList<TabletHealthInput> Tablets { get; init; } = new List<TabletHealthInput>();
}

/// <summary>
/// Pure evaluation of the health-check catalog: inputs in, ordered issue list out (worst severity
/// first, then by id for stability). No UI, no I/O — see <c>HealthService</c> for the live wiring.
/// </summary>
public static class HealthEvaluator
{
    public static IReadOnlyList<HealthIssue> Evaluate(HealthInputs i)
    {
        var issues = new List<HealthIssue>();

        // --- Daemon reachability (hard prerequisites) ---
        if (i.DaemonExeMissing)
        {
            issues.Add(new HealthIssue("daemon.missing", HealthSeverity.Broken,
                "Driver daemon not found",
                "The OpenTabletDriver daemon isn't running and its executable wasn't found, so the app " +
                "can't control your tablet.",
                new Remediation("Fix", RemediationArea.Daemon)));
        }
        else if (!i.DaemonConnected && !i.DaemonConnecting)
        {
            issues.Add(new HealthIssue("daemon.disconnected", HealthSeverity.Broken,
                "Not connected to the daemon",
                "The app isn't connected to the OpenTabletDriver daemon, so tablet settings can't be " +
                "read or changed.",
                new Remediation("Fix", RemediationArea.Daemon)));
        }

        // --- Windows Ink plugin: installed + compatible + actually used ---
        if (!i.WinInkInstalled)
        {
            issues.Add(new HealthIssue("winink.notInstalled", HealthSeverity.Broken,
                "Windows Ink plugin not installed",
                "The Windows Ink plugin delivers pen pressure and tilt to your apps. Without it, drawing " +
                "apps only get basic cursor movement.",
                new Remediation("Fix", RemediationArea.WindowsInk)));
        }
        else if (i.WinInkVersionMismatch)
        {
            issues.Add(new HealthIssue("winink.versionMismatch", HealthSeverity.Misconfigured,
                "Windows Ink plugin may be incompatible",
                "The installed Windows Ink plugin doesn't declare support for the running driver version. " +
                "Updating it keeps pressure and tilt working.",
                new Remediation("Fix", RemediationArea.WindowsInk)));

            // Per-tablet: a detected tablet not using a Windows Ink output mode won't get pressure/tilt.
            AddTabletWinInkIssues(issues, i);
        }
        else
        {
            AddTabletWinInkIssues(issues, i);
        }

        // --- VMulti virtual-pen driver: a prerequisite for the Windows Ink output mode ---
        // Independent of the Windows Ink plugin — both prerequisites surface at once when missing.
        // Only fires on a definitive "not installed" (null = not yet detected).
        if (i.VMultiInstalled == false)
        {
            issues.Add(new HealthIssue("vmulti.notInstalled", HealthSeverity.Broken,
                "VMulti driver not installed",
                "VMulti is the virtual pen device the Windows Ink plugin injects pressure and tilt " +
                "through. Without it, pen pressure and tilt won't reach your apps.",
                new Remediation("Fix", RemediationArea.VMulti)));
        }

        // --- Conflicting manufacturer driver: interferes with OTD detecting the tablet ---
        if (i.HasDriverConflict)
        {
            issues.Add(new HealthIssue("driver.conflict",
                i.BlockingDriverConflict ? HealthSeverity.Broken : HealthSeverity.Misconfigured,
                "Conflicting tablet driver detected",
                "A manufacturer tablet driver (Wacom, Huion, XP-Pen, …) is present and can interfere " +
                "with OpenTabletDriver detecting your tablet. Review and remove it in Driver cleanup.",
                new Remediation("Fix", RemediationArea.DriverCleanup)));
        }

        // --- Recommendations ---
        if (i.DaemonConnected && i.ForeignDaemon)
        {
            issues.Add(new HealthIssue("daemon.foreign", HealthSeverity.Recommendation,
                "Using an external daemon",
                "You're connected to an OpenTabletDriver daemon this app didn't start. Restart it from " +
                "the daemon card to use this app's bundled build.",
                new Remediation("Fix", RemediationArea.Daemon)));
        }

        issues.Sort((a, b) =>
        {
            int bySeverity = ((int)b.Severity).CompareTo((int)a.Severity);
            return bySeverity != 0 ? bySeverity : string.CompareOrdinal(a.Id, b.Id);
        });
        return issues;
    }

    private static void AddTabletWinInkIssues(List<HealthIssue> issues, HealthInputs i)
    {
        foreach (var t in i.Tablets)
        {
            if (t.Detected && !t.OutputModeIsWinInk)
            {
                issues.Add(new HealthIssue($"tablet.notWinInk:{t.Name}", HealthSeverity.Misconfigured,
                    $"{t.Name}: not using Windows Ink",
                    "This tablet's pen behavior isn't set to a Windows Ink mode, so pressure and tilt " +
                    "won't reach your apps.",
                    new Remediation("Fix", RemediationArea.TabletPenBehavior, t.Name)));
            }
        }
    }
}
