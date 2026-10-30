using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The Health page (#1050): every finding the app has, worst first, each with the button that fixes it or
/// takes the user to where it is fixed. The cards that used to fill Home's right-hand column.
/// </summary>
/// <remarks>
/// Home needed that column to be tall, and health is going to get more complicated, so the detail moved
/// here and Home keeps a one-line indicator in the top bar instead. Everything a card's buttons do came
/// along unchanged: only where they live moved.
/// </remarks>
public partial class HealthPageViewModel : ObservableObject, IDisposable
{
    private readonly AppSession _session;
    // Navigate the shell to a tablet's in-app settings page for a health-issue "Fix", optionally
    // deep-linking to the tab that carries the fix.
    private readonly Action<string, TabletDetailTab?> _navigateToTablet;
    // Pen settings split out to their own page (#pen-split); the pen-behaviour "Fix" deep-links there.
    private readonly Action<string, TabletDetailTab?>? _navigateToPen;
    private readonly Action? _openDriverCleanup;
    private readonly Action? _openWindowsInk;
    private readonly Action? _openVMulti;
    private readonly Action? _openConfigs;

    public HealthPageViewModel(AppSession session, DaemonStatusViewModel daemon, HealthService health,
        Action<string, TabletDetailTab?> navigateToTablet,
        Action? openDriverCleanup = null, Action? openWindowsInk = null, Action? openVMulti = null,
        Action? openConfigs = null, Action<string, TabletDetailTab?>? navigateToPen = null)
    {
        _session = session;
        Daemon = daemon;
        Health = health;
        _navigateToTablet = navigateToTablet;
        _navigateToPen = navigateToPen;
        _openDriverCleanup = openDriverCleanup;
        _openWindowsInk = openWindowsInk;
        _openVMulti = openVMulti;
        _openConfigs = openConfigs;

        // The all-clear line answers for the whole page, so it has to watch everything the page can show
        // -- the health catalog AND the daemon card, which is deliberately not a health issue.
        Health.PropertyChanged += OnAttentionSourceChanged;
        Daemon.PropertyChanged += OnAttentionSourceChanged;
        Health.Issues.CollectionChanged += OnIssuesChanged;
    }

    private void OnIssuesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasSubtitle));
    }

    /// <summary>Shared daemon status/control surface. The daemon problem card binds to it and shows only
    /// when <see cref="DaemonStatusViewModel.ShowDaemonProblem"/>.</summary>
    public DaemonStatusViewModel Daemon { get; }

    /// <summary>The health-check catalog (#317); the page binds to <c>Health.Issues</c>, and
    /// <see cref="RemediateCommand"/> dispatches each card's Fix button.</summary>
    public HealthService Health { get; }

    /// <summary>The line under the heading: how many findings, and that the worst come first. Counts them
    /// the way the badge on the Health tab does, so the two never disagree about how many there are. It is also the
    /// all-clear, and it counts the daemon too: the daemon problem card lives outside the health catalog
    /// by design (#317), so "no health issues" was never the same as "nothing is wrong", and with no driver
    /// at all the page once said "Nothing needs attention" directly above a warning card explaining that
    /// nothing was connected.</summary>
    public string Subtitle
    {
        get
        {
            // While a connect is in flight the spinner card says so, and "nothing needs attention" above it
            // would be false by the time it finished.
            if (Daemon.ShowDaemonActivity) return "";

            var (_, count) = HealthBadgeViewModel.Summarize(
                Health.Issues.Select(i => i.Severity), HealthBadgeViewModel.DaemonDown(Daemon));
            return count switch
            {
                0 => "Nothing needs attention.",
                1 => "1 finding.",
                _ => $"{count} findings, worst first.",
            };
        }
    }

    /// <summary>Whether the line under the heading has anything to say.</summary>
    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>The daemon problem card shows when the daemon is the problem -- but not for a daemon the app
    /// refused, which has its own card among the findings (#1042).</summary>
    public bool ShowDaemonCard => Daemon.ShowDaemonProblem && !Daemon.IsDaemonRefused;

    /// <summary>Perform an issue's primary fix: run the relevant command in place or navigate to where the
    /// setting lives.</summary>
    [RelayCommand]
    private void Remediate(HealthIssue? issue)
    {
        if (issue?.Remediation is { } r) Dispatch(r, issue);
    }

    /// <summary>Perform an issue's secondary action (#629) -- the extra button beside Fix, e.g. an
    /// off-screen mapping's "Review", which opens the Display Mapping tab while Fix does the re-map.</summary>
    [RelayCommand]
    private void RemediateSecondary(HealthIssue? issue)
    {
        if (issue?.Secondary is { } r) Dispatch(r, issue);
    }

    private void Dispatch(Remediation r, HealthIssue issue)
    {
        switch (r.Area)
        {
            case RemediationArea.WindowsInk:
                _openWindowsInk?.Invoke();
                break;
            case RemediationArea.VMulti:
                _openVMulti?.Invoke();
                break;
            case RemediationArea.DriverCleanup:
                _openDriverCleanup?.Invoke();
                break;
            case RemediationArea.InputMonitoring:
                // A deep link into System Settings. There is no API to grant this on the user's behalf --
                // the most an app may do is open the pane where they can.
                OpenInputMonitoringSettings();
                break;
            case RemediationArea.Daemon:
                // Show the connection, don't act on it. This card reports which OpenTabletDriver is in
                // use, and adopting the user's own install is supported -- restarting it (the old
                // behaviour) both undid their choice and put a confirmation dialog in front of a button
                // labelled "Review". See docs/design/official-otd-release.md.
                Daemon.OpenDaemonPageCommand.Execute(null);
                break;
            case RemediationArea.TabletPenBehavior:
                // Deep-link to the PEN page, on the Movement pivot that carries the fix (pen settings split
                // out of the tablet page, #pen-split). Falls back to the tablet page if pen nav isn't wired.
                if (!string.IsNullOrEmpty(r.TabletName))
                    (_navigateToPen ?? _navigateToTablet)(r.TabletName, TabletDetailTab.PenBehavior);
                break;
            case RemediationArea.TabletDisplayMapping:
                // Deep-link to the tablet's page, on the Display Mapping tab that carries the fix.
                if (!string.IsNullOrEmpty(r.TabletName)) _navigateToTablet(r.TabletName, TabletDetailTab.DisplayMapping);
                break;
            case RemediationArea.TabletMapToPrimary:
                // Re-map the tablet's active area cleanly to the primary display in place, and persist.
                if (!string.IsNullOrEmpty(r.TabletName)) _ = _session.MapTabletToPrimaryDisplayAsync(r.TabletName);
                break;
            case RemediationArea.TabletResetRotation:
                // Snap the tablet's non-cardinal active-area rotation to the nearest standard angle, and persist.
                if (!string.IsNullOrEmpty(r.TabletName)) _ = _session.ResetTabletRotationToCardinalAsync(r.TabletName);
                break;
            case RemediationArea.TabletPenDynamics:
                // Re-enable the always-on Pen Dynamics filter across profiles and persist.
                _ = _session.EnsureDynamicsAsync();
                break;
            case RemediationArea.RestorePenBehavior:
                // Artist-pen-behavior bundle: re-enable Windows Ink + tip + pressure + tilt in one apply.
                _ = _session.RestoreRecommendedPenBehaviorAsync(r.TabletName);
                break;
            case RemediationArea.Configs:
                // Open the CONFIGS page so the user can review/remove the override.
                _openConfigs?.Invoke();
                break;
            case RemediationArea.AcknowledgeLegacyDaemonPath:
                // Records that the explanation was read, and nothing else: no daemon is selected and none
                // is started, which is why the button says "Got it" rather than naming a copy. The old
                // location stays in settings.json untouched, so a downgrade still finds it.
                Services.AppSettings.Set(Services.HealthService.LegacyPathNoticeAcknowledgedKey, "true");
                Health.Refresh();
                break;
            case RemediationArea.DeveloperInducedWarning:
                // Synthetic warning from the Developer tab -- "fixing" it just clears the induced flag.
                Services.DeveloperSettings.Instance.ClearInduced(issue.Severity);
                break;
        }
    }

    /// <summary>Privacy &amp; Security > Input Monitoring. macOS-only; a no-op elsewhere.</summary>
    [RelayCommand]
    private static void OpenInputMonitoringSettings()
        => Services.PlatformShell.OpenInputMonitoringSettings();

    /// <summary>Follow one of a multi-part issue's review links (#artist-pen-health) -- each offending
    /// setting lives on a different Pen-page pivot, so navigate to the one that owns it.</summary>
    /// <remarks>
    /// The attribute is not decoration. Without it no <c>FollowLinkCommand</c> is generated, and
    /// <c>HealthView.axaml</c> binds one -- which is how every link on a health card became a button
    /// that did nothing, silently, for twelve days (#889).
    /// </remarks>
    [RelayCommand]
    private void FollowLink(HealthLink? link)
    {
        if (link is null) return;

        // Not every multi-part card is about a tablet: the OpenTabletDriver card's rows all lead to the
        // Daemon page, and routing those through the tablet deep-link would open a tablet named "".
        if (link.Area == RemediationArea.Daemon) { Daemon.OpenDaemonPageCommand.Execute(null); return; }

        (_navigateToPen ?? _navigateToTablet)(link.TabletName, PenTabFor(link.Area));
    }

    /// <summary>The Pen-page tab that owns the setting a review link is about: the tip and tilt switches are on
    /// basics, the pressure switch on the pressure tab.</summary>
    internal static TabletDetailTab PenTabFor(RemediationArea area) => area switch
    {
        RemediationArea.TabletPenInputs => TabletDetailTab.PenInputs,                                   // basics
        RemediationArea.TabletPenTilt => TabletDetailTab.PenTilt,                                       // tilt
        RemediationArea.TabletPenPressure => TabletDetailTab.PenDynamics,                                // pressure
        _ => TabletDetailTab.PenBehavior, // TabletPenBehavior > Movement
    };

    /// <summary>Hidden developer affordance: right-clicking a synthetic ("developer-induced") card clears
    /// whichever Developer-tab flag produced it. Ignored for real warnings, so they can't be dismissed
    /// this way.</summary>
    [RelayCommand]
    private void DismissDeveloperIssue(HealthIssue? issue)
    {
        if (issue is { IsDeveloperInduced: true })
            Services.DeveloperSettings.Instance.Dismiss(issue);
    }

    private void OnAttentionSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HealthService.HasIssues)
            or nameof(DaemonStatusViewModel.ShowDaemonProblem)
            or nameof(DaemonStatusViewModel.ShowDaemonActivity)
            or nameof(DaemonStatusViewModel.IsDaemonRefused))
        {
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(HasSubtitle));
        }
        if (e.PropertyName is nameof(DaemonStatusViewModel.ShowDaemonProblem)
            or nameof(DaemonStatusViewModel.IsDaemonRefused))
            OnPropertyChanged(nameof(ShowDaemonCard));
    }

    public void Dispose()
    {
        Health.Issues.CollectionChanged -= OnIssuesChanged;
        Health.PropertyChanged -= OnAttentionSourceChanged;
        Daemon.PropertyChanged -= OnAttentionSourceChanged;
    }
}
