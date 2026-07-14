using System.Collections.Generic;

namespace OpenTabletArtist.Domain.Health;

// DisplayMappingValidity lives in the parent Domain namespace (the display-mapping classifier).

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
    /// <summary>A specific tablet's Display Mapping tab (the mapped area isn't a clean single display).</summary>
    TabletDisplayMapping,
    /// <summary>The Pen Dynamics filter is off/missing on a profile; fixing re-enables it (always-on invariant).</summary>
    TabletPenDynamics,
    /// <summary>The CONFIGS page under Advanced (a tablet is on a custom config override, #467).</summary>
    Configs,
    /// <summary>A synthetic warning induced from the Developer tab; "fixing" it clears the induced flag.</summary>
    DeveloperInducedWarning,
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
    Remediation? Remediation,
    // True when this issue is only present because a Developer-tab toggle forced it (not genuinely true).
    // Drives the hidden right-click "dismiss" on Home; always false for real issues. Set by HealthService.
    bool IsDeveloperInduced = false);

/// <summary>Per-tablet inputs the checks read. <see cref="Mapping"/> is the display-mapping
/// classification (only meaningful for a detected, Absolute-mode tablet; None otherwise).</summary>
public sealed record TabletHealthInput(
    string Name, bool Detected, bool OutputModeIsWinInk,
    DisplayMappingValidity Mapping = DisplayMappingValidity.None,
    // The Pen Dynamics filter is present + enabled on this profile. Defaults true so a check only fires
    // when a detected tablet is definitively missing/disabled (the always-on invariant regressed).
    bool DynamicsFilterActive = true,
    // This tablet is driven by a user config file that overrides OTD's vetted built-in of the same name
    // (#467). Defaults false so the check only fires when an override is actually detected.
    bool ConfigIsOverride = false,
    // The user deliberately turned Windows Ink off for this tablet (the "Disable Windows Ink" sub-option,
    // #549). When set, a non-WinInk mode is an informational note, not a misconfiguration to fix.
    bool WinInkOptedOut = false);

/// <summary>
/// Snapshot of everything the health checks read. The Dashboard already holds all of this state, so it
/// gathers a snapshot and hands it to <see cref="HealthEvaluator.Evaluate"/>. Keeping it as pure data
/// makes evaluation deterministic and unit-testable with no UI or daemon.
/// </summary>
public sealed record HealthInputs
{
    /// <summary>The host is Windows, so the Windows-only pen-delivery stack (VMulti + Windows Ink) and
    /// Windows manufacturer-driver cleanup are applicable. On non-Windows (macOS/Linux) these checks are
    /// skipped entirely — the daemon delivers pen input through its own native output there, so nagging
    /// about VMulti / Windows Ink / driver conflicts would be noise for things the user can't (and needn't)
    /// fix (#140). Defaults true so Windows behaviour and existing tests are unchanged. (#317)</summary>
    public bool IsWindows { get; init; } = true;
    /// <summary>Connected to the daemon right now.</summary>
    public bool DaemonConnected { get; init; }
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
    /// <summary>The app itself is running elevated (as Administrator), which breaks Windows Ink + per-app switching.</summary>
    public bool RunningElevated { get; init; }
    public IReadOnlyList<TabletHealthInput> Tablets { get; init; } = new List<TabletHealthInput>();
    /// <summary>Synthetic warnings to emit, one per severity, induced from the Developer tab so the
    /// "Needs attention" UI can be reviewed/screenshotted. Empty in normal use.</summary>
    public IReadOnlyList<HealthSeverity> InducedSeverities { get; init; } = new List<HealthSeverity>();
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

        // --- Daemon reachability (not-connected / exe-missing) is surfaced by the Home daemon problem
        //     card + the Daemon page, not here, so it can morph through the connecting state and offer
        //     an "Open daemon page" action. Only the "external daemon" recommendation stays a health
        //     item (see below). ---

        // --- Windows-only pen-delivery stack (Windows Ink plugin + VMulti). Skipped off-Windows, where the
        //     daemon delivers pen input through its own native output and neither concept applies (#140). ---
        if (i.IsWindows)
        {
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
        }

        // --- Per-tablet display mapping: flag anything that isn't a clean single-display mapping, so the
        //     pointer lands where the user expects. Off-screen (dead zones) is worse than a custom area. ---
        AddTabletMappingIssues(issues, i);

        // --- Per-tablet Pen Dynamics: the filter should always be enabled (inert until customized). If a
        //     detected tablet's profile has it off/missing, the pen-dynamics settings won't apply. ---
        AddTabletDynamicsIssues(issues, i);

        // --- Per-tablet config override: the tablet is running a custom config that shadows OTD's vetted
        //     built-in. Often deliberate, but worth surfacing (support / odd-behaviour context). ---
        AddTabletConfigOverrideIssues(issues, i);

        // --- Conflicting manufacturer driver: interferes with OTD detecting the tablet. Windows-only —
        //     this parses OTD's Windows manufacturer-driver warnings and the fix runs a Windows tool (#140). ---
        if (i.IsWindows && i.HasDriverConflict)
        {
            issues.Add(new HealthIssue("driver.conflict",
                i.BlockingDriverConflict ? HealthSeverity.Broken : HealthSeverity.Misconfigured,
                "Conflicting tablet driver detected",
                "A manufacturer tablet driver (Wacom, Huion, XP-Pen, …) is present and can interfere " +
                "with OpenTabletDriver detecting your tablet. Review and remove it in Driver cleanup.",
                new Remediation("Fix", RemediationArea.DriverCleanup)));
        }

        // --- Running elevated: no in-app fix (relaunching unelevated is a manual step), so it's an
        //     informational recommendation with no Fix button. ---
        if (i.RunningElevated)
        {
            issues.Add(new HealthIssue("app.elevated", HealthSeverity.Misconfigured,
                "Running as administrator",
                "OpenTabletArtist is running as administrator, which can break Windows Ink pressure/tilt " +
                "and per-app preset switching. Close it and reopen it normally (not \"Run as administrator\").",
                Remediation: null));
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

        // --- Developer-induced synthetic warnings (Advanced → Developer): one per requested severity, so
        //     the "Needs attention" cards can be reviewed at each tier. The Fix just clears the flag. ---
        foreach (var sev in i.InducedSeverities)
        {
            issues.Add(new HealthIssue($"dev.induced.{sev}", sev,
                $"[Developer] Induced {sev.ToString().ToLowerInvariant()} warning",
                "A synthetic health warning induced from the Developer tab, for reviewing how issues " +
                "render. Fixing it simply clears the Developer-tab flag that caused it to show.",
                new Remediation("Clear", RemediationArea.DeveloperInducedWarning)));
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
                if (t.WinInkOptedOut)
                {
                    // Deliberate: the "Disable Windows Ink" sub-option is on (#549). Not a problem to fix —
                    // just a heads-up that this fundamentally changes how the pen behaves.
                    issues.Add(new HealthIssue($"tablet.winInkOff:{t.Name}", HealthSeverity.Recommendation,
                        $"{t.Name}: Windows Ink is off (mouse-compatibility mode)",
                        "Pressure and tilt are disabled for this tablet. The pen acts like a mouse — dragging " +
                        "selects text and objects instead of scrolling. Turn Windows Ink back on from the " +
                        "tablet's Pen Behavior tab to restore pressure and tilt.",
                        new Remediation("Review", RemediationArea.TabletPenBehavior, t.Name)));
                }
                else
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

    private static void AddTabletDynamicsIssues(List<HealthIssue> issues, HealthInputs i)
    {
        foreach (var t in i.Tablets)
        {
            if (t.Detected && !t.DynamicsFilterActive)
            {
                issues.Add(new HealthIssue($"tablet.dynamicsOff:{t.Name}", HealthSeverity.Recommendation,
                    $"{t.Name}: Pen Dynamics filter is off",
                    "OpenTabletArtist keeps the Pen Dynamics filter enabled so your pressure curve and " +
                    "smoothing always apply. It's currently off or missing on this tablet, so those " +
                    "settings won't take effect. Fixing re-enables it (no effect until you customize it).",
                    new Remediation("Fix", RemediationArea.TabletPenDynamics, t.Name)));
            }
        }
    }

    private static void AddTabletConfigOverrideIssues(List<HealthIssue> issues, HealthInputs i)
    {
        foreach (var t in i.Tablets)
        {
            if (t.Detected && t.ConfigIsOverride)
            {
                issues.Add(new HealthIssue($"tablet.configOverride:{t.Name}", HealthSeverity.Recommendation,
                    $"{t.Name}: using a custom tablet config",
                    "This tablet is driven by a custom configuration file that replaces OpenTabletDriver's " +
                    "built-in, vetted config of the same name. That's fine if you did it on purpose, but if " +
                    "the pen behaves oddly, removing the override to restore the built-in is worth trying.",
                    new Remediation("Review", RemediationArea.Configs, t.Name)));
            }
        }
    }

    private static void AddTabletMappingIssues(List<HealthIssue> issues, HealthInputs i)
    {
        foreach (var t in i.Tablets)
        {
            switch (t.Mapping)
            {
                case DisplayMappingValidity.OffScreen:
                    issues.Add(new HealthIssue($"tablet.mappingOffScreen:{t.Name}", HealthSeverity.Misconfigured,
                        $"{t.Name}: mapped area is partly off-screen",
                        "This tablet's mapped area extends beyond your displays, so part of the tablet maps " +
                        "to space with no screen there and the pen reaches dead zones. Re-map it to a display.",
                        new Remediation("Fix", RemediationArea.TabletDisplayMapping, t.Name)));
                    break;
                case DisplayMappingValidity.Custom:
                    issues.Add(new HealthIssue($"tablet.mappingCustom:{t.Name}", HealthSeverity.Recommendation,
                        $"{t.Name}: custom display mapping",
                        "This tablet isn't mapped to a single whole display (a custom or multi-display area). " +
                        "Re-map it to one display for a standard, undistorted 1:1 setup.",
                        new Remediation("Fix", RemediationArea.TabletDisplayMapping, t.Name)));
                    break;
            }
        }
    }
}
