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
    /// <summary>The Daemon page (Advanced → OpenTabletDriver → Daemon), which shows the connection and
    /// its Start/Restart/Refresh controls. Navigates — it does not act on the daemon itself.</summary>
    Daemon,
    /// <summary>macOS: open System Settings at Privacy &amp; Security › Input Monitoring, where the grant
    /// the daemon needs is given. OTA cannot grant it — only take the user to it.</summary>
    InputMonitoring,
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

    /// <summary>Answers the upgrade row about a daemon location OTA no longer starts from: the artist
    /// is content with the bundled copy, so remember that and stop saying it. Acts rather than
    /// navigates — there is nowhere useful to go, the decision is the whole of the fix.</summary>
    AcceptBundledDaemon,
    /// <summary>One-click fix for the artist-pen-behavior bundle: re-enable Windows Ink + pen tip + pressure
    /// + tilt on the tablet in a single apply (#artist-pen-health).</summary>
    RestorePenBehavior,
    /// <summary>Deep-link to a tablet's Pen page › Inputs pivot (pen tip / pressure sensitivity).</summary>
    TabletPenInputs,
    /// <summary>Deep-link to a tablet's Pen page › Dynamics pivot (the Disable tilt toggle).</summary>
    TabletPenTilt,
    /// <summary>One-click fix for an off-screen (or custom) tablet mapping (#629): re-map the active area
    /// cleanly to the primary display (whole-monitor, undistorted 1:1). Paired with a "Review" secondary
    /// that opens the Display Mapping tab instead of changing anything.</summary>
    TabletMapToPrimary,
    /// <summary>One-click fix for a non-cardinal active-area rotation (#629): snap it to the nearest standard
    /// angle (0/90/180/270) and re-fit. Paired with a "Review" secondary that opens the Display Mapping tab.</summary>
    TabletResetRotation,
}

/// <summary>A fix action for an issue: a button label + where it leads. <see cref="TabletName"/> is set
/// for the per-tablet areas so the shell can deep-link to that tablet's tab.</summary>
public sealed record Remediation(string ActionLabel, RemediationArea Area, string? TabletName = null);

/// <summary>One row of a multi-part issue (#artist-pen-health): the specific setting that's off plus a
/// link to where it's reviewed. Used when a single Fix isn't possible because the offending settings live
/// in different places. <see cref="Setting"/> names the problem, <see cref="Destination"/> is the location
/// (e.g. "Pen › basics"), and <see cref="Area"/>/<see cref="TabletName"/> drive the navigation.</summary>
public sealed record HealthLink(string Setting, string Destination, RemediationArea Area, string TabletName = "")
{
    /// <summary>Display text: location first, then the setting — e.g. "Pen › basics › Windows Ink is off".
    /// A row with no destination is just the setting: when every row of a card leads to the same page,
    /// repeating that page's name down the list is noise, not orientation.</summary>
    public string Label => string.IsNullOrEmpty(Destination) ? Setting : $"{Destination} › {Setting}";
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
    IReadOnlyList<HealthLink>? Links = null,
    // An optional second action button rendered beside the primary Fix (#629). Used when Fix performs the
    // change directly but a "Review" that just navigates to where the setting lives is still useful. Null
    // for ordinary single-button issues.
    Remediation? Secondary = null);

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

    /// <summary>
    /// A daemon location the artist chose before #930, which OTA no longer starts from ("" when there
    /// is none, which is everyone who never used the picker).
    /// </summary>
    /// <remarks>
    /// Kept as a health row rather than a startup notice because the consequence outlives the moment.
    /// OpenTabletDriver prefers a <c>userdata</c> folder beside its own executable when one exists, and
    /// a portable install has one; the copy OTA ships does not, so it reads the shared location. An
    /// artist whose chosen path was a portable install therefore sees a different set of settings and
    /// plugins, with their own files present but unused — and they may not connect that to an OTA
    /// upgrade days later. A row that stays while the situation does is findable then; a notice shown
    /// once is not.
    /// </remarks>
    public string IgnoredDaemonPath { get; init; } = "";

    /// <summary>The artist has said they are content with the copy OTA ships, so the row above has
    /// been answered and does not come back.</summary>
    public bool IgnoredDaemonPathAccepted { get; init; }

    /// <summary>The daemon actually answering, if its path could be read ("" otherwise). The row is
    /// also finished when this <em>is</em> the old location: they started it, which is the other way the
    /// situation resolves, and no click should be needed to notice that.</summary>
    public string ConnectedDaemonPath { get; init; } = "";

    /// <summary>
    /// The daemon answering is in a location this app manages, but is not the one selected (#882).
    /// </summary>
    public bool DaemonIsManagedButNotSelected { get; init; }
    /// <summary>Connected, but OTA couldn't read which binary answered — so it is neither known to be
    /// ours nor known to be the user's. The daemon is single-instance, so this is not "which of several":
    /// it is a process whose path OTA can't see, e.g. one running as another user or elevated.</summary>
    public bool DaemonSourceUnknown { get; init; }
    /// <summary>Version of the daemon we're connected to, read off its binary ("" if unknown). Adoption
    /// makes this genuinely variable: the user's own OpenTabletDriver install is whatever version they
    /// have, not the one OTA was built from. (docs/design/official-otd-release.md)</summary>
    public string DaemonVersion { get; init; } = "";
    /// <summary>The OpenTabletDriver version OTA was compiled against — the pinned submodule release
    /// ("" if unknown).</summary>
    public string ExpectedOtdVersion { get; init; } = "";
    /// <summary>macOS: the daemon can see a supported tablet on the bus but hasn't detected it. Enumerating
    /// a HID device needs no permission; opening it does — so this is what a missing Input Monitoring grant
    /// looks like from outside the daemon.</summary>
    public bool DaemonCannotOpenTablet { get; init; }
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
    /// <summary>The app itself is running elevated (as Administrator), which breaks Windows Ink.</summary>
    public bool RunningElevated { get; init; }
    /// <summary>The desktop has no host to render the app's tray icon: a GNOME session with no
    /// StatusNotifierItem watcher on the bus. Avalonia still publishes the icon, but nothing shows it, so
    /// closing the window hides the app with no visible icon to reopen from. Linux/GNOME-only — false
    /// everywhere the tray works (Windows, macOS, non-GNOME Linux, GNOME with the AppIndicator extension).</summary>
    public bool TrayHostUnavailable { get; init; }
    /// <summary>How the app's own settings file loaded on startup (#21). <see cref="SettingsLoadStatus.Ok"/>
    /// raises nothing; Preserved/NotPreserved raise the settings-unreadable check with copy that matches what
    /// actually happened. Platform-independent.</summary>
    public SettingsLoadStatus SettingsLoad { get; init; } = SettingsLoadStatus.Ok;
    /// <summary>The backup filename the unreadable settings file was moved to, when
    /// <see cref="SettingsLoad"/> is <see cref="SettingsLoadStatus.Preserved"/>; null otherwise. Named in the
    /// health copy so the user can find it.</summary>
    public string? SettingsBackupName { get; init; }

    /// <summary>The host is Linux, so the tablet prerequisites that only exist there — OpenTabletDriver's
    /// udev rules, the kernel modules that grab tablets first, and permission to open HID devices — are
    /// applicable (#779). Defaults false so Windows/macOS behaviour and existing tests are unchanged.</summary>
    public bool IsLinux { get; init; }
    /// <summary>Linux: OpenTabletDriver's udev rules are not installed in either the local or the
    /// distro-packaged location. Without them the tablet's HID nodes are not accessible to the daemon.</summary>
    public bool LinuxUdevRulesMissing { get; init; }
    /// <summary>Linux: this process cannot open any <c>/dev/hidraw*</c> node. See
    /// <see cref="LinuxHidAccess"/> — the pending case is a granted group that isn't live yet, which is a
    /// much less alarming thing than a refusal.</summary>
    public LinuxHidAccess LinuxHidAccess { get; init; } = LinuxHidAccess.Ok;
    /// <summary>Linux: a reboot rather than a re-login is needed to activate a freshly granted group,
    /// because a per-user systemd manager is running and hands its stale group set to everything it
    /// launches. Only consulted when <see cref="LinuxHidAccess"/> is
    /// <see cref="LinuxHidAccess.PendingReboot"/>.</summary>
    public bool LinuxUserManagerRunning { get; init; }
    /// <summary>Linux: kernel modules currently bound that claim tablet devices before OpenTabletDriver
    /// can. Empty when there are none.</summary>
    public IReadOnlyList<string> LinuxConflictingModulesLoaded { get; init; } = new List<string>();
    /// <summary>Linux: those modules are not blacklisted, so they will be back after a reboot.</summary>
    public bool LinuxConflictingModulesNotBlacklisted { get; init; }

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
                    "Without the plugin, you will not get pressure or tilt",
                    new Remediation("Fix", RemediationArea.WindowsInk)));
            }
            else if (i.WinInkVersionMismatch)
            {
                issues.Add(new HealthIssue("winink.versionMismatch", HealthSeverity.Misconfigured,
                    "Windows Ink plugin may be incompatible",
                    "Not the plugin version OTA expected",
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
                    "Without VMulti you will not be able to use Windows Ink and get pressure and tilt",
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

        // --- A daemon location chosen before #930, which OTA no longer launches from. Not conditional
        //     on being connected: the artist most likely to be confused is the one whose chosen daemon
        //     is not running, because that is when OTA starts its own instead. ---
        if (!string.IsNullOrWhiteSpace(i.IgnoredDaemonPath)
            && !i.IgnoredDaemonPathAccepted
            && !OtdInterop.PathEquality.Same(i.ConnectedDaemonPath, i.IgnoredDaemonPath))
        {
            issues.Add(new HealthIssue("daemon.ignoredPath",
                HealthSeverity.Recommendation,
                "OpenTabletArtist no longer starts the driver you chose",
                $"It starts the copy it ships. Nothing has been deleted: {i.IgnoredDaemonPath} and its "
                + "settings are where you left them. To go back to it, stop the daemon on the Daemon "
                + "page, close OpenTabletArtist, start that OpenTabletDriver yourself, then reopen "
                + "OpenTabletArtist — only one daemon can run at a time, so it cannot start while this "
                + "one is up. A portable OpenTabletDriver keeps its settings and plugins beside itself, "
                + "so those may look different until you do.",
                new Remediation("Use the bundled one", RemediationArea.AcceptBundledDaemon)));
        }

        // --- Conflicting manufacturer driver: interferes with OTD detecting the tablet. Windows-only —
        //     this parses OTD's Windows manufacturer-driver warnings and the fix runs a Windows tool (#140). ---
        if (i.IsWindows && i.HasDriverConflict)
        {
            issues.Add(new HealthIssue("driver.conflict",
                i.BlockingDriverConflict ? HealthSeverity.Broken : HealthSeverity.Misconfigured,
                "Conflicting tablet driver detected",
                "Another tablet driver is installed and may interfere with OpenTabletDriver",
                new Remediation("Fix", RemediationArea.DriverCleanup)));
        }

        // --- Running elevated: no in-app fix (relaunching unelevated is a manual step), so it's an
        //     informational recommendation with no Fix button. ---
        if (i.RunningElevated)
        {
            issues.Add(new HealthIssue("app.elevated", HealthSeverity.Misconfigured,
                "Running as administrator",
                "OpenTabletDriver does not work correctly if it is running with Administrator permissions",
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

        // --- Linux tablet prerequisites (#779), folded in from the standalone tools/OtdLinuxSetup app.
        //
        //     Gated on no tablet having been detected, because that is what makes them a problem. Every one
        //     of these is a proxy: rules can live somewhere this doesn't look, a distro can grant HID access
        //     by a route other than the input group, and a loaded wacom module only matters if it actually
        //     claimed the device. When the daemon has a tablet, the prerequisites are met however they were
        //     met, and saying otherwise would be nagging about a machine that works.
        //
        //     Detection only: the fixes write udev rules, blacklist kernel modules, regenerate the initramfs
        //     and change group membership. Doing that from the app is a separate decision from telling the
        //     user about it, so the copy says what to run. ---
        bool noTabletDetected = i.Tablets.Count == 0 || i.Tablets.All(t => !t.Detected);
        if (i.IsLinux && noTabletDetected)
        {
            if (i.LinuxUdevRulesMissing)
            {
                issues.Add(new HealthIssue("linux.udevRules", HealthSeverity.Broken,
                    "Tablet access rules aren't installed",
                    "Linux only lets a program read a tablet if a udev rule grants access to it, and " +
                    "OpenTabletDriver's rules aren't installed — so no tablet will be detected no matter " +
                    "what else is set up. Installing OpenTabletDriver from your distribution's package " +
                    "manager puts them in place; otherwise run its generate-rules.sh and install the output " +
                    "to /etc/udev/rules.d/, then replug the tablet.",
                    Remediation: null));
            }

            if (i.LinuxHidAccess == LinuxHidAccess.Blocked)
            {
                issues.Add(new HealthIssue("linux.hidAccess", HealthSeverity.Broken,
                    "No permission to read tablet devices",
                    "The HID devices tablets appear as can't be opened by your user account. Adding " +
                    "yourself to the \"input\" group grants that: sudo usermod -aG input $USER, then " +
                    "reboot.",
                    Remediation: null));
            }
            else if (i.LinuxHidAccess == LinuxHidAccess.PendingReboot)
            {
                // Not a fault — the grant exists, it just isn't live. Saying "permission denied" here would
                // send someone to re-run a command that already worked.
                issues.Add(new HealthIssue("linux.hidAccess", HealthSeverity.Information,
                    "Tablet permissions need a restart to take effect",
                    "You're in the \"input\" group, but this session started before that was granted, so it " +
                    "isn't active yet. " + (i.LinuxUserManagerRunning
                        ? "Reboot to pick it up — logging out likely won't be enough, because your systemd " +
                          "user manager keeps running and hands its old group list to everything it starts."
                        : "Log out and back in, or reboot, to pick it up."),
                    Remediation: null));
            }

            if (i.LinuxConflictingModulesLoaded.Count > 0)
            {
                var names = string.Join(" and ", i.LinuxConflictingModulesLoaded);
                issues.Add(new HealthIssue("linux.conflictingModules", HealthSeverity.Misconfigured,
                    "A kernel driver may have claimed your tablet",
                    $"The {names} kernel module is loaded. These bind tablets before OpenTabletDriver can, " +
                    "which is a common reason a tablet is plugged in and still not detected. " +
                    $"sudo rmmod {string.Join(" ", i.LinuxConflictingModulesLoaded)} unloads them for now; " +
                    (i.LinuxConflictingModulesNotBlacklisted
                        ? "blacklisting them in /etc/modprobe.d/ keeps them from returning on the next boot."
                        : "they're already blacklisted, so they won't be back after a reboot."),
                    Remediation: null));
            }
        }

        // --- Settings file couldn't be read (#21): the app started with defaults. Two cases, with copy that
        //     matches what actually happened — never claim a backup exists when the move failed. No in-app fix
        //     (recovery is restoring/copying the file, and it self-clears on the next clean start). ---
        if (i.SettingsLoad == SettingsLoadStatus.Preserved)
        {
            issues.Add(new HealthIssue("settings.unreadable", HealthSeverity.Misconfigured,
                "Could not read saved settings",
                "OpenTabletArtist couldn't read its saved settings, so it started with defaults. The " +
                $"unreadable file was set aside as \"{i.SettingsBackupName}\" next to settings.json, " +
                "Restore that backup to recover your settings.",
                Remediation: null));
        }
        else if (i.SettingsLoad == SettingsLoadStatus.Recovered)
        {
            // Nothing was lost, so this is a notice rather than a fault — but a save did fail at some
            // point, and knowing that is what lets someone look at a failing disk before it matters.
            issues.Add(new HealthIssue("settings.recovered", HealthSeverity.Information,
                "Your settings were recovered from a backup",
                "OpenTabletArtist couldn't read its saved settings, so it loaded the last copy it saved " +
                $"successfully (\"{i.SettingsBackupName}\"). Your preferences are intact; anything changed " +
                "since that copy was written is not.",
                Remediation: null));
        }
        else if (i.SettingsLoad == SettingsLoadStatus.NotPreserved)
        {
            // Worse: the file couldn't be read AND couldn't be moved aside, so a later save may overwrite it.
            issues.Add(new HealthIssue("settings.unreadable", HealthSeverity.Broken,
                "Your settings couldn't be read or backed up",
                "OpenTabletArtist couldn't read its saved settings and couldn't move the file aside to a " +
                "backup, so it started with defaults — and the unreadable settings.json may be overwritten. " +
                "Copy settings.json out of the OpenTabletArtist folder now if you want to try to recover it.",
                Remediation: null));
        }

        // The tablet is plugged in and the driver can see it but cannot read it. Broken, because the pen
        // does not work at all until the grant is given, and OTA cannot give it — only say what to do.
        // macOS-only by construction: nothing else sets the input.
        if (i.DaemonConnected && i.DaemonCannotOpenTablet)
        {
            issues.Add(new HealthIssue("otd.permissionsMissing", HealthSeverity.Broken,
                "OpenTabletDriver can't read your tablet",
                "Grant Input Monitoring permission to allow OpenTabletDriver to connect to your tablet",
                new Remediation("Open Settings", RemediationArea.InputMonitoring)));
        }

        // --- The OpenTabletDriver you're connected to ---
        // One card, not three. These all describe the same subject and all lead to the same page, so as
        // separate issues they stacked up on Home saying "Review" three times over. Same shape as the
        // artist-pen-behavior bundle (#artist-pen-health): a short line, then a row per thing that's true.
        AddDaemonIssue(issues, i);

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
                        "Pressure and tilt are disabled for this tablet.",
                        new Remediation("Review", RemediationArea.TabletPenBehavior, t.Name)));
                }
                else
                {
                    issues.Add(new HealthIssue($"tablet.notWinInk:{t.Name}", HealthSeverity.Misconfigured,
                        $"{t.Name}: not using Windows Ink",
                        "This tablet's pen behavior isn't set to a Windows Ink mode, so pressure and tilt " +
                        "won't work.",
                        new Remediation("Fix", RemediationArea.RestorePenBehavior, t.Name),
                        Secondary: new Remediation("Review", RemediationArea.TabletPenBehavior, t.Name)));
                }
            }
        }
    }

    /// <summary>
    /// The single card describing the connected OpenTabletDriver. Each row is one thing that is true of
    /// it; the card only appears when at least one is. Severity is the worst of the contributing rows, so
    /// an adopted install on its own stays a quiet Information while a version difference lifts it.
    /// </summary>
    private static void AddDaemonIssue(List<HealthIssue> issues, HealthInputs i)
    {
        if (!i.DaemonConnected) return;

        var rows = new List<HealthLink>();
        var severity = HealthSeverity.Information;

        // Not "Not built by OpenTabletArtist" any more: since #794 OTA builds no daemon, so that was
        // true of every daemon including its own bundled one, and distinguished nothing.
        //
        // Two different facts reach here as ForeignDaemon, and saying the wrong one is worse than saying
        // nothing. A daemon the artist installed elsewhere is not the bundled copy; a daemon that IS one
        // of OTA's own, but not the one OTA would start, is also External -- and telling that person
        // "not the bundled copy" is false, and points them at the very thing they are already running
        // (#882). The classification is the same; the sentence must not be.
        //
        // Since #daemon-bundled-only there is no chosen location, so the second case is now a second
        // copy of OTA's own daemon: a dev tree beside a bundled release, or Debug beside Release.
        if (i.ForeignDaemon)
            rows.Add(new HealthLink(
                // Short on purpose: the row clips at roughly this width, and the previous wording lost
                // its last word to that. A sentence whose meaning lives in the clipped part is worse
                // than a short one -- "a different one is answering" became "a different one is a".
                i.DaemonIsManagedButNotSelected
                    ? "Another copy of the OTD daemon this app ships"
                    : "An OpenTabletDriver you installed, not the bundled copy",
                "", RemediationArea.Daemon));

        if (i.DaemonSourceUnknown)
        {
            rows.Add(new HealthLink("Location couldn't be read", "", RemediationArea.Daemon));
            severity = HealthSeverity.Recommendation;
        }

        // The declared support policy (#786, D3): exactly the pinned OpenTabletDriver release, in both
        // directions. Anything else is untested rather than known-broken, so it says so and stays a
        // Recommendation — the card's own text already promises "nothing here stops it working", and a
        // user running a newer OTD than this app was built against has usually done nothing wrong.
        //
        // This nags the day OpenTabletDriver ships a release, until the submodule is bumped. That is
        // inherent in pinning, and otd-release-watch.yml is what keeps the window short.
        if (!string.IsNullOrEmpty(i.DaemonVersion)
            && !string.IsNullOrEmpty(i.ExpectedOtdVersion)
            && !Domain.DaemonVersion.SameRelease(i.DaemonVersion, i.ExpectedOtdVersion))
        {
            rows.Add(new HealthLink(
                $"Version {i.DaemonVersion}, untested with this app — it was built against "
                + $"{i.ExpectedOtdVersion}",
                "", RemediationArea.Daemon));
            severity = HealthSeverity.Recommendation;
        }

        if (rows.Count == 0) return;

        issues.Add(new HealthIssue("otd.driver", severity,
            "User-supplied OTD Daemon",
            "OTA using a user-supplied OTD daemon",
            new Remediation("Review", RemediationArea.Daemon),
            Links: rows));
    }

    private static void AddTabletDynamicsIssues(List<HealthIssue> issues, HealthInputs i)
    {
        foreach (var t in i.Tablets)
        {
            if (t.Detected && !t.DynamicsFilterActive)
            {
                issues.Add(new HealthIssue($"tablet.dynamicsOff:{t.Name}", HealthSeverity.Recommendation,
                    $"{t.Name}: Pen Dynamics filter is off",
                    "The Pen Dynamics filter is required for pressure curve and smoothing.",
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
                links.Add(new HealthLink("Windows Ink is off", "Pen › basics", RemediationArea.TabletPenBehavior, t.Name));
            if (t.PenTipDisabled)
                links.Add(new HealthLink("Pen tip is disabled", "Pen › basics", RemediationArea.TabletPenInputs, t.Name));
            if (t.PressureDisabled)
                links.Add(new HealthLink("Pressure sensitivity is off", "Pen › basics", RemediationArea.TabletPenInputs, t.Name));
            if (t.TiltDisabled)
                links.Add(new HealthLink("Tilt is disabled", "Pen › pressure", RemediationArea.TabletPenTilt, t.Name));

            issues.Add(new HealthIssue($"tablet.penBehavior:{t.Name}", HealthSeverity.Recommendation,
                $"{t.Name}: pen isn't set up for drawing",
                "Settings artists rely on are turned off. Restore them all in one click, or review each " +
                "below.",
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
                        "This tablet's mapped area extends beyond your displays.",
                        new Remediation("Fix", RemediationArea.TabletMapToPrimary, t.Name),
                        Secondary: new Remediation("Review", RemediationArea.TabletDisplayMapping, t.Name)));
                    break;
                case DisplayMappingValidity.Custom:
                    issues.Add(new HealthIssue($"tablet.mappingCustom:{t.Name}", HealthSeverity.Recommendation,
                        $"{t.Name}: custom display mapping",
                        "This tablet isn't mapped to a single whole display (a custom or multi-display " +
                        "area).",
                        new Remediation("Fix", RemediationArea.TabletMapToPrimary, t.Name),
                        Secondary: new Remediation("Review", RemediationArea.TabletDisplayMapping, t.Name)));
                    break;
            }

            // Independent of the mapping-validity classification: a non-cardinal active-area rotation
            // (anything but 0/90/180/270) skews the pen axes off the screen, so strokes come out slanted.
            if (t.NonCardinalRotation)
            {
                issues.Add(new HealthIssue($"tablet.mappingRotation:{t.Name}", HealthSeverity.Misconfigured,
                    $"{t.Name}: unusual active-area rotation",
                    "This tablet's active area is rotated by an angle that isn't 0°, 90°, 180°, or 270°",
                    new Remediation("Fix", RemediationArea.TabletResetRotation, t.Name),
                    Secondary: new Remediation("Review", RemediationArea.TabletDisplayMapping, t.Name)));
            }
        }
    }
}
