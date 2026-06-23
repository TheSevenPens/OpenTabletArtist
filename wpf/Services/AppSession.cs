using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OtdWindowsHelper.Concurrency;
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

/// <summary>Current OTD settings and the apply+persist path (#41 PR 2).</summary>
public interface ISettingsCoordinator
{
    Settings? CurrentSettings { get; }
    /// <summary>Applies settings to the daemon, persists to disk (best-effort), and reloads.</summary>
    Task ApplyAndSaveSettingsAsync(Settings settings);
}

/// <summary>Tablet/device data produced by the session's data load (#41 PR 2).</summary>
public interface IDeviceData : INotifyPropertyChanged
{
    JToken? Tablets { get; }
    bool HasTablet { get; }
    string TabletName { get; }
    string TabletArea { get; }
    string TabletPressure { get; }
    string TabletButtons { get; }
    IReadOnlyList<ProfileItem> Profiles { get; }
    string OutputMode { get; }
    bool HasWindowsInk { get; }
    string PresetDirectory { get; }
    string PluginDirectory { get; }
    string SettingsFilePath { get; }
    (float Width, float Height)? GetTabletDigitizer(string tabletName);
    /// <summary>Raised (UI thread) after each successful data load.</summary>
    event Action? DataLoaded;
}

public partial class AppSession : ObservableObject, IConnectionState, ISettingsCoordinator, IDeviceData, IDisposable
{
    private readonly DaemonClient _daemon;
    private readonly IDaemonLifecycleService _daemonLifecycle;
    private readonly ISettingsFileStore _settingsStore;
    private readonly CancellationTokenSource _cts = new();
    // Ensures only the most recent data load applies (Connected handler, 3s poll, Refresh). #19.
    private readonly LatestOnlyGate _loadGate = new();
    private Settings? _settings;

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

    // --- Device data (IDeviceData) — populated by the data load ---
    [ObservableProperty] private JToken? _tablets;
    [ObservableProperty] private bool _hasTablet;
    [ObservableProperty] private string _tabletName = "";
    [ObservableProperty] private string _tabletArea = "";
    [ObservableProperty] private string _tabletPressure = "";
    [ObservableProperty] private string _tabletButtons = "";
    [ObservableProperty] private string _outputMode = "";
    [ObservableProperty] private bool _hasWindowsInk;
    [ObservableProperty] private string _presetDirectory = "";
    [ObservableProperty] private string _pluginDirectory = "";
    [ObservableProperty] private string _settingsFilePath = "";
    [ObservableProperty] private List<ProfileItem> _profiles = [];

    IReadOnlyList<ProfileItem> IDeviceData.Profiles => Profiles;
    public Settings? CurrentSettings => _settings;
    public event Action? DataLoaded;

    public AppSession(DaemonClient daemon, IDaemonLifecycleService daemonLifecycle, ISettingsFileStore settingsStore)
    {
        _daemon = daemon;
        _daemonLifecycle = daemonLifecycle;
        _settingsStore = settingsStore;

        _daemon.Connected += () => Dispatcher.UIThread.InvokeAsync(() =>
        {
            ConnectionStatus = "Connected";
            IsConnected = true;
            IsDaemonRunning = true;
            UpdateDaemonSource();
            Connected?.Invoke();
            _ = LoadDataAsync();
        });
        _daemon.Disconnected += () => Dispatcher.UIThread.InvokeAsync(() =>
        {
            ConnectionStatus = "Disconnected";
            IsConnected = false;
            IsDaemonRunning = false;
            IsAppOwnedDaemon = false;
            IsForeignDaemon = false;
            DaemonSourcePath = "";
            HasTablet = false;
            TabletName = "";
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

    /// <summary>
    /// Auto-starts the daemon if not running, then begins connecting. Called once at startup.
    /// </summary>
    /// <remarks>UI-thread only — it mutates observable connection state directly.</remarks>
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
        _ = PollDataAsync();
    }

    /// <summary>Begins (re)connecting to the daemon. Used by the shell's Refresh when disconnected.</summary>
    /// <remarks>UI-thread only — it mutates observable connection state directly. The lifecycle
    /// commands below have the same contract (invoked from UI command paths).</remarks>
    public Task ConnectAsync()
    {
        ConnectionStatus = "Connecting...";
        return _daemon.ConnectAsync(_cts.Token);
    }

    // --- Data load (IDeviceData) + settings apply (ISettingsCoordinator) ---

    /// <summary>Reloads device data + settings from the daemon. UI-thread only.</summary>
    public Task ReloadAsync() => LoadDataAsync();

    // Coalesced entry point: only the most recently requested load applies its results.
    private Task LoadDataAsync() => _loadGate.RunAsync(LoadDataCoreAsync);

    private async Task LoadDataCoreAsync()
    {
        // Mutates observable state, so it must run on the UI thread. Every caller (the connect
        // handler, the poll, apply, reload) marshals via the dispatcher; verify so a future
        // off-thread caller fails loudly instead of corrupting bindings. (Codex note, #42.)
        Dispatcher.UIThread.VerifyAccess();
        try
        {
            // Tablets (JToken — complex runtime type)
            var tablets = await _daemon.GetTabletsAsync();
            Tablets = tablets;

            var detectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tablets.Count > 0)
            {
                foreach (var t in tablets)
                {
                    var props = t["Properties"] ?? t;
                    var name = props["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        detectedNames.Add(name);
                        AppSettings.Set($"LastSeen:{name}", DateTime.Now.ToString("o"));
                    }
                }

                var first = tablets[0];
                var firstProps = first["Properties"] ?? first;
                HasTablet = true;
                TabletName = firstProps["Name"]?.ToString() ?? "Unknown";

                var specs = firstProps["Specifications"];
                var digi = specs?["Digitizer"];
                var pen = specs?["Pen"];
                TabletArea = $"{digi?["Width"]} x {digi?["Height"]} mm";
                TabletPressure = pen?["MaxPressure"]?.ToString() ?? "?";
                TabletButtons = pen?["ButtonCount"]?.ToString() ?? "?";
            }
            else
            {
                HasTablet = false;
                TabletName = "";
                TabletArea = "";
                TabletPressure = "";
                TabletButtons = "";
            }

            // Settings (typed) + profile derivation
            _settings = await _daemon.GetSettingsAsync();
            if (_settings != null)
            {
                Profiles = _settings.Profiles
                    .Select(p =>
                    {
                        bool detected = detectedNames.Contains(p.Tablet);
                        DateTime? lastSeen = null;
                        var stored = AppSettings.Get($"LastSeen:{p.Tablet}");
                        if (stored != null && DateTime.TryParse(stored, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                            lastSeen = dt;
                        if (detected)
                            lastSeen = DateTime.Now;
                        return new ProfileItem(p, detected, lastSeen);
                    })
                    .ToList();

                if (Profiles.Count > 0)
                {
                    var mode = Profiles[0].Profile.OutputMode?.Path;
                    OutputMode = mode?.Split('.').LastOrDefault() ?? "Unknown";
                    HasWindowsInk = mode?.Contains("WinInk", StringComparison.OrdinalIgnoreCase) ?? false;
                }
            }

            // App info paths
            var appInfo = await _daemon.GetAppInfoAsync();
            if (appInfo != null)
            {
                PresetDirectory = appInfo.PresetDirectory ?? "";
                SettingsFilePath = appInfo.SettingsFile ?? "";
                PluginDirectory = appInfo.PluginDirectory ?? "";
            }

            DataLoaded?.Invoke();
        }
        catch { /* Data load failed — will retry on next connection/poll */ }
    }

    private async Task PollDataAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            await Task.Delay(3000, _cts.Token).ConfigureAwait(false);
            if (IsConnected)
            {
                try { await Dispatcher.UIThread.InvokeAsync(LoadDataAsync); }
                catch { }
            }
        }
    }

    /// <summary>Applies settings to the daemon, persists to disk (best-effort), and reloads. UI-thread only.</summary>
    public async Task ApplyAndSaveSettingsAsync(Settings settings)
    {
        _settings = settings;
        await _daemon.SetSettingsAsync(settings);

        // Persist to disk (same as OTD's own UX Save). TODO(#21): surface TrySave failures.
        if (!string.IsNullOrEmpty(SettingsFilePath))
            _settingsStore.TrySave(settings, SettingsFilePath);

        await LoadDataAsync();
    }

    public (float Width, float Height)? GetTabletDigitizer(string tabletName)
    {
        if (Tablets is not JArray tablets) return null;
        foreach (var t in tablets)
        {
            var props = t["Properties"] ?? t;
            if (props["Name"]?.ToString() == tabletName)
            {
                var digi = props["Specifications"]?["Digitizer"];
                if (digi != null)
                {
                    var w = digi["Width"]?.Value<float>() ?? 0;
                    var h = digi["Height"]?.Value<float>() ?? 0;
                    if (w > 0 && h > 0) return (w, h);
                }
            }
        }
        return null;
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
        _loadGate.Dispose();
        _daemon.Dispose();
    }
}
