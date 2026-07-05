using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Shared daemon status/control surface, backed by the <see cref="AppSession"/>. Both the Home problem
/// card and the Daemon page (Advanced → OpenTabletDriver) bind to one instance, so the daemon state +
/// controls aren't duplicated. Home shows it only when there's a problem (<see cref="ShowDaemonProblem"/>);
/// the Daemon page always shows the full card.
/// </summary>
public sealed partial class DaemonStatusViewModel : ObservableObject, IDisposable
{
    private readonly AppSession _session;
    private readonly Action? _openDaemonPage;

    public DaemonStatusViewModel(AppSession session, Action? openDaemonPage = null)
    {
        _session = session;
        _openDaemonPage = openDaemonPage;
        _session.PropertyChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null) OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(IsConnected) or nameof(ShowDaemonActivity) or nameof(HasDaemonOperationError)
            or nameof(ConnectStalled) or nameof(IsDaemonExeMissing))
            OnPropertyChanged(nameof(ShowDaemonProblem));
        if (e.PropertyName is nameof(IsConnected) or nameof(DaemonStatusText) or nameof(ConnectStalled))
            OnPropertyChanged(nameof(HomeProblemText));
    }

    // --- Forwarded session state (see AppSession) ---
    public bool IsConnected => _session.IsConnected;
    public string DaemonStatusText => _session.DaemonStatusText;
    public bool ShowAppOwnedDaemon => _session.ShowAppOwnedDaemon;
    public bool ShowForeignDaemonWarning => _session.ShowForeignDaemonWarning;
    public bool ShowDaemonSourceUnknown => _session.ShowDaemonSourceUnknown;
    public string DaemonSourcePath => _session.DaemonSourcePath;
    public string DaemonVersion => _session.DaemonVersion;
    public bool HasDaemonVersion => _session.HasDaemonVersion;
    public bool CanStartDaemon => _session.CanStartDaemon;
    public bool IsDaemonBusy => _session.IsDaemonBusy;
    public bool ShowDaemonActivity => _session.ShowDaemonActivity;
    public string DaemonActivityText => _session.DaemonActivityText;
    public bool ShowStartButton => _session.ShowStartButton;
    public string DaemonOperationError => _session.DaemonOperationError;
    public bool HasDaemonOperationError => _session.HasDaemonOperationError;
    public bool IsDaemonExeMissing => _session.IsDaemonExeMissing;
    public bool ConnectStalled => _session.ConnectStalled;

    public IAsyncRelayCommand StartDaemonCommand => _session.StartDaemonCommand;
    public IAsyncRelayCommand StopDaemonCommand => _session.StopDaemonCommand;
    public IAsyncRelayCommand RestartDaemonCommand => _session.RestartDaemonCommand;
    public IRelayCommand LaunchOtdUxCommand => _session.LaunchOtdUxCommand;

    /// <summary>Home surfaces the daemon only when there's a connection problem or an op in flight — never
    /// in the normal connected state (an <em>external</em> daemon is a separate Needs-attention item).</summary>
    public bool ShowDaemonProblem =>
        !IsConnected || ShowDaemonActivity || HasDaemonOperationError || ConnectStalled;

    /// <summary>Home-card wording. On Home the daemon card stands alone (no "OpenTabletDriver Daemon"
    /// heading above it, unlike the Daemon page), so the subject is spelled out: "Not connected" reads as
    /// "Not connected to daemon".</summary>
    public string HomeProblemText
    {
        get
        {
            if (IsConnected) return DaemonStatusText;
            if (ConnectStalled) return "Still trying to reach the daemon…";
            return "Not connected to daemon";
        }
    }

    /// <summary>"Fix" = start the daemon if needed and (re)connect. Same as the Start control.</summary>
    public IAsyncRelayCommand FixCommand => _session.StartDaemonCommand;

    /// <summary>Re-check the daemon: reload when connected, otherwise (re)connect.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task Refresh()
    {
        if (_session.IsConnected) await _session.ReloadAsync();
        else await _session.ConnectAsync();
    }

    /// <summary>Navigate to the Daemon page (Advanced → OpenTabletDriver → Daemon).</summary>
    [RelayCommand]
    private void OpenDaemonPage() => _openDaemonPage?.Invoke();

    public void Dispose() => _session.PropertyChanged -= OnSessionChanged;
}
