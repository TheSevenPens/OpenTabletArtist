using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Dashboard — the application's home/status page. Surfaces the health checklist
/// ("Needs attention"), the tablets overview, and the startup toggle. The daemon appears here only when
/// there's a problem, via the shared <see cref="DaemonStatusViewModel"/>; the full daemon status +
/// controls live on the Daemon page (Advanced → OpenTabletDriver).
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly AppSession _session;
    private readonly IDialogService _dialogs;
    // Navigate the shell to a tablet's in-app settings page for a health-issue "Fix", optionally
    // deep-linking to the tab that carries the fix.
    private readonly Action<string, TabletDetailTab?> _navigateToTablet;
    // Pen settings split out to their own page (#pen-split); the pen-behaviour "Fix" deep-links there.
    private readonly Action<string, TabletDetailTab?>? _navigateToPen;
    private readonly Action? _openDriverCleanup;
    private readonly Action? _openWindowsInk;
    private readonly Action? _openVMulti;
    private readonly Action? _openConfigs;

    public DashboardViewModel(AppSession session, DaemonStatusViewModel daemon, IDialogService dialogs,
        Action<string, TabletDetailTab?> navigateToTablet, HealthService health, TabletsOverviewViewModel tablets,
        Action? openDriverCleanup = null, Action? openWindowsInk = null, Action? openVMulti = null,
        Action? openConfigs = null, Action<string, TabletDetailTab?>? navigateToPen = null)
    {
        _session = session;
        Daemon = daemon;
        _dialogs = dialogs;
        _navigateToTablet = navigateToTablet;
        _navigateToPen = navigateToPen;
        _openDriverCleanup = openDriverCleanup;
        _openWindowsInk = openWindowsInk;
        _openVMulti = openVMulti;
        _openConfigs = openConfigs;
        Health = health;
        TabletsOverview = tablets;
        // The RESOURCES "Supported tablets" link opens the same in-app catalog the old Home card did,
        // highlighting whichever tablet is currently detected (#155).
        About = new AboutViewModel(dialogs,
            () => TabletsOverview.Tablets.FirstOrDefault(t => t.IsDetected)?.Name);

        _session.PropertyChanged += OnSessionPropertyChanged;
    }

    /// <summary>Shared daemon status/control surface. The Home problem card binds to it and shows only
    /// when <see cref="DaemonStatusViewModel.ShowDaemonProblem"/>; the full card lives on the Daemon page.</summary>
    public DaemonStatusViewModel Daemon { get; }

    /// <summary>The health-check catalog (#317); the Home "Needs attention" list binds to
    /// <c>Health.Issues</c>, and <see cref="RemediateCommand"/> dispatches each card's Fix button.</summary>
    public HealthService Health { get; }

    /// <summary>The tablets overview (list + supported-tablets link), merged into Home.</summary>
    public TabletsOverviewViewModel TabletsOverview { get; }

    /// <summary>The About content (what-is / help / resources / version), shown in Home's right column —
    /// the standalone About page was folded into Home. Home owns its own instance (constructed with the
    /// dialog service so its RESOURCES "Supported tablets" link can open the in-app catalog).</summary>
    public AboutViewModel About { get; }

    /// <summary>Perform an issue's fix: run the relevant command in place or navigate to where the
    /// setting lives.</summary>
    [RelayCommand]
    private void Remediate(HealthIssue? issue)
    {
        if (issue?.Remediation is not { } r) return;
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
            case RemediationArea.Daemon:
                Daemon.RestartDaemonCommand.Execute(null); // external daemon → restart to this app's build
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
            case RemediationArea.TabletPenDynamics:
                // Re-enable the always-on Pen Dynamics filter across profiles and persist.
                _ = _session.EnsureDynamicsAndSaveAsync();
                break;
            case RemediationArea.Configs:
                // Open the CONFIGS page so the user can review/remove the override.
                _openConfigs?.Invoke();
                break;
            case RemediationArea.DeveloperInducedWarning:
                // Synthetic warning from the Developer tab — "fixing" it just clears the induced flag.
                Services.DeveloperSettings.Instance.ClearInduced(issue.Severity);
                break;
        }
    }

    /// <summary>Hidden developer affordance: right-clicking a synthetic ("developer-induced") card on Home
    /// clears whichever Developer-tab flag produced it. Ignored for real warnings, so they can't be
    /// dismissed this way.</summary>
    [RelayCommand]
    private void DismissDeveloperIssue(HealthIssue? issue)
    {
        if (issue is { IsDeveloperInduced: true })
            Services.DeveloperSettings.Instance.Dismiss(issue);
    }

    public bool HasTablet => _session.HasTablet;

    [ObservableProperty] private string _tabletStatusText = "No tablet detected";

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IDeviceData.HasTablet) or nameof(IDeviceData.TabletName))
        {
            OnPropertyChanged(nameof(HasTablet));
            TabletStatusText = _session.HasTablet ? $"{_session.TabletName} detected" : "No tablet detected";
        }
    }

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionPropertyChanged;
    }
}
