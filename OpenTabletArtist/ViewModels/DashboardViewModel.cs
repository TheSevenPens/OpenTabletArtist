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
    // Navigate the shell to a tablet's in-app settings page for a health-issue "Fix".
    private readonly Action<string> _navigateToTablet;
    private readonly Action? _openDriverCleanup;
    private readonly Action? _openWindowsInk;
    private readonly Action? _openVMulti;

    public DashboardViewModel(AppSession session, DaemonStatusViewModel daemon, IDialogService dialogs,
        Action<string> navigateToTablet, HealthService health, TabletsOverviewViewModel tablets,
        Action? openDriverCleanup = null, Action? openWindowsInk = null, Action? openVMulti = null)
    {
        _session = session;
        Daemon = daemon;
        _dialogs = dialogs;
        _navigateToTablet = navigateToTablet;
        _openDriverCleanup = openDriverCleanup;
        _openWindowsInk = openWindowsInk;
        _openVMulti = openVMulti;
        Health = health;
        TabletsOverview = tablets;

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
            case RemediationArea.TabletDisplayMapping:
                // Deep-link to the tablet's page; its Display Mapping / Pen Behavior tab carries the fix.
                if (!string.IsNullOrEmpty(r.TabletName)) _navigateToTablet(r.TabletName);
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
