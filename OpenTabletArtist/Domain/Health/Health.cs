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

    /// <summary>Answers the upgrade row about a daemon location OTA no longer starts from: it has been
    /// read, so remember that and stop saying it. An acknowledgement and nothing more — it selects no
    /// daemon and starts none, which is why it is not called "use the bundled one": that row can appear
    /// while OTA is connected to some other external copy, and a button promising a switch would then be
    /// promising something this does not do (#941). Acts rather than navigates; there is nowhere useful
    /// to go.</summary>
    AcknowledgeLegacyDaemonPath,
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
    bool TiltDisabled = false, string? Id = null);      // BindingSettings.DisableTilt — apps receive no tilt

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
    /// <summary>The host is macOS, so its Input Monitoring permission inference is applicable.</summary>
    public bool IsMacOS { get; init; }
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

    /// <summary>The artist has read the row above and said so, which is all the button does — it
    /// chooses no daemon and starts nothing, because there is nothing for it to choose between. Durable,
    /// so the explanation does not come back every launch.</summary>
    public bool LegacyPathNoticeAcknowledged { get; init; }

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
    public bool? WinInkInstalled { get; init; } = false;
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

/// <summary>OTA's presentation boundary: shared findings plus app-only notices, ordered as cards.</summary>
public static class HealthEvaluator
{
    public static IReadOnlyList<HealthIssue> Evaluate(HealthInputs i)
    {
        var issues = HealthIssuePresenter.Present(OtdHealth.HealthEvaluator.Evaluate(ToSnapshot(i))).ToList();

        // --- A daemon location chosen before #930, which OTA no longer launches from. Not conditional
        //     on being connected: the artist most likely to be confused is the one whose chosen daemon
        //     is not running, because that is when OTA starts its own instead. ---
        if (!string.IsNullOrWhiteSpace(i.IgnoredDaemonPath)
            && !i.LegacyPathNoticeAcknowledged
            && !OtdInterop.PathEquality.Same(i.ConnectedDaemonPath, i.IgnoredDaemonPath))
        {
            issues.Add(new HealthIssue("daemon.ignoredPath",
                // Information, not Recommendation: nothing about the current setup is undesirable, and
                // Recommendation says it is. This explains a changed rule; it does not ask for a fix.
                HealthSeverity.Information,
                "OpenTabletArtist no longer starts the driver you chose",
                // "When no daemon is running" rather than a bare "it starts": this row can be on screen
                // while some other OpenTabletDriver is answering, and the policy being described is about
                // what OTA launches, not about what is running now (#946).
                "When no OpenTabletDriver daemon is running, OpenTabletArtist starts the copy it ships. "
                + "It has not moved or deleted your previous OpenTabletDriver files — "
                + $"{i.IgnoredDaemonPath} and its settings are untouched. If that installation is still "
                + "there and you would rather use it, quit OpenTabletArtist from its tray menu: choose "
                + "\"Quit and stop the daemon\" if it is offered, otherwise choose \"Quit\" and stop any "
                + "running OpenTabletDriver yourself. Then start the one you want and launch "
                + "OpenTabletArtist again — only one daemon can run at a time, so yours cannot start "
                + "while another is up. A portable OpenTabletDriver keeps its settings and plugins beside "
                + "itself, so those may look different until you do.",
                new Remediation("Got it", RemediationArea.AcknowledgeLegacyDaemonPath)));
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
            int bySeverity = b.Severity.CompareTo(a.Severity);
            return bySeverity != 0 ? bySeverity : string.CompareOrdinal(a.Id, b.Id);
        });
        return issues;
    }

    private static OtdHealth.HealthSnapshot ToSnapshot(HealthInputs i) => new()
    {
        Platform = i.IsWindows ? OtdHealth.HealthPlatform.Windows
            : i.IsLinux ? OtdHealth.HealthPlatform.Linux
            : i.IsMacOS ? OtdHealth.HealthPlatform.MacOS : OtdHealth.HealthPlatform.Unspecified,
        DaemonConnected = i.DaemonConnected,
        ForeignDaemon = i.ForeignDaemon,
        DaemonIsManagedButNotSelected = i.DaemonIsManagedButNotSelected,
        DaemonSourceUnknown = i.DaemonSourceUnknown,
        DaemonVersion = i.DaemonVersion,
        ExpectedOtdVersion = i.ExpectedOtdVersion,
        DaemonCannotOpenTablet = i.DaemonCannotOpenTablet,
        WinInkInstalled = i.WinInkInstalled,
        WinInkVersionMismatch = i.WinInkVersionMismatch,
        VMultiInstalled = i.VMultiInstalled,
        HasDriverConflict = i.HasDriverConflict,
        BlockingDriverConflict = i.BlockingDriverConflict,
        RunningElevated = i.RunningElevated,
        LinuxUdevRulesMissing = i.LinuxUdevRulesMissing,
        LinuxHidAccess = i.LinuxHidAccess switch
        {
            LinuxHidAccess.Blocked => OtdHealth.HidAccessStatus.Blocked,
            LinuxHidAccess.PendingReboot => OtdHealth.HidAccessStatus.PendingRestart,
            _ => OtdHealth.HidAccessStatus.NoProblemReported,
        },
        LinuxUserManagerRunning = i.LinuxUserManagerRunning,
        LinuxConflictingModulesLoaded = i.LinuxConflictingModulesLoaded,
        LinuxConflictingModulesNotBlacklisted = i.LinuxConflictingModulesNotBlacklisted,
        // Preserve collector subject IDs. Only developer samples and legacy callers use evaluation-local indices.
        Tablets = i.Tablets.Select((t, index) => new OtdHealth.TabletHealthSnapshot(
            t.Name, t.Detected, t.OutputModeIsWinInk,
            t.Mapping switch
            {
                DisplayMappingValidity.Clean => OtdHealth.DisplayMappingStatus.Clean,
                DisplayMappingValidity.Custom => OtdHealth.DisplayMappingStatus.Custom,
                DisplayMappingValidity.OffScreen => OtdHealth.DisplayMappingStatus.OffScreen,
                _ => OtdHealth.DisplayMappingStatus.None,
            },
            t.NonCardinalRotation, !t.DynamicsFilterActive, t.ConfigIsOverride, t.WinInkOptedOut,
            t.PenTipDisabled, t.PressureDisabled, t.TiltDisabled,
            Id: t.Id ?? index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray(),
    };
}
