using System.Collections.Generic;

namespace OpenTabletArtist.Domain.Health;

// DisplayMappingValidity lives in the parent Domain namespace (the display-mapping classifier).

/// <summary>
/// How serious a health issue is. Higher value = worse; the "Needs attention" list sorts by this
/// (worst first) and colors the card accordingly. The tiers mirror #317, plus an FYI level (#549):
/// <list type="bullet">
/// <item><see cref="Information"/> — nothing is wrong; a heads-up about a deliberate choice that
/// materially changes behavior (e.g. Windows Ink turned off for mouse compatibility).</item>
/// <item><see cref="Recommendation"/> — works, but isn't the recommended configuration.</item>
/// <item><see cref="Misconfigured"/> — set up incorrectly; a feature won't behave as expected.</item>
/// <item><see cref="Broken"/> — a prerequisite is missing; core functionality won't work.</item>
/// </list>
/// </summary>
public enum HealthSeverity
{
    Information = 0,
    Recommendation = 1,
    Misconfigured = 2,
    Broken = 3,
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
    /// <summary>One-click fix for the artist-pen-behavior bundle: re-enable Windows Ink + pen tip + pressure
    /// + tilt on the tablet in a single apply (#artist-pen-health).</summary>
    RestorePenBehavior,
    /// <summary>Deep-link to a tablet's Pen page › Inputs pivot (pen tip / pressure sensitivity).</summary>
    TabletPenInputs,
    /// <summary>Deep-link to a tablet's Pen page › Dynamics pivot (the Disable tilt toggle).</summary>
    TabletPenTilt,
}

/// <summary>A fix action for an issue: a button label + where it leads. <see cref="TabletName"/> is set
/// for the per-tablet areas so the shell can deep-link to that tablet's tab.</summary>
public sealed record Remediation(string ActionLabel, RemediationArea Area, string? TabletName = null);

/// <summary>One row of a multi-part issue (#artist-pen-health): the specific setting that's off plus a
/// link to where it's reviewed. Used when a single Fix isn't possible because the offending settings live
/// in different places. <see cref="Setting"/> names the problem, <see cref="Destination"/> is the location
/// (e.g. "Pen › inputs"), and <see cref="Area"/>/<see cref="TabletName"/> drive the navigation.</summary>
public sealed record HealthLink(string Setting, string Destination, RemediationArea Area, string TabletName)
{
    /// <summary>Display text: location first, then the setting — e.g. "Pen › movement › Windows Ink is off".</summary>
    public string Label => $"{Destination} › {Setting}";
}

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
    bool IsDeveloperInduced = false,
    // Per-setting review links for an issue whose offenders live in several places (no single fix). Null/
    // empty for ordinary issues, which render just a title + one Fix button (#artist-pen-health).
    IReadOnlyList<HealthLink>? Links = null);

/// <summary>Per-tablet inputs the checks read. <see cref="Mapping"/> is the display-mapping
/// classification (only meaningful for a detected, Absolute-mode tablet; None otherwise).</summary>
public sealed record TabletHealthInput(
    string Name, bool Detected, bool OutputModeIsWinInk,
    DisplayMappingValidity Mapping = DisplayMappingValidity.None,
    // The active-area rotation is a non-cardinal angle (not 0/90/180/270). The app only offers the cardinal
    // angles, so this comes from external tooling and skews the pen axes off the screen. Defaults false.
    bool NonCardinalRotation = false,
    // The Pen Dynamics filter is present + enabled on this profile. Defaults true so a check only fires
    // when a detected tablet is definitively missing/disabled (the always-on invariant regressed).
    bool DynamicsFilterActive = true,
    // This tablet is driven by a user config file that overrides OTD's vetted built-in of the same name
    // (#467). Defaults false so the check only fires when an override is actually detected.
    bool ConfigIsOverride = false,
    // The user deliberately turned Windows Ink off for this tablet (the "Disable Windows Ink" sub-option,
    // #549). When set, a non-WinInk mode is an informational note, not a misconfiguration to fix.
    bool WinInkOptedOut = false,
    // Artist-pen-behavior offenders (#artist-pen-health) — settings that individually work but together
    // leave the pen useless for drawing. All default false so existing tests/inputs are unchanged.
    bool PenTipDisabled = false,     // the pen tip has no binding, so tapping does nothing (#493)
    bool PressureDisabled = false,   // BindingSettings.DisablePressure — flat, pressure-less strokes (#494)
    bool TiltDisabled = false);      // BindingSettings.DisableTilt — apps receive no tilt

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
    /// <summary>The desktop has no host to render the app's tray icon: a GNOME session with no
    /// StatusNotifierItem watcher on the bus. Avalonia still publishes the icon, but nothing shows it, so
    /// closing the window hides the app with no visible icon to reopen from. Linux/GNOME-only — false
    /// everywhere the tray works (Windows, macOS, non-GNOME Linux, GNOME with the AppIndicator extension).</summary>
    public bool TrayHostUnavailable { get; init; }
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
                    "It delivers pen pressure and tilt to your apps; without it you only get basic cursor movement.",
                    new Remediation("Fix", RemediationArea.WindowsInk)));
            }
            else if (i.WinInkVersionMismatch)
            {
                issues.Add(new HealthIssue("winink.versionMismatch", HealthSeverity.Misconfigured,
                    "Windows Ink plugin may be incompatible",
                    "It doesn't declare support for the running driver version — update it to keep pressure and tilt working.",
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
                    "The virtual pen device that pressure and tilt are injected through; without it they won't reach your apps.",
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

        // --- Per-tablet artist-pen-behavior bundle: several settings that each work but together make the
        //     pen useless for drawing (Windows Ink off, pen tip / pressure / tilt disabled). Bundled into
        //     one card because there's no single place to fix or review them (#artist-pen-health). ---
        AddTabletPenBehaviorIssues(issues, i);

        // --- Conflicting manufacturer driver: interferes with OTD detecting the tablet. Windows-only —
        //     this parses OTD's Windows manufacturer-driver warnings and the fix runs a Windows tool (#140). ---
        if (i.IsWindows && i.HasDriverConflict)
        {
            issues.Add(new HealthIssue("driver.conflict",
                i.BlockingDriverConflict ? HealthSeverity.Broken : HealthSeverity.Misconfigured,
                "Conflicting tablet driver detected",
                "A manufacturer driver (Wacom, Huion, XP-Pen, …) can block OpenTabletDriver from detecting your tablet.",
                new Remediation("Fix", RemediationArea.DriverCleanup)));
        }

        // --- Running elevated: no in-app fix (relaunching unelevated is a manual step), so it's an
        //     informational recommendation with no Fix button. ---
        if (i.RunningElevated)
        {
            issues.Add(new HealthIssue("app.elevated", HealthSeverity.Misconfigured,
                "Running as administrator",
                "This can break Windows Ink pressure/tilt and per-app switching — reopen it normally, not elevated.",
                Remediation: null));
        }

        // --- Tray host missing (Linux/GNOME with no StatusNotifierItem host): the tray icon is published
        //     to the bus but nothing renders it, so closing the window hides the app with no visible icon to
        //     bring it back. Informational — tablet input is unaffected; it's a heads-up plus how to restore
        //     the tray. No in-app fix (the GNOME extension is a manual install), so no Fix button. ---
        if (i.TrayHostUnavailable)
        {
            issues.Add(new HealthIssue("tray.gnomeNoSni", HealthSeverity.Information,
                "Tray icon can't be shown",
                "Your GNOME desktop has no system-tray host, so OpenTabletArtist's tray icon won't appear — " +
                "and closing the window hides it with no icon to reopen from (relaunching the app brings the " +
                "window back). Install the \"AppIndicator and KStatusNotifierItem Support\" GNOME extension to " +
                "restore the tray icon.",
                Remediation: null));
        }

        // --- Recommendations ---
        if (i.DaemonConnected && i.ForeignDaemon)
        {
            issues.Add(new HealthIssue("daemon.foreign", HealthSeverity.Recommendation,
                "Using an external daemon",
                "You're connected to a daemon this app didn't start — restart it to use the bundled build.",
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
                    // Absorbed into the artist-pen-behavior bundle when that fires (it always does once
                    // Windows Ink is off), so the two don't double up on Home (#artist-pen-health).
                    if (ArtistBundleFires(t, i.IsWindows)) continue;

                    // Deliberate: the "Don't use Windows Ink" sub-option is on (#549). Not a problem to fix —
                    // just a heads-up that this fundamentally changes how the pen behaves.
                    issues.Add(new HealthIssue($"tablet.winInkOff:{t.Name}", HealthSeverity.Information,
                        $"{t.Name}: Windows Ink is off (mouse-compatibility mode)",
                        "Pressure and tilt are disabled for this tablet. Turn Windows Ink back on from the " +
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

    // The artist-pen-behavior bundle fires for a detected tablet as soon as any one of its offenders —
    // Windows Ink off, or the pen tip / pressure / tilt disabled — is active. Each is individually enough
    // to noticeably hurt drawing, so even a lone one is worth surfacing (#artist-pen-health).
    private static bool ArtistBundleFires(TabletHealthInput t, bool isWindows)
    {
        if (!t.Detected) return false;
        bool winInkOff = isWindows && t.WinInkOptedOut;
        return winInkOff || t.PenTipDisabled || t.PressureDisabled || t.TiltDisabled;
    }

    private static void AddTabletPenBehaviorIssues(List<HealthIssue> issues, HealthInputs i)
    {
        foreach (var t in i.Tablets)
        {
            if (!ArtistBundleFires(t, i.IsWindows)) continue;

            var links = new List<HealthLink>();
            if (i.IsWindows && t.WinInkOptedOut)
                links.Add(new HealthLink("Windows Ink is off", "Pen › movement", RemediationArea.TabletPenBehavior, t.Name));
            if (t.PenTipDisabled)
                links.Add(new HealthLink("Pen tip is disabled", "Pen › inputs", RemediationArea.TabletPenInputs, t.Name));
            if (t.PressureDisabled)
                links.Add(new HealthLink("Pressure sensitivity is off", "Pen › inputs", RemediationArea.TabletPenInputs, t.Name));
            if (t.TiltDisabled)
                links.Add(new HealthLink("Tilt is disabled", "Pen › dynamics", RemediationArea.TabletPenTilt, t.Name));

            issues.Add(new HealthIssue($"tablet.penBehavior:{t.Name}", HealthSeverity.Recommendation,
                $"{t.Name}: pen isn't set up for drawing",
                "Settings artists rely on are turned off — and each lives in a different place. Restore them " +
                "all in one click, or review each below.",
                new Remediation("Fix", RemediationArea.RestorePenBehavior, t.Name),
                Links: links));
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

            // Independent of the mapping-validity classification: a non-cardinal active-area rotation
            // (anything but 0/90/180/270) skews the pen axes off the screen, so strokes come out slanted.
            if (t.NonCardinalRotation)
            {
                issues.Add(new HealthIssue($"tablet.mappingRotation:{t.Name}", HealthSeverity.Misconfigured,
                    $"{t.Name}: unusual active-area rotation",
                    "This tablet's active area is rotated by an angle that isn't 0°, 90°, 180°, or 270°, so the " +
                    "pen axes don't line up with the screen and strokes come out skewed. Set the rotation back " +
                    "to one of the standard angles on the Display Mapping tab.",
                    new Remediation("Fix", RemediationArea.TabletDisplayMapping, t.Name)));
            }
        }
    }
}
