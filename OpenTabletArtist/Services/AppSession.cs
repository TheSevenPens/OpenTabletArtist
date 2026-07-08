using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Concurrency;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

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
/// <summary>State of the last settings auto-save (#321). OTA persists every change immediately; this
/// drives a quiet indicator and, importantly, surfaces a failed disk write.</summary>
public enum SettingsSaveState { None, Saving, Saved, Failed }

public interface IConnectionState : INotifyPropertyChanged
{
    bool IsConnected { get; }
    string ConnectionStatus { get; }
    bool IsDaemonRunning { get; }
    bool IsAppOwnedDaemon { get; }
    bool IsForeignDaemon { get; }
    string DaemonSourcePath { get; }
    /// <summary>Version stamped on the connected daemon's executable (read best-effort from its file
    /// via the pipe-server PID; empty when not connected or the path/version couldn't be read). (#296)</summary>
    string DaemonVersion { get; }
    bool HasDaemonVersion { get; }
    bool ShowAppOwnedDaemon { get; }
    bool ShowForeignDaemonWarning { get; }
    bool ShowDaemonSourceUnknown { get; }
    bool CanStartDaemon { get; }
    /// <summary>The daemon exe couldn't be found (not built / not bundled) and none is running, so a
    /// connect attempt is pointless. Checked before every connect; surfaces a clear "build the
    /// solution" message instead of a silent 30s timeout.</summary>
    bool IsDaemonExeMissing { get; }
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

    // Settings auto-save feedback (#321): OTA applies + persists every settings change immediately (no
    // Save button). These surface that quietly, and — critically — surface a disk-save failure so a
    // change that's live but unpersisted doesn't silently vanish on the next restart.
    /// <summary>The save indicator should be shown (Saving / Saved / failed).</summary>
    bool ShowSaveStatus { get; }
    /// <summary>The last settings save failed to write to disk (change is live but not persisted).</summary>
    bool SaveFailed { get; }
    /// <summary>Text for the save indicator.</summary>
    string SaveStatusText { get; }

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
    /// <summary>Applies settings to the daemon and reloads, but does NOT persist to disk — a temporary
    /// live override (profile switching, #320). The saved <c>settings.json</c> default is untouched.</summary>
    Task ApplyLiveOnlyAsync(Settings settings);
    /// <summary>Applies settings to the daemon ONLY — no disk save, no reload, and (unlike
    /// <see cref="ApplyLiveOnlyAsync"/>) does <b>not</b> mutate <see cref="CurrentSettings"/>. For automatic
    /// per-app switching (#167): the editor keeps showing/persisting the user's default while the daemon
    /// runs a transient per-app snapshot. Live pen streams still update (they read daemon reports).</summary>
    Task ApplyEphemeralAsync(Settings settings);
    /// <summary>Reverts the daemon to the saved on-disk default (undoes a live-only override, #320).</summary>
    Task RestoreDefaultAsync();
}

/// <summary>Tablet/device data produced by the session's data load (#41 PR 2).</summary>
public interface IDeviceData : INotifyPropertyChanged
{
    JToken? Tablets { get; }
    /// <summary>Every currently-connected tablet (one Dashboard card each, #190). The scalar
    /// <see cref="HasTablet"/>/<see cref="TabletName"/>/… below mirror the first entry for back-compat.</summary>
    IReadOnlyList<DetectedTablet> DetectedTablets { get; }
    /// <summary>The tablet that single-target flows (tray actions, Test, Diagnostics) act on. Defaults
    /// to the first connected tablet and stays valid as tablets come and go; user-selectable when more
    /// than one is connected (#190 phase 3). Null when nothing is connected.</summary>
    string? ActiveTabletName { get; }
    /// <summary>Choose the active tablet. Ignored unless <paramref name="name"/> is a connected tablet.</summary>
    void SetActiveTablet(string? name);
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
    // Ensures only the most recent data load applies (Connected handler, TabletsChanged event,
    // fallback poll, Refresh). #19.
    private readonly LatestOnlyGate _loadGate = new();

    // Apply-loop hardening (#applyloop): a serialized snapshot of the settings as last loaded from the
    // daemon (the no-op guard: applying byte-identical settings is skipped), and a circuit-breaker that
    // stops a runaway apply↔reload loop from hanging the app (a safety net behind the #433 class of bug).
    private string? _lastLoadedSettingsJson;
    private readonly ApplyLoopBreaker _applyLoopBreaker = new();

    /// <summary>
    /// Fallback reconciliation interval. Detection is event-driven via the daemon's
    /// <c>TabletsChanged</c> push (#170); this poll is only a safety net in case an event is missed,
    /// not the primary detection path — hence much longer than the original 3s magic literal.
    /// </summary>
    private static readonly TimeSpan FallbackPollInterval = TimeSpan.FromSeconds(30);
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
    [ObservableProperty] private string _daemonVersion = "";
    public bool HasDaemonVersion => !string.IsNullOrEmpty(DaemonVersion);
    partial void OnDaemonVersionChanged(string value) => OnPropertyChanged(nameof(HasDaemonVersion));
    [ObservableProperty] private string _daemonStatusText = "Not connected";
    [ObservableProperty] private bool _isDaemonExeMissing;

    /// <summary>Message shown when <see cref="IsDaemonExeMissing"/> — the most common cause of a
    /// dead connection (building only the app, or only running the test suite, never produces the
    /// standalone daemon exe).</summary>
    public const string DaemonExeMissingMessage =
        "OpenTabletDriver.Daemon.exe wasn't found and no daemon is running. Build the whole " +
        "solution (dotnet build OpenTabletArtist.slnx) so the daemon is produced, then try again.";

    // --- Lifecycle-operation feedback (Start/Stop/Restart) ---
    [ObservableProperty] private bool _isDaemonBusy;
    [ObservableProperty] private string _daemonOperationStatus = "";
    [ObservableProperty] private string _daemonOperationError = "";
    public bool HasDaemonOperationError => !string.IsNullOrEmpty(DaemonOperationError);
    partial void OnDaemonOperationErrorChanged(string value) => OnPropertyChanged(nameof(HasDaemonOperationError));

    // --- Settings auto-save feedback (#321) ---
    [ObservableProperty] private SettingsSaveState _saveState;
    public bool ShowSaveStatus => SaveState != SettingsSaveState.None;
    public bool SaveFailed => SaveState == SettingsSaveState.Failed;
    public string SaveStatusText => SaveState switch
    {
        SettingsSaveState.Saving => "Saving…",
        SettingsSaveState.Saved => "Saved",
        SettingsSaveState.Failed => "Couldn't save — your change is live but won't survive a restart",
        _ => "",
    };

    private DispatcherTimer? _saveClearTimer;

    partial void OnSaveStateChanged(SettingsSaveState value)
    {
        OnPropertyChanged(nameof(ShowSaveStatus));
        OnPropertyChanged(nameof(SaveFailed));
        OnPropertyChanged(nameof(SaveStatusText));

        // "Saved" fades to nothing after a moment; "Saving"/"Failed" stay until the next save transition.
        _saveClearTimer?.Stop();
        if (value != SettingsSaveState.Saved) return;
        try
        {
            _saveClearTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _saveClearTimer.Tick -= OnSaveClearTick;
            _saveClearTimer.Tick += OnSaveClearTick;
            _saveClearTimer.Start();
        }
        catch { /* no Dispatcher (headless tests) — the "Saved" text just lingers until the next save */ }
    }

    private void OnSaveClearTick(object? sender, EventArgs e)
    {
        _saveClearTimer?.Stop();
        if (SaveState == SettingsSaveState.Saved) SaveState = SettingsSaveState.None;
    }

    /// <summary>
    /// How long a Start/Restart waits for the connection to come up (and Stop waits for it to
    /// drop) before treating the operation as failed. Settable so tests don't wait the full
    /// wall-clock timeout.
    /// </summary>
    public TimeSpan DaemonOperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // --- Connect progress (#296) ---
    /// <summary>Seconds elapsed on the in-flight connect/lifecycle activity — surfaced as
    /// "…(12s)" so a slow connect looks like progress rather than a frozen spinner. Reset to 0
    /// whenever the activity indicator is not showing.</summary>
    [ObservableProperty] private int _connectElapsedSeconds;

    /// <summary>Human phase for the current connect attempt (e.g. "Starting the daemon…",
    /// "Waiting for the daemon to respond…"). Empty when a lifecycle op drives the indicator
    /// instead (that path uses <see cref="DaemonOperationStatus"/>).</summary>
    [ObservableProperty] private string _connectPhase = "";

    /// <summary>An auto-connect ran past <see cref="DaemonOperationTimeout"/> without reaching the
    /// daemon. The background loop keeps retrying, so we say so plainly (with a Retry affordance)
    /// instead of silently flipping to a bare "Not connected".</summary>
    [ObservableProperty] private bool _connectStalled;

    // Ticks the elapsed-seconds counter while the activity indicator is up. Created lazily on the
    // UI thread (first activity) and guarded so headless tests without a Dispatcher degrade to a
    // static 0 rather than throwing.
    private readonly Stopwatch _connectStopwatch = new();
    private DispatcherTimer? _connectTicker;
    private bool _activityTiming;

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

    /// <summary>Phase text for the activity indicator, with the elapsed seconds appended once the
    /// counter is running (#296) so a slow connect reads as progress.</summary>
    public string DaemonActivityText
    {
        get
        {
            var phase = IsDaemonBusy ? DaemonOperationStatus
                      : string.IsNullOrEmpty(ConnectPhase) ? "Connecting…" : ConnectPhase;
            return ConnectElapsedSeconds > 0 ? $"{phase}  ·  {ConnectElapsedSeconds}s" : phase;
        }
    }

    /// <summary>Offer "Start" only when not connected and not already mid-connect.</summary>
    public bool ShowStartButton => !IsConnected && !IsConnecting;

    // --- Device data (IDeviceData) — populated by the data load ---
    [ObservableProperty] private JToken? _tablets;
    [ObservableProperty] private List<DetectedTablet> _detectedTablets = [];
    [ObservableProperty] private string? _activeTabletName;
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
    IReadOnlyList<DetectedTablet> IDeviceData.DetectedTablets => DetectedTablets;

    /// <summary>Choose the active tablet (#190 phase 3). Ignored unless it's a connected tablet, so a
    /// stale pick from the UI can't point the single-target flows at a disconnected tablet.</summary>
    public void SetActiveTablet(string? name)
    {
        if (name != null && DetectedTablets.Any(t => t.Name == name))
            ActiveTabletName = name;
    }
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
            // Connected, so the exe clearly isn't missing — clear the flag and its stale message.
            IsDaemonExeMissing = false;
            // Any prior "still retrying" / connect-phase state is now moot.
            ConnectStalled = false;
            ConnectPhase = "";
            if (DaemonOperationError == DaemonExeMissingMessage) DaemonOperationError = "";
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
            DaemonVersion = "";
            HasTablet = false;
            TabletName = "";
            DetectedTablets = [];
            ActiveTabletName = null;
            _pluginEnsured = false; // re-ensure the plugin on the next connection
            Disconnected?.Invoke();
        });

        // Event-driven detection (#170): the daemon pushes TabletsChanged on plug/unplug (and on
        // sleep/wake), so reload immediately for near-instant detection and an accurate "last seen",
        // rather than waiting up to a full FallbackPollInterval. Marshalled to the UI thread (the
        // event fires off the RPC thread); the load gate coalesces a burst of events into one load.
        _daemon.TabletsChanged += () => Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (IsConnected) _ = LoadDataAsync();
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
    partial void OnConnectPhaseChanged(string value) => OnPropertyChanged(nameof(DaemonActivityText));
    partial void OnConnectElapsedSecondsChanged(int value) => OnPropertyChanged(nameof(DaemonActivityText));

    /// <summary>Recompute the daemon status text + activity indicators from the connect/op state.</summary>
    private void UpdateDaemonActivity()
    {
        DaemonStatusText = IsConnected ? "Daemon running" : IsConnecting ? "Connecting…" : "Not connected";
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(ShowDaemonActivity));
        OnPropertyChanged(nameof(DaemonActivityText));
        OnPropertyChanged(nameof(ShowStartButton));
        SyncActivityTimer();
    }

    /// <summary>Start/stop the elapsed-seconds ticker on the edges of <see cref="ShowDaemonActivity"/>,
    /// so the counter reflects one contiguous connect/op rather than accumulating across attempts.</summary>
    private void SyncActivityTimer()
    {
        var active = ShowDaemonActivity;
        if (active == _activityTiming) return;
        _activityTiming = active;
        try
        {
            if (active)
            {
                _connectStopwatch.Restart();
                ConnectElapsedSeconds = 0;
                _connectTicker ??= CreateActivityTicker();
                _connectTicker.Start();
            }
            else
            {
                _connectTicker?.Stop();
                _connectStopwatch.Stop();
                ConnectElapsedSeconds = 0;
            }
        }
        catch
        {
            // No Dispatcher (headless tests) — the elapsed counter just stays at 0; everything else works.
        }
    }

    private DispatcherTimer CreateActivityTicker()
    {
        var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ticker.Tick += (_, _) => ConnectElapsedSeconds = (int)_connectStopwatch.Elapsed.TotalSeconds;
        return ticker;
    }

    partial void OnIsAppOwnedDaemonChanged(bool value) => NotifyOwnership();
    partial void OnIsForeignDaemonChanged(bool value) => NotifyOwnership();

    /// <summary>
    /// Auto-starts the daemon if not running, then begins connecting. Called once at startup.
    /// </summary>
    /// <remarks>UI-thread only — it mutates observable connection state directly.</remarks>
    public async Task StartAndConnectAsync()
    {
        // Start the poll up front: it's a no-op while disconnected and keeps device data fresh once
        // a connection is established (even via a later Start), regardless of the early return below.
        _ = PollDataAsync();

        IsDaemonRunning = _daemonLifecycle.IsRunning();

        // Specific pre-connect check (the #1 cause of a dead connection): with no exe to launch and
        // nothing already running, a connect attempt would just time out. Say so plainly and stop.
        if (!DaemonReachable()) { SetDaemonExeMissing(); return; }
        IsDaemonExeMissing = false;

        ConnectStalled = false;
        if (!IsDaemonRunning)
        {
            // No flat delay: the pipe connect below already waits for the daemon's pipe to come up,
            // so a fixed sleep only adds latency (and risks eating the connect timeout). (#246)
            ConnectPhase = "Starting the daemon…";
            _daemonLifecycle.Launch();
            ConnectPhase = "Waiting for the daemon to respond…";
        }
        else
        {
            ConnectPhase = "Connecting to the daemon…";
        }

        ConnectionStatus = "Connecting...";
        await _daemon.ConnectAsync(_cts.Token);
        _ = MonitorConnectAttemptAsync(++_connectAttempt);
    }

    /// <summary>Is there a daemon to talk to — our exe present to launch, or one already running
    /// (including a separately-installed instance)? Gates every connect path so a missing build
    /// surfaces a clear message rather than a silent timeout.</summary>
    private bool DaemonReachable() =>
        _daemonLifecycle.ExpectedExePath() != null || _daemonLifecycle.IsRunning();

    /// <summary>Flag the daemon exe as missing and surface the message; no connect is attempted.</summary>
    private void SetDaemonExeMissing()
    {
        IsDaemonExeMissing = true;
        DaemonOperationError = DaemonExeMissingMessage;
        ConnectionStatus = "Disconnected";
    }

    /// <summary>Begins (re)connecting to the daemon. Used by the shell's Refresh when disconnected.</summary>
    /// <remarks>UI-thread only — it mutates observable connection state directly. The lifecycle
    /// commands below have the same contract (invoked from UI command paths).</remarks>
    public Task ConnectAsync()
    {
        if (!DaemonReachable()) { SetDaemonExeMissing(); return Task.CompletedTask; }
        IsDaemonExeMissing = false;
        ConnectStalled = false;
        ConnectPhase = "Connecting to the daemon…";
        ConnectionStatus = "Connecting...";
        var connect = _daemon.ConnectAsync(_cts.Token);
        _ = MonitorConnectAttemptAsync(++_connectAttempt);
        return connect;
    }

    // Identifies the latest connect attempt so a stale monitor (e.g. from an earlier Refresh) can't
    // clear the indicator out from under a newer attempt (Codex #114).
    private int _connectAttempt;

    /// <summary>A fired-and-forgotten connect (startup / Refresh) keeps retrying in the background;
    /// if it hasn't landed within the timeout, drop the "Connecting…" spinner but flag the attempt as
    /// stalled so the card shows "Couldn't reach the daemon — still retrying" with a Retry button,
    /// instead of a silent "Not connected" that looks like nothing happened (#296). A later success
    /// flips it via the Connected callback. Only the latest attempt may clear the status.</summary>
    private async Task MonitorConnectAttemptAsync(int attempt)
    {
        if (!await WaitForConnectionStateAsync(connected: true, DaemonOperationTimeout)
            && attempt == _connectAttempt
            && !IsConnected && ConnectionStatus == "Connecting...")
        {
            ConnectStalled = true;
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
            var detected = new List<DetectedTablet>(tablets.Count);
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
                detected.Add(ParseDetectedTablet(t));
            }
            DetectedTablets = detected;

            // Keep the active tablet valid: clear it when nothing's connected, and default it to the
            // first tablet when unset or when the previously-active one has disconnected (#190 phase 3).
            if (detected.Count == 0)
                ActiveTabletName = null;
            else if (ActiveTabletName == null || detected.All(t => t.Name != ActiveTabletName))
                ActiveTabletName = detected[0].Name;

            if (detected.Count > 0)
            {
                // Scalars mirror the first tablet for back-compat; the Dashboard now shows all of
                // them via DetectedTablets (#190).
                var first = detected[0];
                HasTablet = true;
                TabletName = first.Name;
                TabletArea = first.Area;
                TabletPressure = first.Pressure;
                TabletButtons = first.Buttons;
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
            // Drop rename-orphaned/duplicate filter stores before deriving profiles, so the Filters
            // and JSON views never show e.g. the dead OtdArtist.* DynamicsFilter next to the current
            // one. Persisted below once paths are known. (Forward guard mirrored in save path.)
            bool staleFiltersRemoved = ProfileFilterMaintenance.CleanLegacyFilters(_settings);
            // #465: on the app-owned daemon, disable any non-approved (third-party / driver-built-in)
            // filter so only our Pen Dynamics / Calibration / Hover filters run and the pen stays
            // consistent. Never touch a foreign daemon's filters. Persisted below if it changed anything.
            bool unapprovedDisabled = !IsForeignDaemon && ProfileFilterMaintenance.DisableUnapprovedFilters(_settings);
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

            // One-time migration: if we stripped orphaned filter stores above, persist the cleaned settings
            // so they don't linger on disk. Best-effort — the in-memory cleanup has already fixed the
            // display. (The dynamics-filter normalization above is intentionally NOT persisted here.)
            if ((staleFiltersRemoved || unapprovedDisabled) && _settings != null)
            {
                try
                {
                    await _daemon.SetSettingsAsync(_settings);
                    if (!string.IsNullOrEmpty(SettingsFilePath))
                        _settingsStore.TrySave(_settings, SettingsFilePath);
                }
                catch { /* leave it; next save will retry the cleanup via the forward guard */ }
            }

            // Baseline for the no-op apply guard: what the daemon currently holds, as we see it now.
            _lastLoadedSettingsJson = SerializeForCompare(_settings);

            DataLoaded?.Invoke();

            // Make sure our pressure-curve plugin is installed in the app-owned daemon (once per
            // connection). Fire-and-forget so it can't stall the load.
            _ = EnsurePressurePluginAsync();
        }
        catch { /* Data load failed — will retry on next connection/poll */ }
    }

    /// <summary>Parse one daemon tablet token into a <see cref="DetectedTablet"/> (name + formatted specs).</summary>
    private static DetectedTablet ParseDetectedTablet(JToken t)
    {
        var props = t["Properties"] ?? t;
        var name = props["Name"]?.ToString() ?? "Unknown";
        var specs = props["Specifications"];
        var digi = specs?["Digitizer"];
        var pen = specs?["Pen"];
        return new DetectedTablet(
            name,
            $"{digi?["Width"]} x {digi?["Height"]} mm",
            pen?["MaxPressure"]?.ToString() ?? "?",
            pen?["ButtonCount"]?.ToString() ?? "?");
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
        await PluginInstallApplier.ApplyAsync(this, outcome);
    }

    /// <summary>Low-frequency fallback reconciliation. Detection itself is event-driven (#170); this
    /// only catches a missed <c>TabletsChanged</c> push so state can't drift indefinitely.</summary>
    private async Task PollDataAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            await Task.Delay(FallbackPollInterval, _cts.Token).ConfigureAwait(false);
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

        // (a) No-op guard: applying settings byte-identical to what the daemon last returned is a pure
        // write of unchanged data — skip it. Avoids redundant daemon writes/reloads and neutralizes the
        // common "same value written back repeatedly" loop without a save flicker.
        if (SerializeForCompare(settings) is { } json && json == _lastLoadedSettingsJson)
            return;

        // (b) Circuit-breaker: if applies are firing faster than any legitimate use, a binding loop is
        // running — skip to break it (no reload → the loop can't re-trigger) instead of hanging the app.
        if (!_applyLoopBreaker.Allow(Environment.TickCount64))
        {
            System.Diagnostics.Debug.WriteLine(
                "AppSession: apply-loop breaker tripped — skipping ApplyAndSave to avoid a hang (a UI binding is looping).");
            return;
        }

        // Forward guard: never write back a stale/duplicate filter store (e.g. left by a rename).
        ProfileFilterMaintenance.CleanLegacyFilters(settings);
        if (!IsForeignDaemon) ProfileFilterMaintenance.DisableUnapprovedFilters(settings); // #465: keep only approved filters enabled
        _settings = settings;

        SaveState = SettingsSaveState.Saving;
        bool saved;
        try
        {
            await _daemon.SetSettingsAsync(settings);
            // Persist to disk (same as OTD's own UX Save). Surface the result (#321/#21): a failed write
            // means the change is live but won't survive a daemon restart, which we must not hide.
            saved = string.IsNullOrEmpty(SettingsFilePath) || _settingsStore.TrySave(settings, SettingsFilePath);
        }
        catch
        {
            SaveState = SettingsSaveState.Failed;
            throw; // keep the existing error-propagation contract for callers
        }
        SaveState = saved ? SettingsSaveState.Saved : SettingsSaveState.Failed;

        await LoadDataAsync();
    }

    /// <summary>Deterministic string form of the settings for cheap equality comparison (the no-op apply
    /// guard). Best-effort — returns null on any serialization failure, which just disables the guard for
    /// that call (the circuit-breaker still backs it up).</summary>
    private static string? SerializeForCompare(Settings? settings)
    {
        if (settings == null) return null;
        try { return Newtonsoft.Json.JsonConvert.SerializeObject(settings); }
        catch { return null; }
    }

    /// <inheritdoc />
    public async Task ApplyLiveOnlyAsync(Settings settings)
    {
        Dispatcher.UIThread.VerifyAccess();
        ProfileFilterMaintenance.CleanLegacyFilters(settings);
        if (!IsForeignDaemon) ProfileFilterMaintenance.DisableUnapprovedFilters(settings); // #465: keep only approved filters enabled
        _settings = settings;
        // Apply live, reload — but deliberately do NOT TrySave: this is a temporary override, so the
        // saved settings.json default must stay intact (#320). No save chip either; the override cue owns
        // the feedback.
        await _daemon.SetSettingsAsync(settings);
        await LoadDataAsync();
    }

    /// <inheritdoc />
    public async Task ApplyEphemeralAsync(Settings settings)
    {
        Dispatcher.UIThread.VerifyAccess();
        ProfileFilterMaintenance.CleanLegacyFilters(settings);
        if (!IsForeignDaemon) ProfileFilterMaintenance.DisableUnapprovedFilters(settings); // #465: keep only approved filters enabled
        // Per-app switch (#167): apply to the daemon ONLY. Deliberately does NOT touch _settings, TrySave,
        // or reload — CurrentSettings must stay on the user's default so the editor edits the default, not
        // the transient per-app snapshot. Live pen streams read daemon reports, so they still update.
        await _daemon.SetSettingsAsync(settings);
    }

    /// <inheritdoc />
    public async Task RestoreDefaultAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        // Re-read the saved default from disk (untouched by a live-only override) and apply it.
        if (!string.IsNullOrEmpty(SettingsFilePath)
            && _settingsStore.TryLoad(SettingsFilePath, out var def) && def != null)
        {
            _settings = def;
            await _daemon.SetSettingsAsync(def);
        }
        await LoadDataAsync();
    }

    /// <summary>Force the Pen Dynamics filter present + enabled across all profiles and persist — the Home
    /// health-check "Fix" backing the always-on invariant (#dynamics-always-on). No-op if nothing changed.</summary>
    public async Task EnsureDynamicsAndSaveAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_settings != null && PressureCurveProfile.EnsureEnabled(_settings))
            await ApplyAndSaveSettingsAsync(_settings);
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
            // Nothing to launch and nothing running → don't spin a 30s timeout; say what's wrong.
            if (!DaemonReachable()) { SetDaemonExeMissing(); return; }
            IsDaemonExeMissing = false;

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
            // Restart relaunches *our* build, so it needs our exe present — check before stopping,
            // so we never kill a running daemon we can't bring back.
            if (_daemonLifecycle.ExpectedExePath() == null) { SetDaemonExeMissing(); return; }
            IsDaemonExeMissing = false;

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
            DaemonVersion = "";
            return;
        }

        var actual = GetConnectedDaemonPath();
        DaemonSourcePath = actual ?? "";
        // Read the version off the connected daemon's own binary (no RPC — the daemon doesn't report
        // it). Best-effort: cross-session/elevated processes may hide their path, leaving it blank. (#296)
        DaemonVersion = actual != null ? ReadExecutableVersion(actual) : "";

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

    /// <summary>Best-effort product/file version off an executable's Win32 version stamp. Returns "" on
    /// any failure (missing file, no version resource). Strips SemVer build metadata (e.g. "+abc123").</summary>
    private static string ReadExecutableVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = (info.ProductVersion ?? info.FileVersion ?? "").Trim();
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
        catch
        {
            return "";
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _loadGate.Dispose();
        _daemon.Dispose();
    }
}
