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
using OtdInterop;

namespace OpenTabletArtist.Services;

public interface IConnectionState : INotifyPropertyChanged
{
    bool IsConnected { get; }
    string ConnectionStatus { get; }
    bool IsDaemonRunning { get; }
    /// <summary>
    /// Whose daemon this is (#742). Anything that writes to, or automatically modifies, its settings
    /// must require <see cref="DaemonOwnership.Owned"/> — <c>!IsForeignDaemon</c> also matches
    /// <see cref="DaemonOwnership.Unknown"/>, which is exactly the case where OTA should do least.
    /// </summary>
    DaemonOwnership Ownership { get; }

    /// <summary>Shorthand for <c>Ownership == Owned</c>. Safe to gate a write on.</summary>
    bool IsAppOwnedDaemon { get; }

    /// <summary>Shorthand for <c>Ownership == External</c>. Note this is <b>not</b> the negation of
    /// <see cref="IsAppOwnedDaemon"/> — an unidentifiable daemon is neither.</summary>
    bool IsForeignDaemon { get; }
    /// <summary>The daemon is somewhere this app manages, but is not the one selected (#882).</summary>
    bool DaemonIsManagedButNotSelected { get; }
    string DaemonSourcePath { get; }
    /// <summary>Version stamped on the connected daemon's executable (read best-effort from its file
    /// via the pipe-server PID; empty when not connected or the path/version couldn't be read). (#296)</summary>
    string DaemonVersion { get; }
    bool HasDaemonVersion { get; }
    bool ShowAppOwnedDaemon { get; }
    bool ShowForeignDaemonWarning { get; }
    /// <summary>This build ships a daemon of its own in <c>&lt;app&gt;/Daemon/</c>. False on macOS, where
    /// nothing is bundled yet — so the app is built against an OpenTabletDriver release without carrying
    /// a copy of it. (docs/design/official-otd-release.md)</summary>
    bool HasBundledDaemon { get; }
    /// <summary>The connected daemon's path is known and worth showing.</summary>
    bool HasDaemonSourcePath { get; }
    bool ShowDaemonSourceUnknown { get; }
    /// <summary>The daemon can see a supported tablet but hasn't detected it (macOS permissions).</summary>
    bool DaemonCannotOpenTablet { get; }
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

    // Live application and explicit persistence share one visible status.
    /// <summary>The save indicator should be shown (Saving / Saved / failed).</summary>
    /// <summary>The last settings save failed to write to disk (change is live but not persisted).</summary>
    bool SaveFailed { get; }
    /// <summary>Text for the save indicator.</summary>
    string SaveStatusText { get; }

    IAsyncRelayCommand StartDaemonCommand { get; }
    IAsyncRelayCommand StopDaemonCommand { get; }
    IAsyncRelayCommand RestartDaemonCommand { get; }
    IRelayCommand LaunchOtdUxCommand { get; }
    /// <summary>True when OpenTabletDriver's own UX can actually be launched — see
    /// <see cref="AppSession.CanLaunchOtdUx"/>. False in a published build, where the UI hides the card.</summary>
    bool CanLaunchOtdUx { get; }

    // --- The rest of what the daemon page's status surface reads (#900) ---------------------------
    //
    // These were reachable only through the concrete AppSession, so DaemonStatusViewModel took the class
    // and everything downstream of it -- the whole daemon page -- was unreachable from a test. Two
    // defects lived there because of it: a switch-to-bundled button whose condition never consulted the
    // chosen daemon location, and its replacement, which rendered in no state at all (#899). Neither was
    // subtle; both were invisible.

    /// <summary>A connect that has not answered within the grace period, so the UI can say so.</summary>
    bool ConnectStalled { get; }

    /// <summary>Whether a start, stop, restart or the initial connect is in flight.</summary>
    bool ShowDaemonActivity { get; }

    /// <summary>What that in-flight operation is, phrased for the topology wire.</summary>
    string DaemonActivityText { get; }

    /// <summary>An unsaved edit thrown away because the connected daemon changed (#787).</summary>
    string DiscardedChangeNotice { get; }

    /// <summary>Whether there is such a notice to show.</summary>
    bool HasDiscardedChangeNotice { get; }

    /// <summary>The driver changed its own settings and the page followed along (#920).</summary>
    string SettingsRefreshedNotice { get; }

    /// <summary>Whether there is such a notice to show.</summary>
    bool HasSettingsRefreshedNotice { get; }

    /// <summary>Re-reads what the daemon holds, for a connection that is already up.</summary>
    Task ReloadAsync();

    /// <summary>Connects to a daemon, for one that is not.</summary>
    Task ConnectAsync();
}

/// <summary>The shared editable settings workspace. Applying never persists.</summary>
public interface ISettingsCoordinator
{
    Settings? CurrentSettings { get; }
    Task<SettingsApplyOutcome> ApplySettingsAsync(Settings settings);
    Task<SettingsRestoreOutcome> RestoreDefaultAsync();
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
    /// <summary>The daemon's tablet-configuration override folder (from AppInfo), or "" if unknown (#480/#467).</summary>
    string ConfigurationDirectory { get; }
    (float Width, float Height)? GetTabletDigitizer(string tabletName);
    /// <summary>Full digitizer spec (mm dimensions + raw maxima) for a tablet, or null if unavailable.
    /// Needed by flows that map between raw tablet units and the desktop (e.g. calibration).</summary>
    Domain.TabletDigitizerSpec? GetDigitizerSpec(string tabletName);
    /// <summary>Raised (UI thread) after each successful data load.</summary>
    event Action? DataLoaded;
}

/// <summary>
/// The daemon connection slice of the shared application session (Option C, #41).
/// Owns the daemon client + its lifecycle, the connection/ownership state, and the
/// connect/start/stop/restart commands. Consumers depend on the narrow
/// <see cref="IConnectionState"/> role and bind its observable properties.
///
/// Thread-affinity rule: this type mutates its observable state only on the UI thread
/// (the daemon's Connected/Disconnected callbacks marshal via the dispatcher), so binders
/// and subscribers never have to marshal.
/// </summary>
public partial class AppSession : ObservableObject, IConnectionState, ISettingsCoordinator, IDeviceData, IDisposable
{
    private readonly IDaemonLifecycleService _daemonLifecycle;
    // No _settingsStore field: the store is handed to the coordinator and nothing else here touches it.
    // AppSession writing settings directly is what #803 found on the load path.
    private readonly CancellationTokenSource _cts = new();
    // Ensures only the most recent data load applies (Connected handler, TabletsChanged event,
    // fallback poll, Refresh). #19.
    private readonly LatestOnlyGate _loadGate = new();

    private string? _daemonPath;
    private SettingsWorkspace? _workspace;
    private readonly OtdSession _session;

    /// <summary>
    /// Fallback reconciliation interval. Detection is event-driven via the daemon's
    /// <c>TabletsChanged</c> push (#170); this poll is only a safety net in case an event is missed,
    /// not the primary detection path — hence much longer than the original 3s magic literal.
    /// </summary>
    private static readonly TimeSpan FallbackPollInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The underlying daemon client. Temporary seam: data-load (settings/tablets/app-info)
    /// still lives in the shell and uses this until it moves into the session (#41 PR 2).
    /// </summary>
    public IDaemonCapabilities Daemon => _session.Capabilities;

    /// <summary>Raised on the UI thread once the daemon connection is established.</summary>
    public event Action? Connected;
    /// <summary>Raised on the UI thread when the daemon connection drops.</summary>
    public event Action? Disconnected;

    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isDaemonRunning;
    /// <summary>
    /// Whose daemon this is (#742). The single source of truth; <see cref="IsAppOwnedDaemon"/> and
    /// <see cref="IsForeignDaemon"/> are views of it, so they can no longer drift into the state where
    /// both are false and callers read that as "ours".
    /// </summary>
    [ObservableProperty] private DaemonOwnership _ownership = DaemonOwnership.Unknown;

    public bool IsAppOwnedDaemon => Ownership == DaemonOwnership.Owned;
    public bool IsForeignDaemon => Ownership == DaemonOwnership.External;

    /// <summary>
    /// The answering daemon sits in a location this app manages, but is not the one selected (#882).
    /// </summary>
    /// <remarks>
    /// Not a third ownership state: it is still <see cref="DaemonOwnership.External"/>, and nothing about
    /// privileges changes. It exists so the health card can say which of the two reasons applies rather
    /// than printing the wrong one.
    /// </remarks>
    [ObservableProperty] private bool _daemonIsManagedButNotSelected;
    [ObservableProperty] private string _daemonSourcePath = "";
    partial void OnDaemonSourcePathChanged(string value) => OnPropertyChanged(nameof(HasDaemonSourcePath));
    [ObservableProperty] private string _daemonVersion = "";
    public bool HasDaemonVersion => !string.IsNullOrEmpty(DaemonVersion);
    partial void OnDaemonVersionChanged(string value) => OnPropertyChanged(nameof(HasDaemonVersion));
    [ObservableProperty] private string _daemonStatusText = "Not connected";
    [ObservableProperty] private bool _isDaemonExeMissing;

    /// <summary>The daemon can see a supported tablet on the bus but has not detected it — on macOS the
    /// signature of a missing Input Monitoring grant, since enumerating a HID device needs no permission
    /// while opening it does. Only ever evaluated when no tablet was detected, so it costs nothing on a
    /// working setup.</summary>
    [ObservableProperty] private bool _daemonCannotOpenTablet;

    /// <summary>Message shown when <see cref="IsDaemonExeMissing"/> — the most common cause of a
    /// dead connection (building only the app, or only running the test suite, never produces the
    /// standalone daemon exe).</summary>
    // static readonly (not const) so it can interpolate the platform-aware daemon exe name (#140);
    // still a single stable string usable in comparisons/assignments below.
    // Two audiences. Where OTA ships or builds its own daemon, a missing one really does mean "you
    // haven't built the solution". On macOS it never means that — OTA drives an OpenTabletDriver the user
    // installs (docs/design/official-otd-release.md), so telling them to run dotnet is advice they cannot
    // act on and that points away from the actual fix.
    public static readonly string DaemonExeMissingMessage =
        OperatingSystem.IsMacOS()
            ? "OpenTabletDriver isn't installed, or it's somewhere OpenTabletArtist didn't look. Install "
              + "it, or point OTA at an existing copy on the Daemon page."
            // Not "build the solution": the daemon left it in #786/#790, so that advice now cannot work.
            // Offer the two routes that do, cheapest first.
            : $"{Domain.DaemonExePaths.DaemonExeName} wasn't found and no daemon is running. Install "
              + "OpenTabletDriver and point OTA at it on the Daemon page, or build the daemon from the "
              + "submodule (scripts/build.ps1, or dotnet build "
              + "external/OpenTabletDriver/OpenTabletDriver.Daemon/OpenTabletDriver.Daemon.csproj).";

    // --- Lifecycle-operation feedback (Start/Stop/Restart) ---
    [ObservableProperty] private bool _isDaemonBusy;
    [ObservableProperty] private string _daemonOperationStatus = "";
    [ObservableProperty] private string _daemonOperationError = "";
    public bool HasDaemonOperationError => !string.IsNullOrEmpty(DaemonOperationError);
    partial void OnDaemonOperationErrorChanged(string value) => OnPropertyChanged(nameof(HasDaemonOperationError));

    [ObservableProperty] private SettingsSaveState _saveState;
    [ObservableProperty] private bool _settingsBusy;
    public bool HasUnsavedChanges => _workspace?.HasUnsavedChanges == true;
    public bool SettingsPaused => _workspace?.IsPaused == true;
    public bool CanEditSettings => !SettingsBusy && _session.CanEditSettings && !SettingsPaused;
    public bool SaveFailed => SaveState is SettingsSaveState.Failed or SettingsSaveState.ApplyFailed
        or SettingsSaveState.Disconnected or SettingsSaveState.ChangedElsewhere or SettingsSaveState.CouldNotCheck;
    public string SaveStatusText => !string.IsNullOrEmpty(_session.SettingsProblem) ? _session.SettingsProblem : SaveState switch
    {
        SettingsSaveState.Applying => "Applying…",
        SettingsSaveState.Unsaved => "Unsaved changes",
        SettingsSaveState.Saving => "Saving…",
        // Saved says nothing, because there is nothing to say: every edit is already live on the driver,
        // and the only question this line answers is whether anything would be lost by a restart. An
        // artist who has just pressed Save watches "Saving…" turn into silence, which is the answer.
        // "Applied — not saved" tried to explain the whole model in four words and mostly raised the
        // question of what "applied" meant; "Unsaved changes" names the one thing at stake.
        SettingsSaveState.Saved => "",
        SettingsSaveState.Failed => "Save failed",
        SettingsSaveState.ApplyFailed => "Couldn't confirm the change — reload and review the driver's settings",
        SettingsSaveState.Disconnected => "Disconnected",
        // Short lines, because this one shares a row with the other save states and is read at a glance.
        // What caused the pause, and what leaving it costs, is SettingsPausedView's to say -- it is on
        // screen for exactly these states and has room for a sentence.
        //
        // Not "somebody else edited them": the driver does this to itself. Attaching a tablet it has not
        // seen before makes it generate a profile and write its own settings back (DetectTablets then
        // SetSettings, and ProfileCollection.GetProfile adds the missing one), which the next observation
        // reads as a difference like any other. Naming an editor would be wrong most of the time and
        // would send the artist looking for an application that is not running (#919).
        SettingsSaveState.ChangedElsewhere => "Reload settings to continue",
        SettingsSaveState.CouldNotCheck => "Reload settings",
        _ => "",
    };
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscardedChangeNotice))]
    private string _discardedChangeNotice = "";
    public bool HasDiscardedChangeNotice => !string.IsNullOrEmpty(DiscardedChangeNotice);
    public event Action? SettingsReplaced;

    /// <summary>
    /// A hold on the settings for something that edits them across time (#922). Dispose to release.
    /// </summary>
    public interface ISettingsEditingScope : IDisposable
    {
        /// <summary>Whether the document this scope opened over is still the one in play.</summary>
        bool StillCurrent { get; }

        /// <summary>Submits a profile, refusing once the document has been replaced underneath.</summary>
        Task<SettingsApplyOutcome> ApplyProfileAsync(OpenTabletDriver.Desktop.Profiles.Profile profile);
    }

    /// <summary>
    /// Whether the host has editor input that has not been submitted yet (#920).
    /// </summary>
    /// <remarks>
    /// Supplied by the shell, because only it can see a half-moved slider or a dialog mid-edit. The
    /// library is not told about any of that, and the decision about whether local work may be replaced
    /// stays on this side of the boundary. Absent, nothing is idle — a host that has not said cannot be
    /// assumed to have nothing to lose.
    /// </remarks>
    public Func<bool>? HasPendingEditorInput { get; set; }

    private int _editingReservations;

    private bool EditingIsIdle() =>
        _editingReservations == 0 && HasPendingEditorInput is { } pending && !pending();

    /// <summary>
    /// Holds the settings open for something that edits them across time, and refuses its writes once
    /// the document has been replaced under it (#922).
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a dialog that captures the whole settings when it opens and submits a profile from them
    /// later — calibration is the one that does this. Two things it needs and a plain apply does not:
    /// nothing may adopt an outside change while it is open, and if something replaces the document
    /// anyway, its submission must be refused rather than quietly putting the captured values back.
    /// </para>
    /// <para>
    /// Refusing loses the dialog's work, which is the lesser harm: it is one dialog's worth, and the
    /// artist is present and can repeat it. Applying it silently reverts settings they may have changed
    /// deliberately in between, with nothing to indicate it happened.
    /// </para>
    /// </remarks>
    public ISettingsEditingScope ReserveEditing()
    {
        Dispatcher.UIThread.VerifyAccess();
        _editingReservations++;
        return new EditingScope(this, _workspace, _workspace?.Generation ?? 0);
    }

    private sealed class EditingScope(AppSession session, SettingsWorkspace? workspace, int generation)
        : ISettingsEditingScope
    {
        private bool _closed;
        private int _generation = generation;

        /// <summary>
        /// Whether this scope may still submit. A released one may not (#925).
        /// </summary>
        /// <remarks>
        /// Disposal gave up the reservation and left the scope able to write, so a dialog that had been
        /// closed and released could still reach the settings through it. Releasing the hold and losing
        /// the ability to use it are the same event.
        /// </remarks>
        public bool StillCurrent =>
            !_closed
            && workspace is not null
            && ReferenceEquals(workspace, session._workspace)
            && workspace.Generation == _generation;

        /// <summary>
        /// Submits, and keeps up with its own write only (#923).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Calibration applies as it goes, so a rule that counted every wholesale write as somebody
        /// else's would break the second preview. It advances past exactly one step, and only when that
        /// step is the one this call caused: a competing write in the same interval leaves the count
        /// further on than that, and the scope is behind from then until it is closed.
        /// </para>
        /// <para>
        /// Claimed at submission, not on the way back (#924). The count moves when a write is submitted,
        /// so catching up only once the driver answered left the scope calling itself stale for as long
        /// as its own write was in flight -- and a dialog that asked during that interval was told
        /// somebody else owned the document, when the somebody else was itself.
        /// </para>
        /// </remarks>
        public Task<SettingsApplyOutcome> ApplyProfileAsync(
            OpenTabletDriver.Desktop.Profiles.Profile profile)
        {
            if (!StillCurrent) return Task.FromResult(SettingsApplyOutcome.ChangedElsewhere);
            var before = workspace!.Generation;
            var applying = session.ApplyProfileAsync(profile);
            if (workspace.Generation == before + 1) _generation = workspace.Generation;
            return applying;
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            session._editingReservations--;
        }
    }

    /// <summary>
    /// Says the page now shows something the driver changed by itself (#920).
    /// </summary>
    /// <remarks>
    /// Quiet on purpose: nothing was lost and nothing is being asked of the artist. It exists so the
    /// values moving under them is explained rather than mysterious — a tablet they just plugged in makes
    /// the driver rewrite its own settings, and the page following along would otherwise look like a
    /// glitch.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingsRefreshedNotice))]
    private string _settingsRefreshedNotice = "";

    public bool HasSettingsRefreshedNotice => !string.IsNullOrEmpty(SettingsRefreshedNotice);
    public Func<Task<bool>>? ResolveUnsavedChanges { get; set; }

    partial void OnSaveStateChanged(SettingsSaveState value) => NotifySettingsState();
    partial void OnSettingsBusyChanged(bool value) => NotifySettingsState();

    private void NotifySettingsState()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SettingsPaused));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(SaveFailed));
        OnPropertyChanged(nameof(SaveStatusText));
    }

    private void UpdateSettingsState()
    {
        var next = !IsConnected ? SettingsSaveState.Disconnected
            : SettingsPaused ? SettingsSaveState.ChangedElsewhere
            : HasUnsavedChanges ? SettingsSaveState.Unsaved : SettingsSaveState.Saved;
        // Background observations must not erase a failure before the user acts on it.
        if (!(SaveState == SettingsSaveState.Failed && next == SettingsSaveState.Unsaved)
            && !(next == SettingsSaveState.ChangedElsewhere
                && SaveState is SettingsSaveState.ApplyFailed or SettingsSaveState.CouldNotCheck))
            SaveState = next;
        NotifySettingsState();
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
    public bool ShowDaemonSourceUnknown => IsConnected && Ownership == DaemonOwnership.Unknown;
    public bool HasBundledDaemon => _daemonLifecycle.HasBundledDaemon();
    public bool HasDaemonSourcePath => !string.IsNullOrEmpty(DaemonSourcePath);
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
    [ObservableProperty] private string _configurationDirectory = "";   // the daemon's tablet-config folder
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
    public Settings? CurrentSettings => _workspace?.Current;
    public event Action? DataLoaded;

    /// <param name="session">
    /// The library session: one connection, and the one settings authority over it. Taken rather than
    /// built here so a test can supply one over a daemon that is not there — and taken whole, because
    /// the pairing is what guarantees that no second authority exists to reorder writes behind this one.
    /// </param>
    /// <param name="daemonLifecycle">Starting, stopping and locating daemon processes. The host's, and
    /// staying the host's: which executable to run and whether to ask the user first are product
    /// decisions.</param>
    public AppSession(OtdSession session, IDaemonLifecycleService daemonLifecycle)
    {
        _session = session;
        _daemonLifecycle = daemonLifecycle;

        _session.Connected += change => Dispatcher.UIThread.Post(() =>
        {
            if (Abandoned || change.ConnectionId != session.ConnectionId) return;
            ConnectionStatus = "Connected";
            IsConnected = true;
            IsDaemonRunning = true;
            IsDaemonExeMissing = false;
            ConnectStalled = false;
            ConnectPhase = "";
            if (DaemonOperationError == DaemonExeMissingMessage) DaemonOperationError = "";
            ApplyDaemonIdentity(change);
            if (_workspace is not null && HasUnsavedChanges)
                DiscardedChangeNotice = "Reconnected to the driver. Its current settings replaced the previous unsaved workspace.";
            _workspace = session.Settings is { } settings ? new SettingsWorkspace(settings, IsAppOwnedDaemon) : null;
            if (_workspace is null) Profiles = [];
            SettingsReplaced?.Invoke();
            SaveState = SettingsSaveState.None;
            UpdateSettingsState();
            Connected?.Invoke();
            _ = LoadDataAsync();
        });
        _session.Disconnected += () => Dispatcher.UIThread.Post(() =>
        {
            if (Abandoned || session.ConnectionId != 0) return;
            if (HasUnsavedChanges)
                DiscardedChangeNotice = "The driver disconnected. Reconnecting reloads its current settings; unsaved changes may be lost.";
            _workspace = null;
            SettingsReplaced?.Invoke();
            ConnectionStatus = "Disconnected";
            IsConnected = false;
            IsDaemonRunning = false;
            Ownership = DaemonOwnership.Unknown;
            DaemonIsManagedButNotSelected = false;
            DaemonSourcePath = "";
            DaemonVersion = "";
            HasTablet = false;
            TabletName = "";
            DetectedTablets = [];
            ActiveTabletName = null;
            _pluginEnsured = false;
            UpdateSettingsState();
            Disconnected?.Invoke();
        });

        // Event-driven detection (#170): the daemon pushes TabletsChanged on plug/unplug (and on
        // sleep/wake), so reload immediately for near-instant detection and an accurate "last seen",
        // rather than waiting up to a full FallbackPollInterval. Marshalled to the UI thread (the
        // event fires off the RPC thread); the load gate coalesces a burst of events into one load.
        _session.Capabilities.TabletsChanged += () => Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (IsConnected) _ = LoadDataAsync();
        });
    }

    /// <summary>Does the daemon's own HID enumeration contain a tablet it has a configuration for?
    /// Best-effort: any failure reads as "no", because claiming a permissions problem on a failed probe
    /// would be worse than staying quiet.</summary>
    private async Task<bool> DaemonSeesUnopenedTabletAsync()
    {
        try
        {
            var devices = await _session.Capabilities.GetDevicesAsync();
            foreach (var device in devices)
            {
                var vendor = device["VendorID"]?.Value<int>();
                var product = device["ProductID"]?.Value<int>();
                if (vendor is { } v && product is { } p && SupportedTabletsCatalog.IsSupportedDevice(v, p))
                    return true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("Couldn't check the daemon's device list for an unopened tablet.", ex);
        }
        return false;
    }

    private void NotifyOwnership()
    {
        OnPropertyChanged(nameof(IsAppOwnedDaemon));
        OnPropertyChanged(nameof(IsForeignDaemon));
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

    partial void OnOwnershipChanged(DaemonOwnership value) => NotifyOwnership();

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
            if (_daemonLifecycle.Launch(_daemonPath) is { } launchProblem)
            {
                DaemonOperationError = launchProblem;
                ConnectionStatus = "Disconnected";
                return;
            }
            ConnectPhase = "Waiting for the daemon to respond…";
        }
        else
        {
            ConnectPhase = "Connecting to the daemon…";
        }

        ConnectionStatus = "Connecting...";
        await _session.ConnectAsync(_cts.Token);
        _ = MonitorConnectAttemptAsync(++_connectAttempt);
    }

    /// <summary>Is there a daemon to talk to — our exe present to launch, or one already running
    /// (including a separately-installed instance)? Gates every connect path so a missing build
    /// surfaces a clear message rather than a silent timeout.</summary>
    private bool DaemonReachable() =>
        (_daemonPath ?? _daemonLifecycle.ExpectedExePath()) != null || _daemonLifecycle.IsRunning();

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
        var connect = _session.ConnectAsync(_cts.Token);
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
    /// <summary>
    /// Set the moment an exit begins, before anything is waited for.
    /// </summary>
    /// <remarks>
    /// Cancelling the token stops the loops <em>starting</em> and stops nothing else. A refresh reaches
    /// the daemon through <see cref="LoadDataCoreAsync"/>, whose reads go to the capabilities rather than
    /// through the settings session, so the library's admission control never sees them: the reload that
    /// follows an apply would begin during the very close that is settling that apply.
    /// </remarks>
    private volatile bool _closing;

    /// <summary>
    /// The one funnel every refresh goes through — the connect handler, the poll, window activation,
    /// the public reload, and the reload after an apply — so refusing here refuses all of them.
    /// </summary>
    private Task LoadDataAsync() =>
        _closing ? Task.CompletedTask : _loadGate.RunAsync(LoadDataCoreAsync);

    /// <summary>
    /// Whether a load that is already running should stop where it is.
    /// </summary>
    /// <remarks>
    /// Refusing new loads is not enough on its own: one already past the gate when the exit began would
    /// go on reading the daemon and publishing into view models that are about to be disposed. Checked
    /// after every await in the load, because every one of them is a point where an exit can arrive.
    /// </remarks>
    private bool Abandoned => _closing || _disposed;

    private async Task LoadDataCoreAsync()
    {
        var connectionId = _session.ConnectionId;
        bool Obsolete() => Abandoned || connectionId != _session.ConnectionId;
        // Mutates observable state, so it must run on the UI thread. Every caller (the connect
        // handler, the poll, apply, reload) marshals via the dispatcher; verify so a future
        // off-thread caller fails loudly instead of corrupting bindings. (Codex note, #42.)
        Dispatcher.UIThread.VerifyAccess();
        try
        {
            // Tablets (JToken — complex runtime type)
            var tablets = await _session.Capabilities.GetTabletsAsync();
            if (Obsolete()) return;

            Tablets = tablets;

            var detectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var detected = new List<DetectedTablet>(tablets.Count);
            // Collected here and written ONCE below, after the essential daemon reads (#735). Writing
            // per tablet inside this loop rewrote the whole preference file per tablet per reload, and a
            // failed write threw into the outer catch — so an unwritable timestamp could stop working
            // device and settings state from ever reaching the UI.
            var lastSeenUpdates = new Dictionary<string, string>();
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
                    lastSeenUpdates[LastSeenKey(name)] = DateTime.Now.ToString("o");
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

            // "No tablets" has two very different causes: nothing is plugged in, or something is and the
            // daemon can't open it. Ask the daemon what it can SEE only in that case — the answer is only
            // interesting when the detected list is empty.
            var unopened = detected.Count == 0 && await DaemonSeesUnopenedTabletAsync();
            if (Obsolete()) return;

            DaemonCannotOpenTablet = unopened;

            // Observe outside changes. Adopted here only when nothing local is at stake; anything the
            // artist would lose makes this a pause for them to answer (#920).
            if (_workspace is { } workspace)
            {
                var refreshed = await workspace.RefreshAsync(EditingIsIdle);
                if (ReferenceEquals(workspace, _workspace) && !Abandoned)
                {
                    if (refreshed.ChangedTheBaseline)
                    {
                        SettingsRefreshedNotice =
                            "Driver settings refreshed. The driver changed them and you had nothing unsaved "
                            + "in progress, so this page now shows what it holds.";
                        SettingsReplaced?.Invoke();
                        PublishSettings();
                        SaveState = SettingsSaveState.None;
                    }
                    if (!workspace.HasPendingApply) UpdateSettingsState();
                }
            }
            if (Obsolete()) return;
            var settings = CurrentSettings;
            if (settings != null)
            {
                Profiles = settings.Profiles
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
            var appInfo = await _session.Capabilities.GetAppInfoAsync();
            if (Obsolete()) return;
            if (appInfo != null)
            {
                PresetDirectory = appInfo.PresetDirectory ?? "";
                PluginDirectory = appInfo.PluginDirectory ?? "";
                ConfigurationDirectory = appInfo.ConfigurationDirectory ?? "";   // authoritative override folder (#480/#467)
            }

            // Best-effort history, deliberately AFTER every essential read (#735). SetMany never throws
            // and never aborts the load: a tablet the user can see and configure matters more than
            // remembering when we last saw it. AppSettings logs the failure and flags LastWriteFailed.
            AppSettings.SetMany(lastSeenUpdates);

            if (Obsolete()) return;
            DataLoaded?.Invoke();

            // Make sure our pressure-curve plugin is installed in the app-owned daemon (once per
            // connection). Fire-and-forget so it can't stall the load.
            _ = EnsurePressurePluginAsync();
        }
        catch (Exception ex)
        {
            // Data load failed — retried on the next connection/poll, so it's not fatal, but log it (#21)
            // rather than swallow: a persistent failure here is why the UI can look stale.
            AppLog.Warn("Session data load failed; will retry on the next poll.", ex);
        }
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
        var connectionId = _session.ConnectionId;
        if (_pluginEnsured || !IsAppOwnedDaemon || string.IsNullOrEmpty(PluginDirectory)) return;
        _pluginEnsured = true;
        var dir = PluginDirectory;
        var outcome = await Task.Run(() => _pluginInstaller.EnsureInstalled(dir));

        // Narrowly: stops the installer's RESULT being applied to a daemon this application is leaving.
        // Applying reaches the daemon, and this is fire-and-forget, so nothing would observe it failing
        // against a connection that has gone.
        //
        // It does not cancel an installation already running -- that finishes on its own -- and it does
        // not make this task's faults observed. Neither is claimed.
        //
        // No regression covers it: reaching here needs an app-owned daemon and a real plugin installer,
        // neither of which the test harness has. Said here rather than left to look covered.
        if (Abandoned || connectionId != _session.ConnectionId) return;

        await PluginInstallApplier.ApplyAsync(this, outcome);
    }

    /// <summary>Low-frequency fallback reconciliation. Detection itself is event-driven (#170); this
    /// only catches a missed <c>TabletsChanged</c> push so state can't drift indefinitely.</summary>
    private async Task PollDataAsync()
    {
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(FallbackPollInterval, token).ConfigureAwait(false);
                if (IsConnected)
                {
                    try { await Dispatcher.UIThread.InvokeAsync(LoadDataAsync); }
                    catch (Exception ex) { AppLog.Debug($"Fallback poll reload skipped: {ex.Message}"); }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public Task<SettingsApplyOutcome> ApplySettingsAsync(Settings settings) =>
        ApplyThroughAsync(workspace => workspace.ApplyAsync(settings));

    public Task<SettingsApplyOutcome> ApplyProfileAsync(OpenTabletDriver.Desktop.Profiles.Profile profile) =>
        ApplyThroughAsync(workspace => workspace.ApplyProfileAsync(profile));

    private async Task<SettingsApplyOutcome> ApplyThroughAsync(
        Func<SettingsWorkspace, Task<SettingsApplyOutcome>> apply)
    {
        Dispatcher.UIThread.VerifyAccess();
        var workspace = _workspace;
        if (workspace is null || !_session.CanEditSettings) return SettingsApplyOutcome.Disconnected;
        SaveState = SettingsSaveState.Applying;
        var outcome = await apply(workspace);
        if (outcome.Error is not null) AppLog.Warn("Settings apply could not be confirmed.", outcome.Error);
        if (!ReferenceEquals(workspace, _workspace) || Abandoned) return SettingsApplyOutcome.Disconnected;
        if (!workspace.HasPendingApply)
        {
            UpdateSettingsState();
            if (!outcome.IsLive)
                SaveState = outcome.Status == SettingsApplyStatus.CouldNotCheck ? SettingsSaveState.CouldNotCheck
                    : outcome.Status == SettingsApplyStatus.ChangedElsewhere ? SettingsSaveState.ChangedElsewhere
                    : SettingsSaveState.ApplyFailed;
            PublishSettings();
        }
        return outcome;
    }

    private void PublishSettings()
    {
        if (CurrentSettings is { } settings)
        {
            Profiles = settings.Profiles.Select(p => new ProfileItem(p,
                DetectedTablets.Any(t => t.Name == p.Tablet), null)).ToList();
            OutputMode = settings.Profiles.FirstOrDefault()?.OutputMode?.Path?.Split('.').LastOrDefault() ?? "Unknown";
            HasWindowsInk = OutputMode.Contains("WinInk", StringComparison.OrdinalIgnoreCase);
        }
        DataLoaded?.Invoke();
    }

    public async Task<bool> SaveSettingsAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        var workspace = _workspace;
        if (workspace is null) return false;
        SaveState = SettingsSaveState.Saving;
        var outcome = await workspace.SaveAsync();
        if (outcome.Error is not null) AppLog.Warn("Settings save failed.", outcome.Error);
        if (!ReferenceEquals(workspace, _workspace) || Abandoned) return false;
        UpdateSettingsState();
        if (!outcome.IsSaved) SaveState = outcome.Status == SettingsSaveStatus.Paused
            ? SettingsSaveState.ChangedElsewhere : SettingsSaveState.Failed;
        if (outcome.IsSaved) PublishSettings();
        return outcome.IsSaved;
    }

    public async Task ReloadSettingsAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_workspace is not { } workspace)
        {
            await _session.InitializeAsync();
            return;
        }
        var outcome = await workspace.ReloadAsync();
        if (!ReferenceEquals(workspace, _workspace) || Abandoned) return;
        if (outcome.ChangedTheBaseline)
        {
            DiscardedChangeNotice = "";
            SettingsReplaced?.Invoke();
            PublishSettings();
            SaveState = SettingsSaveState.None;
            UpdateSettingsState();
        }
        else SaveState = SettingsSaveState.CouldNotCheck;
    }

    public async Task<SettingsRestoreOutcome> RestoreDefaultAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        var workspace = _workspace;
        if (workspace is null) return SettingsRestoreOutcome.Disconnected;
        if (SettingsBusy) return SettingsRestoreOutcome.Failed(null);
        SettingsBusy = true;
        SettingsRestoreOutcome outcome;
        try { outcome = await workspace.RestoreAsync(); }
        finally { SettingsBusy = false; }
        if (!ReferenceEquals(workspace, _workspace) || Abandoned) return SettingsRestoreOutcome.Disconnected;
        if (outcome.IsRestored)
        {
            SettingsReplaced?.Invoke();
            PublishSettings();
            SaveState = SettingsSaveState.None;
        }
        UpdateSettingsState();
        return outcome;
    }

    /// <summary>Force the Pen Dynamics filter present + enabled across all profiles and apply — the Home
    /// health-check "Fix" backing the always-on invariant (#dynamics-always-on). No-op if nothing changed.</summary>
    public async Task EnsureDynamicsAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (CurrentSettings is { } s && PressureCurveProfile.EnsureEnabled(s))
            await ApplySettingsAsync(s);
    }

    /// <summary>The one-click "Restore recommended pen settings" fix for the artist-pen-behavior health
    /// bundle (#artist-pen-health): re-enable Windows Ink + pen tip + pressure + tilt on the tablet's
    /// profile in a single apply. No-op if the tablet isn't found or nothing needed changing.</summary>
    public async Task RestoreRecommendedPenBehaviorAsync(string? tabletName)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (CurrentSettings is not { } settings || string.IsNullOrEmpty(tabletName)) return;
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) return;

        var changed = Domain.PenBehaviorRestore.ToRecommended(profile, OperatingSystem.IsWindows());

        // The Windows-Ink-off health offender reads the opt-out flag (WinInkAutoOptOut), not just the
        // output-mode path — so switching the mode back to Windows Ink above isn't enough; clear the flag
        // too, or the offender (and the auto-setup's "leave this tablet off Windows Ink" behaviour) persists.
        if (OperatingSystem.IsWindows() && WinInkAutoOptOut.IsOptedOut(tabletName))
        {
            WinInkAutoOptOut.Clear(tabletName);
            changed = true;
        }

        if (changed) await ApplySettingsAsync(settings);
    }

    /// <summary>The Home health-check "Fix" for an off-screen mapping (#629): re-map the tablet's active
    /// area cleanly to the primary display — a whole-monitor, undistorted 1:1 fit — and apply, pulling the
    /// pen back onto real screen space. No-op if the tablet/profile or the display set is unavailable.</summary>
    public async Task MapTabletToPrimaryDisplayAsync(string? tabletName)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (CurrentSettings is not { } settings || string.IsNullOrEmpty(tabletName)) return;
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) return;

        var displays = DisplayEnumerator.Enumerate();
        if (displays is not { Count: > 0 }) return;
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];

        if (!Domain.DisplayMappingApplier.ApplyToProfile(profile, GetTabletDigitizer(tabletName), primary, displays))
            return;

        await ApplySettingsAsync(settings);
    }

    /// <summary>The Home health-check "Fix" for a non-cardinal active-area rotation (#629): snap the tablet's
    /// rotation to the nearest standard angle (0/90/180/270) and re-fit the area to its mapped display, then
    /// apply — un-skewing the pen axes. No-op if the tablet/profile is unavailable.</summary>
    public async Task ResetTabletRotationToCardinalAsync(string? tabletName)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (CurrentSettings is not { } settings || string.IsNullOrEmpty(tabletName)) return;
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile?.AbsoluteModeSettings?.Tablet == null) return;

        double current = profile.AbsoluteModeSettings.Tablet.Rotation;
        int cardinal = ((int)System.Math.Round(current / 90.0) * 90 % 360 + 360) % 360;

        var displays = DisplayEnumerator.Enumerate();
        if (!Domain.DisplayMappingApplier.ApplyRotation(profile, GetTabletDigitizer(tabletName), cardinal, displays))
            return;

        await ApplySettingsAsync(settings);
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
            _session.AutoReconnect = true;
            if (_daemonLifecycle.Launch(_daemonPath) is { } launchProblem)
            {
                // It died on the spot. Waiting out the 30s connect timeout would replace a precise
                // explanation with a generic one.
                DaemonOperationError = launchProblem;
                ConnectionStatus = "Disconnected";
                return;
            }
            OnPropertyChanged(nameof(CanStartDaemon));

            DaemonOperationStatus = "Connecting…";
            ConnectionStatus = "Connecting...";
            _connectAttempt++; // invalidate any pending startup/Refresh monitor
            await _session.ConnectAsync(_cts.Token);

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

    // Stop the running daemon in the cleanest way for how it's managed (#601): a systemd user service is
    // stopped via `systemctl --user stop` (killing it by name bypasses systemd — it looks like a crash in
    // journald, and a Restart= unit revives it); otherwise the process is killed by name. Off Linux,
    // IsActive() is always false, so this is exactly the previous kill-by-name behaviour.
    /// <summary>
    /// Stop the daemon, by the cleanest route available for how it is running (#601).
    /// <para>
    /// systemd-managed: <c>systemctl --user stop</c>, so the unit is actually stopped rather than having
    /// its process killed out from under it — a Kill there reads as a crash in journald, and a unit with
    /// <c>Restart=</c> would simply bring it back.
    /// </para>
    /// <para>
    /// Otherwise: kill the ONE process we are connected to, found by the pipe→pid lookup. This used to
    /// kill every process named OpenTabletDriver.Daemon, which stops a second OTD instance the user
    /// started themselves — a foreign daemon this app never owned. StopAll remains the fallback for when
    /// the pid isn't known: off-Windows the lookup is unavailable, and the daemon is effectively a
    /// singleton there, so by-name is both the only option and a safe one.
    /// </para>
    /// </summary>
    /// <summary>
    /// Decides now what stopping the daemon would mean, and hands back something that does it later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For an exit, which has to stop the daemon <em>after</em> closing rather than before: killing it
    /// first takes the connection away from the writes the close is trying to settle, so an apply in
    /// flight when the user chooses "quit and stop" loses its daemon and the edit with it.
    /// </para>
    /// <para>
    /// Resolving cannot simply be deferred along with the stop, which is the trap here.
    /// <see cref="StopDaemonProcessAsync"/> asks the live session which process is answering, and after a
    /// close there is no session to ask -- so it would find nothing and fall through to stopping every
    /// daemon on the machine, including one this application never spoke to. The target is therefore
    /// captured while still connected and the returned action stops <em>that</em>, or nothing.
    /// </para>
    /// <para>
    /// Returns null when the user declines. The confirmation happens here too, while there is still a
    /// window to ask in.
    /// </para>
    /// </remarks>
    public async Task<Func<Task>?> PrepareDaemonStopAsync()
    {
        if (!await ConfirmedDaemonActionAsync("stop")) return null;

        // Suppressed before the close, not after: reconnecting to a daemon we are about to stop would
        // race the stop and could leave the session holding a connection nobody meant to keep.
        _session.AutoReconnect = false;

        if (OtdSystemdService.IsActive()) return () => OtdSystemdService.StopAsync();

        if (_session.ConnectedProcessId() is { } pid)
            return () => { _daemonLifecycle.Stop(pid); return Task.CompletedTask; };

        // Unidentifiable while connected is the one case where stopping everything is still the answer,
        // and it is the same answer the interactive path gives. What has changed is that it is decided
        // here, with the connection open, rather than inferred from a session that has gone.
        return () => { _daemonLifecycle.StopAll(); return Task.CompletedTask; };
    }

    private async Task StopDaemonProcessAsync()
    {
        if (OtdSystemdService.IsActive())
        {
            await OtdSystemdService.StopAsync();
            return;
        }

        if (_session.ConnectedProcessId() is { } pid)
            _daemonLifecycle.Stop(pid);
        else
            _daemonLifecycle.StopAll();
    }

    /// <summary>
    /// Asked before stopping a daemon this app didn't start (#613, option 2). Return true to go ahead.
    /// Both the Stop and Restart paths consult it, and the tray's "Quit and stop the daemon" inherits it
    /// by routing through <see cref="StopDaemonCommand"/>.
    /// <para>
    /// A settable hook rather than a constructor dependency because <c>DialogService</c> is built FROM
    /// the session — injecting it back would be circular. Left null (tests, and the window between
    /// construction and the shell wiring it up) means no prompt: the stop itself is already correct
    /// without a UI attached, and a service that cannot ask must not therefore refuse.
    /// </para>
    /// </summary>
    public Func<string, Task<bool>>? ConfirmForeignDaemonAction { get; set; }

    /// <summary>True when the user has agreed — or there is nothing to agree to, because we own the
    /// daemon or nothing is there to ask with.</summary>
    /// <summary>
    /// Ask before stopping or restarting anything that isn't positively OTA's own build.
    ///
    /// The gate is "do we know it is ours", not "do we know it is theirs" — those differ in the case that
    /// matters. When OTA can't read which binary answered (a daemon running as another user, or elevated),
    /// the daemon is neither owned nor foreign, and gating on the foreign flag skipped the confirmation
    /// precisely when OTA knew least about what it was about to kill.
    /// </summary>
    private async Task<bool> ConfirmedDaemonActionAsync(string verb)
    {
        if (IsAppOwnedDaemon || ConfirmForeignDaemonAction is not { } confirm) return true;
        return await confirm(verb);
    }

    [RelayCommand]
    private async Task StopDaemon()
    {
        if (IsDaemonBusy) return;
        if (ResolveUnsavedChanges is { } resolve && !await resolve()) return;
        if (!await ConfirmedDaemonActionAsync("stop")) return;
        IsDaemonBusy = true;
        DaemonOperationError = "";
        try
        {
            DaemonOperationStatus = "Stopping daemon…";
            // User-initiated stop: suppress auto-reconnect so the client doesn't immediately spin
            // trying to reconnect to the daemon we're about to kill (which races a later Start).
            _session.AutoReconnect = false;
            await StopDaemonProcessAsync();

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
        if (_daemonPath is not null && _session.ConnectedExecutablePath is { } actual
            && !PathEquality.Same(_daemonPath, actual))
        {
            DaemonOperationError = "A different driver is running. Restart OpenTabletArtist to use it.";
            return;
        }
        if (ResolveUnsavedChanges is { } resolve && !await resolve()) return;
        // Restart the executable pinned when this app first connected.
        if (!await ConfirmedDaemonActionAsync("restart")) return;
        IsDaemonBusy = true;
        DaemonOperationError = "";
        try
        {
            // Confirm a relaunch target before stopping,
            // so we never kill a running daemon we can't bring back.
            if ((_daemonPath ?? _daemonLifecycle.ExpectedExePath()) == null) { SetDaemonExeMissing(); return; }
            IsDaemonExeMissing = false;

            // Stop phase: suppress auto-reconnect while the old process dies, wait for the drop.
            DaemonOperationStatus = "Stopping daemon…";
            _session.AutoReconnect = false;
            await StopDaemonProcessAsync();
            await WaitForConnectionStateAsync(connected: false, DaemonOperationTimeout);

            // Start phase: relaunch and connect to the fresh instance.
            DaemonOperationStatus = "Starting daemon…";
            _session.AutoReconnect = true;
            if (_daemonLifecycle.Launch(_daemonPath) is { } launchProblem)
            {
                // Worth being loud here: the old daemon is already stopped, so a silent failure leaves
                // the user with no driver at all and no idea why.
                DaemonOperationError = launchProblem;
                ConnectionStatus = "Disconnected";
                return;
            }

            DaemonOperationStatus = "Connecting…";
            ConnectionStatus = "Connecting...";
            _connectAttempt++; // invalidate any pending startup/Refresh monitor
            await _session.ConnectAsync(_cts.Token);

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
        var token = _cts.Token;
        var sw = Stopwatch.StartNew();
        while (IsConnected != connected)
        {
            if (sw.Elapsed >= timeout) return false;
            try { await Task.Delay(100, token); }
            catch (OperationCanceledException) { return IsConnected == connected; }
        }
        return true;
    }

    /// <summary>
    /// The OTD WPF UX project in the submodule (resolution in <see cref="OtdUxPaths"/>, so it's unit-
    /// testable). Only present in a development tree: a published build ships the daemon but not the UX
    /// sources, and launching it needs the .NET SDK's <c>dotnet run</c>.
    /// </summary>
    private static string OtdUxProjectPath => OtdUxPaths.ProjectPath(AppContext.BaseDirectory);

    /// <summary>True when <see cref="OtdUxProjectPath"/> exists, i.e. we're running from a development
    /// tree. The Daemon page hides the OTD UX card when this is false, rather than offering a button
    /// that silently does nothing for everyone on a released build.</summary>
    public bool CanLaunchOtdUx => Directory.Exists(OtdUxProjectPath);

    [RelayCommand(CanExecute = nameof(CanLaunchOtdUx))]
    private void LaunchOtdUx()
    {
        // Launch the OTD WPF UX from the submodule via dotnet run
        var otdUxProject = OtdUxProjectPath;
        if (!Directory.Exists(otdUxProject))
        {
            AppLog.Warn($"Can't launch the OTD UX: no project at {otdUxProject} " +
                        "(expected — a published build doesn't ship the OTD UX sources).");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("dotnet", $"run --project \"{otdUxProject}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            // Needs the .NET SDK on PATH; report rather than swallowing.
            AppLog.Warn("Couldn't start the OTD UX via 'dotnet run'.", ex);
        }
    }

    // Determine whether the daemon we're connected to is this project's build.
    // Conservative: only flags "foreign" when we can positively read the server path.
    /// <summary>
    /// Shows a daemon the library has already identified and invalidated for (#828).
    ///
    /// The connect path no longer asks: the library subscribes to its own connection, does the
    /// identification on this application's execution context, and hands the answer over. What is left
    /// here is what was always ours -- what to display, and whether this daemon is one we may act on
    /// without asking the user first.
    /// </summary>
    /// <summary>
    /// Shows a daemon, and says so when identifying it cost the user an edit.
    ///
    /// Everything here is presentation and product judgement: what to display, and whether this daemon is
    /// one we may stop or restart without asking. The question of <em>which</em> daemon, and what to drop
    /// because it is a different one, was answered before we got here.
    /// </summary>
    private void ApplyDaemonIdentity(DaemonChange change)
    {
        var actual = change.ExecutablePath;
        _daemonPath ??= actual;

        DaemonSourcePath = actual ?? "";
        // Read the version off the connected daemon's own binary (no RPC — the daemon doesn't report it).
        // Falls back to the sibling managed assembly for a native apphost (macOS #140). Best-effort:
        // cross-session/elevated processes may hide their path, leaving it blank. (#296)
        DaemonVersion = actual != null ? Domain.DaemonVersion.Read(actual) : "";

        if (actual == null)
        {
            // Connected, but we can't read which binary answered — elevated, another user's, or more
            // than one candidate. Unknown is a real answer, not a soft "no" (#742).
            Ownership = DaemonOwnership.Unknown;
            // Nothing is known about which daemon this is, so nothing may be said about whether it is
            // the selected one (#882).
            DaemonIsManagedButNotSelected = false;
            return;
        }

        // "Ours" means OTA put it there — in its own folder — not that OTA compiled it. Since #794 the
        // bundled daemon is OpenTabletDriver's own released binary, so nothing here is built by us and
        // "our build" would name an empty set. An adopted OTD install is still the user's, so it stays
        // "foreign": that is what keeps Stop/Restart behind a confirmation (ConfirmForeignDaemonAction)
        // even though it is the daemon we start. Adoption being *supported* is expressed by the health
        // catalog treating it as Information, not by pretending it is ours.
        // See docs/design/official-otd-release.md.
        var resolved = ExecutablePath.SameFile(actual, _daemonLifecycle.ExpectedExePath());
        var managed = _daemonLifecycle.IsAppManaged(actual);
        var owned = resolved && managed;

        // Managed by location, but not the one the user picked: ExpectedExePath prefers a user-chosen
        // daemon over the bundled candidate, so the bundled copy answering while a selection points
        // elsewhere lands here. External is the right classification (#880), but "an OpenTabletDriver
        // you installed, not the bundled copy" is then a false sentence about the bundled copy (#882).
        //
        // Set BEFORE Ownership, so a listener woken by the ownership change already sees the matching
        // value. Its own notification is observed too (HealthService), for the case where ownership
        // does not move at all: External to External, a different daemon answering.
        DaemonIsManagedButNotSelected = !owned && managed;
        Ownership = owned ? DaemonOwnership.Owned : DaemonOwnership.External;

    }

    /// <summary>Cancel background work and close the driver connection. The caller resolves unsaved edits first.</summary>
    public async Task<bool> CloseAsync(TimeSpan settleWithin)
    {
        _closing = true;
        if (!_disposed) _cts.Cancel();
        return await _session.CloseAsync(settleWithin);
    }

    private bool _disposed;

    public void Dispose()
    {
        // Idempotent, because closing and disposing are now two calls a host makes in sequence and an
        // exit can reach this twice. Cancelling a disposed token source throws.
        if (_disposed) return;

        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _loadGate.Dispose();

        // Stop the connection activity timer before disposing its session.
        _connectTicker?.Stop();
        _connectTicker = null;

        // The session owns the connection, so it is what gets disposed -- disposing the connection
        // directly would leave the session holding something already gone.
        _session.Dispose();
    }
}
