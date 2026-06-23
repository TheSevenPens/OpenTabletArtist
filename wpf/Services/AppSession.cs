using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.Services;

/// <summary>
/// The daemon connection slice of the shared application session (Option C, #41).
/// Owns the daemon client + its lifecycle, the connection/ownership state, and the
/// connect/start/stop/restart commands. Consumers depend on the narrow
/// <see cref="IConnectionState"/> role and bind its observable properties.
///
/// Thread-affinity rule: this type mutates its observable state only on the UI thread
/// (the daemon's Connected/Disconnected callbacks marshal via the dispatcher), so binders
/// and subscribers never have to marshal. Later steps (#41) move settings + data-load here.
/// </summary>
public interface IConnectionState : INotifyPropertyChanged
{
    bool IsConnected { get; }
    string ConnectionStatus { get; }
    bool IsDaemonRunning { get; }
    bool IsAppOwnedDaemon { get; }
    bool IsForeignDaemon { get; }
    string DaemonSourcePath { get; }
    bool ShowAppOwnedDaemon { get; }
    bool ShowForeignDaemonWarning { get; }
    bool ShowDaemonSourceUnknown { get; }
    bool CanStartDaemon { get; }
    string DaemonStatusText { get; }

    IAsyncRelayCommand StartDaemonCommand { get; }
    IRelayCommand StopDaemonCommand { get; }
    IAsyncRelayCommand RestartDaemonCommand { get; }
    IRelayCommand LaunchOtdUxCommand { get; }
}

public partial class AppSession : ObservableObject, IConnectionState, IDisposable
{
    private readonly DaemonClient _daemon;
    private readonly IDaemonLifecycleService _daemonLifecycle;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// The underlying daemon client. Temporary seam: data-load (settings/tablets/app-info)
    /// still lives in the shell and uses this until it moves into the session (#41 PR 2).
    /// </summary>
    public DaemonClient Daemon => _daemon;

    /// <summary>Raised on the UI thread once the daemon connection is established.</summary>
    public event Action? Connected;
    /// <summary>Raised on the UI thread when the daemon connection drops.</summary>
    public event Action? Disconnected;

    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isDaemonRunning;
    [ObservableProperty] private bool _isAppOwnedDaemon;
    [ObservableProperty] private bool _isForeignDaemon;
    [ObservableProperty] private string _daemonSourcePath = "";
    [ObservableProperty] private string _daemonStatusText = "Not connected";

    public bool ShowAppOwnedDaemon => IsConnected && IsAppOwnedDaemon;
    public bool ShowForeignDaemonWarning => IsConnected && IsForeignDaemon;
    public bool ShowDaemonSourceUnknown => IsConnected && !IsAppOwnedDaemon && !IsForeignDaemon;
    public bool CanStartDaemon => !IsConnected && _daemonLifecycle.FindExe() != null;

    public AppSession(DaemonClient daemon, IDaemonLifecycleService daemonLifecycle)
    {
        _daemon = daemon;
        _daemonLifecycle = daemonLifecycle;

        _daemon.Connected += () => Dispatcher.UIThread.InvokeAsync(() =>
        {
            ConnectionStatus = "Connected";
            IsConnected = true;
            IsDaemonRunning = true;
            UpdateDaemonSource();
            Connected?.Invoke();
        });
        _daemon.Disconnected += () => Dispatcher.UIThread.InvokeAsync(() =>
        {
            ConnectionStatus = "Disconnected";
            IsConnected = false;
            IsDaemonRunning = false;
            IsAppOwnedDaemon = false;
            IsForeignDaemon = false;
            DaemonSourcePath = "";
            Disconnected?.Invoke();
        });
    }

    private void NotifyOwnership()
    {
        OnPropertyChanged(nameof(ShowAppOwnedDaemon));
        OnPropertyChanged(nameof(ShowForeignDaemonWarning));
        OnPropertyChanged(nameof(ShowDaemonSourceUnknown));
    }

    partial void OnIsConnectedChanged(bool value)
    {
        DaemonStatusText = value ? "Daemon running" : "Not connected";
        OnPropertyChanged(nameof(CanStartDaemon));
        NotifyOwnership();
    }

    partial void OnIsAppOwnedDaemonChanged(bool value) => NotifyOwnership();
    partial void OnIsForeignDaemonChanged(bool value) => NotifyOwnership();

    /// <summary>Auto-starts the daemon if not running, then begins connecting. Called once at startup.</summary>
    public async Task StartAndConnectAsync()
    {
        IsDaemonRunning = _daemonLifecycle.IsRunning();
        if (!IsDaemonRunning && _daemonLifecycle.FindExe() != null)
        {
            _daemonLifecycle.Launch();
            await Task.Delay(1000);
        }

        ConnectionStatus = "Connecting...";
        await _daemon.ConnectAsync(_cts.Token);
    }

    /// <summary>Begins (re)connecting to the daemon. Used by the shell's Refresh when disconnected.</summary>
    public Task ConnectAsync()
    {
        ConnectionStatus = "Connecting...";
        return _daemon.ConnectAsync(_cts.Token);
    }

    [RelayCommand]
    private async Task StartDaemon()
    {
        _daemonLifecycle.Launch();
        await Task.Delay(1000);
        OnPropertyChanged(nameof(CanStartDaemon));
        if (!IsConnected)
        {
            ConnectionStatus = "Connecting...";
            await _daemon.ConnectAsync(_cts.Token);
        }
    }

    [RelayCommand]
    private void StopDaemon() => _daemonLifecycle.StopAll();

    [RelayCommand]
    private async Task RestartDaemon()
    {
        _daemonLifecycle.StopAll();

        await Task.Delay(500);
        _daemonLifecycle.Launch();
        await Task.Delay(1000);

        if (!IsConnected)
        {
            ConnectionStatus = "Connecting...";
            await _daemon.ConnectAsync(_cts.Token);
        }
    }

    [RelayCommand]
    private void LaunchOtdUx()
    {
        // Launch the OTD WPF UX from the submodule via dotnet run
        var otdUxProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "external", "OpenTabletDriver", "OpenTabletDriver.UX.Wpf"));

        if (Directory.Exists(otdUxProject))
        {
            Process.Start(new ProcessStartInfo("dotnet", $"run --project \"{otdUxProject}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
    }

    // Determine whether the daemon we're connected to is this project's build.
    // Conservative: only flags "foreign" when we can positively read the server path.
    private void UpdateDaemonSource()
    {
        if (!IsConnected)
        {
            IsAppOwnedDaemon = false;
            IsForeignDaemon = false;
            DaemonSourcePath = "";
            return;
        }

        var actual = GetConnectedDaemonPath();
        DaemonSourcePath = actual ?? "";

        if (actual == null)
        {
            IsAppOwnedDaemon = false;
            IsForeignDaemon = false;
            return;
        }

        var owned = ExecutablePath.SameFile(actual, _daemonLifecycle.ExpectedExePath());
        IsAppOwnedDaemon = owned;
        IsForeignDaemon = !owned;
    }

    private string? GetConnectedDaemonPath()
    {
        var pid = _daemon.GetServerProcessId();
        return pid == null ? null : _daemonLifecycle.GetProcessPath(pid.Value);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _daemon.Dispose();
    }
}
