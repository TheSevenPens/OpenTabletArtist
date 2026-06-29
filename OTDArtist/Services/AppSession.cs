using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OtdArtist.Concurrency;
using OtdArtist.Domain;

namespace OtdArtist.Services;

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
    /// <summary>Offer Start only when not connected and not mid-connect (drives the tray + dashboard).</summary>
    bool ShowStartButton { get; }
    string DaemonStatusText { get; }

    /// <summary>True while a Start/Stop/Restart is in progress (drives the busy indicator).</summary>
    bool IsDaemonBusy { get; }
    /// <summary>Current phase of the running lifecycle op, e.g. "Stopping…", "Connecting…".</summary>
    string DaemonOperationStatus { get; }
    /// <summary>Set when a lifecycle op times out or fails; empty otherwise.</summary>
    string DaemonOperationError { get; }
    bool HasDaemonOperationError { get; }

    IAsyncRelayCommand StartDaemonCommand { get; }
    IAsyncRelayCommand StopDaemonCommand { get; }
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

    // --- Lifecycle-operation feedback (Start/Stop/Restart) ---
    [ObservableProperty] private bool _isDaemonBusy;
    [ObservableProperty] private string _daemonOperationStatus = "";
    [ObservableProperty] private string _daemonOperationError = "";
    public bool HasDaemonOperationError => !string.IsNullOrEmpty(DaemonOperationError);
    partial void OnDaemonOperationErrorChanged(string value) => OnPropertyChanged(nameof(HasDaemonOperationError));

    /// <summary>
    /// How long a Start/Restart waits for the connection to come up (and Stop waits for it to
    /// drop) before treating the operation as failed. Settable so tests don't wait the full
    /// wall-clock timeout.
    /// </summary>
    public TimeSpan DaemonOperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool ShowAppOwnedDaemon => IsConnected && IsAppOwnedDaemon;
    public bool ShowForeignDaemonWarning => IsConnected && IsForeignDaemon;
    public bool ShowDaemonSourceUnknown => IsConnected && !IsAppOwnedDaemon && !IsForeignDaemon;
    public bool CanStartDaemon => !IsConnected && _daemonLifecycle.FindExe() != null;

    /// <summary>A connect attempt is in flight (e.g. the ~5s initial auto-connect at startup) but the
    /// daemon hasn't answered yet — used to show a "Connecting…" indicator instead of a bare
    /// "Not connected".</summary>
    public bool IsConnecting => !IsConnected && ConnectionStatus == "Connecting...";

    /// <summary>Show the indeterminate activity indicator for either a lifecycle op or the initial connect.</summary>
    public bool ShowDaemonActivity => IsDaemonBusy || IsConnecting;

    /// <summary>Phase text for the activity indicator.</summary>
    public string DaemonActivityText => IsDaemonBusy ? DaemonOperationStatus : "Connecting…";

    /// <summary>Offer "Start" only when not connected and not already mid-connect.</summary>
    public bool ShowStartButton => !IsConnected && !IsConnecting;

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
            _pluginEnsured = false; // re-ensure the plugin on the next connection
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
        OnPropertyChanged(nameof(CanStartDaemon));
        NotifyOwnership();
        UpdateDaemonActivity();
    }

    partial void OnConnectionStatusChanged(string value) => UpdateDaemonActivity();
    partial void OnIsDaemonBusyChanged(bool value) => UpdateDaemonActivity();
    partial void OnDaemonOperationStatusChanged(string value) => UpdateDaemonActivity();

    /// <summary>Recompute the daemon status text + activity indicators from the connect/op state.</summary>
    private void UpdateDaemonActivity()
    {
        DaemonStatusText = IsConnected ? "Daemon running" : IsConnecting ? "Connecting…" : "Not connected";
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(ShowDaemonActivity));
        OnPropertyChanged(nameof(DaemonActivityText));
        OnPropertyChanged(nameof(ShowStartButton));
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
        _ = MonitorConnectAttemptAsync(++_connectAttempt);
    }

    /// <summary>Begins (re)connecting to the daemon. Used by the shell's Refresh when disconnected.</summary>
    /// <remarks>UI-thread only — it mutates observable connection state directly. The lifecycle
    /// commands below have the same contract (invoked from UI command paths).</remarks>
    public Task ConnectAsync()
    {
        ConnectionStatus = "Connecting...";
        var connect = _daemon.ConnectAsync(_cts.Token);
        _ = MonitorConnectAttemptAsync(++_connectAttempt);
        return connect;
    }

    // Identifies the latest connect attempt so a stale monitor (e.g. from an earlier Refresh) can't
    // clear the indicator out from under a newer attempt (Codex #114).
    private int _connectAttempt;

    /// <summary>A fired-and-forgotten connect (startup / Refresh) keeps retrying in the background;
    /// if it hasn't landed within the timeout, drop the "Connecting…" indicator back to
    /// "Not connected" so the card isn't stuck on a spinner. A later success flips it via the
    /// Connected callback. Only the latest attempt may clear the status.</summary>
    private async Task MonitorConnectAttemptAsync(int attempt)
    {
        if (!await WaitForConnectionStateAsync(connected: true, DaemonOperationTimeout)
            && attempt == _connectAttempt
            && !IsConnected && ConnectionStatus == "Connecting...")
        {
            ConnectionStatus = "Disconnected";
        }
    }

    // --- Data load (IDeviceData) + settings apply (ISettingsCoordinator) ---

    /// <summary>Reloads device data + settings from the daemon. UI-thread only.</summary>
    public Task ReloadAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return LoadDataAsync();
    }

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
                        // Key normalized to lower-case so the read side (keyed by the profile's
                        // Tablet name) matches even if the daemon's reported casing drifts from the
                        // profile's — detection is case-insensitive, so persistence must be too (#138).
                        AppSettings.Set(LastSeenKey(name), DateTime.Now.ToString("o"));
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
                        // lastSeen stays null when this tablet has never been observed connected
                        // while the helper was running — there's no timestamp we could know (#138).
                        DateTime? lastSeen = null;
                        // Prefer the normalized key; fall back to the pre-#138 exact-case key so
                        // history written before the lowercase migration isn't lost (re-detection
                        // rewrites it under the normalized key). (Cursor nit on #142.)
                        var stored = string.IsNullOrEmpty(p.Tablet)
                            ? null
                            : AppSettings.Get(LastSeenKey(p.Tablet)) ?? AppSettings.Get($"LastSeen:{p.Tablet}");
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

            // Make sure our pressure-curve plugin is installed in the app-owned daemon (once per
            // connection). Fire-and-forget so it can't stall the load.
            _ = EnsurePressurePluginAsync();
        }
        catch { /* Data load failed — will retry on next connection/poll */ }
    }

    /// <summary>Persistence key for a tablet's last-seen timestamp. Lower-cased so write (by detected
    /// daemon name) and read (by profile name) match case-insensitively, like detection does (#138).</summary>
    private static string LastSeenKey(string tabletName) => $"LastSeen:{tabletName.ToLowerInvariant()}";

    private readonly PressurePluginInstaller _pluginInstaller = new();
    private bool _pluginEnsured;

    private async Task EnsurePressurePluginAsync()
    {
        if (_pluginEnsured || !IsAppOwnedDaemon || string.IsNullOrEmpty(PluginDirectory)) return;
        _pluginEnsured = true;
        var dir = PluginDirectory;
        var outcome = await Task.Run(() => _pluginInstaller.EnsureInstalled(dir));
        switch (outcome)
        {
            case PluginInstallOutcome.Installed:
                // Fresh directory the daemon hadn't loaded at startup — a load imports it.
                await _daemon.LoadPluginsAsync();
                break;
            case PluginInstallOutcome.Updated:
                // The daemon already loaded the old assembly at startup, and LoadPlugins won't
                // replace an already-loaded directory, so restart it to pick up the new DLL
                // (Codex #98). On reconnect the plugin is up to date, so this can't loop.
                await RestartDaemon();
                break;
        }
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
        // Verify up front so an off-thread caller fails before any side effects (daemon write,
        // disk save, reload) rather than only at the reload's VerifyAccess. (Codex #43.)
        Dispatcher.UIThread.VerifyAccess();
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

    /// <summary>Full digitizer spec (mm dimensions + raw maxima) for a tablet, from the daemon's
    /// reported specs — needed by the calibration mapper (#127). Null if unavailable/degenerate.</summary>
    public Domain.TabletDigitizerSpec? GetDigitizerSpec(string tabletName)
    {
        if (Tablets is not JArray tablets) return null;
        foreach (var t in tablets)
        {
            var props = t["Properties"] ?? t;
            if (props["Name"]?.ToString() != tabletName) continue;
            var d = props["Specifications"]?["Digitizer"];
            if (d == null) return null;
            float w = d["Width"]?.Value<float>() ?? 0, h = d["Height"]?.Value<float>() ?? 0;
            float mx = d["MaxX"]?.Value<float>() ?? 0, my = d["MaxY"]?.Value<float>() ?? 0;
            return w > 0 && h > 0 && mx > 0 && my > 0 ? new Domain.TabletDigitizerSpec(w, h, mx, my) : null;
        }
        return null;
    }

    [RelayCommand]
    private async Task StartDaemon()
    {
        if (IsDaemonBusy) return;
        IsDaemonBusy = true;
        DaemonOperationError = "";
        try
        {
            DaemonOperationStatus = "Starting daemon…";
            _daemon.AutoReconnect = true;
            _daemonLifecycle.Launch();
            OnPropertyChanged(nameof(CanStartDaemon));

            DaemonOperationStatus = "Connecting…";
            ConnectionStatus = "Connecting...";
            _connectAttempt++; // invalidate any pending startup/Refresh monitor
            await _daemon.ConnectAsync(_cts.Token);

            if (!await WaitForConnectionStateAsync(connected: true, DaemonOperationTimeout))
            {
                DaemonOperationError = "The daemon didn't come online within 30 seconds.";
                ConnectionStatus = "Disconnected"; // clear the Connecting… indicator on failure
            }
        }
        finally
        {
            DaemonOperationStatus = "";
            IsDaemonBusy = false;
        }
    }

    [RelayCommand]
    private async Task StopDaemon()
    {
        if (IsDaemonBusy) return;
        IsDaemonBusy = true;
        DaemonOperationError = "";
        try
        {
            DaemonOperationStatus = "Stopping daemon…";
            // User-initiated stop: suppress auto-reconnect so the client doesn't immediately spin
            // trying to reconnect to the daemon we're about to kill (which races a later Start).
            _daemon.AutoReconnect = false;
            _daemonLifecycle.StopAll();

            if (!await WaitForConnectionStateAsync(connected: false, DaemonOperationTimeout))
                DaemonOperationError = "The daemon didn't stop within 30 seconds.";
        }
        finally
        {
            DaemonOperationStatus = "";
            IsDaemonBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestartDaemon()
    {
        if (IsDaemonBusy) return;
        IsDaemonBusy = true;
        DaemonOperationError = "";
        try
        {
            // Stop phase: suppress auto-reconnect while the old process dies, wait for the drop.
            DaemonOperationStatus = "Stopping daemon…";
            _daemon.AutoReconnect = false;
            _daemonLifecycle.StopAll();
            await WaitForConnectionStateAsync(connected: false, DaemonOperationTimeout);

            // Start phase: relaunch and connect to the fresh instance.
            DaemonOperationStatus = "Starting daemon…";
            _daemon.AutoReconnect = true;
            _daemonLifecycle.Launch();

            DaemonOperationStatus = "Connecting…";
            ConnectionStatus = "Connecting...";
            _connectAttempt++; // invalidate any pending startup/Refresh monitor
            await _daemon.ConnectAsync(_cts.Token);

            if (!await WaitForConnectionStateAsync(connected: true, DaemonOperationTimeout))
            {
                DaemonOperationError = "The daemon didn't come online within 30 seconds.";
                ConnectionStatus = "Disconnected"; // clear the Connecting… indicator on failure
            }
        }
        finally
        {
            DaemonOperationStatus = "";
            IsDaemonBusy = false;
        }
    }

    /// <summary>
    /// Awaits until <see cref="IsConnected"/> reaches <paramref name="connected"/> or the timeout
    /// elapses. The daemon's Connected/Disconnected callbacks flip IsConnected on the UI thread;
    /// this polls that state (the commands run on the UI thread, so continuations resume there).
    /// Returns true if the target state was reached, false on timeout.
    /// </summary>
    private async Task<bool> WaitForConnectionStateAsync(bool connected, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (IsConnected != connected)
        {
            if (sw.Elapsed >= timeout) return false;
            try { await Task.Delay(100, _cts.Token); }
            catch (OperationCanceledException) { return IsConnected == connected; }
        }
        return true;
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
