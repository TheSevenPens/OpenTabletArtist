using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Dashboard — the application's home/status page (Option C, #41 PR 4).
/// Unlike the narrow page VMs, the Dashboard surfaces the whole session, so it takes the
/// concrete <see cref="AppSession"/> (connection + device + settings) rather than a single
/// role interface, and owns the Dashboard-only concerns: the VMulti driver card and the
/// Windows Ink plugin card. It forwards the session's connection/device state (mirroring its
/// PropertyChanged) so the existing bindings keep working.
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly AppSession _session;
    private readonly IDialogService _dialogs;
    // Navigate the shell to a tablet's in-app settings page (#tablet-ux-overhaul). Replaces opening
    // the modal dialog for a Home card's "Settings".
    private readonly Action<string> _navigateToTablet;
    private readonly Action? _openDriverCleanup;
    // Jump to the Windows Ink Plugin / VMulti pages (Advanced) — where those fixes now live (#317).
    private readonly Action? _openWindowsInk;
    private readonly Action? _openVMulti;

    public DashboardViewModel(AppSession session, IDialogService dialogs, Action<string> navigateToTablet,
        HealthService health, TabletsOverviewViewModel tablets, Action? openDriverCleanup = null,
        Action? openWindowsInk = null, Action? openVMulti = null)
    {
        _session = session;
        _dialogs = dialogs;
        _navigateToTablet = navigateToTablet;
        _openDriverCleanup = openDriverCleanup;
        _openWindowsInk = openWindowsInk;
        _openVMulti = openVMulti;
        Health = health;
        TabletsOverview = tablets;

        _session.PropertyChanged += OnSessionPropertyChanged;

        // Assign the backing field directly so reading the current state here doesn't fire the change
        // handler (which would rewrite the registry) at construction.
        _startWithWindows = StartupService.IsEnabled;
    }

    /// <summary>Whether the run-at-startup toggle is available (Windows only) — hides the card elsewhere.</summary>
    public bool StartupSupported => StartupService.IsSupported;

    /// <summary>Launch OpenTabletArtist when Windows starts (per-user Run key). (#360)</summary>
    [ObservableProperty] private bool _startWithWindows;

    partial void OnStartWithWindowsChanged(bool value) => StartupService.SetEnabled(value);

    /// <summary>The health-check catalog (#317); the Home "Needs attention" list binds to
    /// <c>Health.Issues</c>, and <see cref="RemediateCommand"/> dispatches each card's Fix button.</summary>
    public HealthService Health { get; }

    /// <summary>The tablets overview (list + supported-tablets link), merged into Home so it's the single
    /// landing page — the former standalone Tablets page (#307) was folded in here.</summary>
    public TabletsOverviewViewModel TabletsOverview { get; }

    /// <summary>Perform an issue's fix: run the relevant command in place (install/update/reconnect) or
    /// navigate to the tablet whose setting needs changing.</summary>
    [RelayCommand]
    private void Remediate(HealthIssue? issue)
    {
        if (issue?.Remediation is not { } r) return;
        switch (r.Area)
        {
            case RemediationArea.WindowsInk:
                _openWindowsInk?.Invoke(); // the fix lives on the Windows Ink Plugin page now (#317)
                break;
            case RemediationArea.VMulti:
                _openVMulti?.Invoke(); // the fix lives on the VMulti Driver page now (#317)
                break;
            case RemediationArea.DriverCleanup:
                _openDriverCleanup?.Invoke(); // remove the conflicting driver on the Driver cleanup page
                break;
            case RemediationArea.Daemon:
                if (issue.Id == "daemon.foreign") RestartDaemonCommand.Execute(null);
                else RefreshConnectionCommand.Execute(null);
                break;
            case RemediationArea.TabletPenBehavior:
                if (!string.IsNullOrEmpty(r.TabletName)) _navigateToTablet(r.TabletName);
                break;
        }
    }

    // --- Connection + device state forwarded from the session (mirrored below) ---
    public bool IsConnected => _session.IsConnected;
    public string DaemonStatusText => _session.DaemonStatusText;
    public bool ShowAppOwnedDaemon => _session.ShowAppOwnedDaemon;
    public bool ShowForeignDaemonWarning => _session.ShowForeignDaemonWarning;
    public bool ShowDaemonSourceUnknown => _session.ShowDaemonSourceUnknown;
    public string DaemonSourcePath => _session.DaemonSourcePath;
    // Version read off the connected daemon's binary (#296).
    public string DaemonVersion => _session.DaemonVersion;
    public bool HasDaemonVersion => _session.HasDaemonVersion;
    public bool CanStartDaemon => _session.CanStartDaemon;
    public bool HasTablet => _session.HasTablet;

    // Lifecycle-operation feedback, forwarded from the session (mirrored via PropertyChanged).
    public bool IsDaemonBusy => _session.IsDaemonBusy;
    public string DaemonOperationStatus => _session.DaemonOperationStatus;
    public bool ShowDaemonActivity => _session.ShowDaemonActivity;
    public string DaemonActivityText => _session.DaemonActivityText;
    public bool ShowStartButton => _session.ShowStartButton;
    public string DaemonOperationError => _session.DaemonOperationError;
    public bool HasDaemonOperationError => _session.HasDaemonOperationError;
    public bool IsDaemonExeMissing => _session.IsDaemonExeMissing;
    // Auto-connect gave up waiting but the background loop is still retrying (#296).
    public bool ConnectStalled => _session.ConnectStalled;

    public IAsyncRelayCommand StartDaemonCommand => _session.StartDaemonCommand;
    public IAsyncRelayCommand StopDaemonCommand => _session.StopDaemonCommand;
    public IAsyncRelayCommand RestartDaemonCommand => _session.RestartDaemonCommand;
    public IRelayCommand LaunchOtdUxCommand => _session.LaunchOtdUxCommand;

    [ObservableProperty] private string _tabletStatusText = "No tablet detected";

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null) OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(IDeviceData.HasTablet) or nameof(IDeviceData.TabletName))
            TabletStatusText = _session.HasTablet ? $"{_session.TabletName} detected" : "No tablet detected";
    }

    [RelayCommand]
    private async Task RefreshConnection()
    {
        if (_session.IsConnected) await _session.ReloadAsync();
        else await _session.ConnectAsync();
    }

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionPropertyChanged;
    }
}
