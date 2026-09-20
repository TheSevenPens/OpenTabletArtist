using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Plugin.Logging;
using StreamJsonRpc;

namespace OtdInterop;

/// <summary>
/// The daemon connection itself: one named pipe, one JSON-RPC channel, and the reconnect loop that
/// keeps them up.
/// </summary>
///
/// <remarks>
/// <para>
/// Internal, and that is the point of it being here (#807 Phase 4). This type can write settings
/// straight to the daemon, with none of the ordering, ownership or session checks that
/// <see cref="IOtdSettingsSession"/> exists to apply. While it was a public class in the app, every
/// caller that could reach a connection could also reach that write, and the protection was a
/// convention. Hosts get it from <see cref="OtdSession"/>, as <see cref="IDaemonTransport"/> — which
/// does not carry those two verbs. They are on <see cref="IDaemonSettingsChannel"/>, which is internal,
/// so the settings session can reach them and the host cannot.
/// </para>
/// <para>
/// Not thread-safe beyond what is marked. The reconnect loop and the debug reference count have their
/// own coordination; everything else assumes the host's single execution context, the same requirement
/// <see cref="IOtdSettingsSession"/> documents.
/// </para>
/// </remarks>
internal sealed class DaemonClient : IDaemonTransport, IDaemonSettingsChannel
{
    private const string DefaultPipeName = "OpenTabletDriver.Daemon";

    /// <summary>
    /// The pipe this client connects to. The daemon's, except for a test that stands up its own.
    /// </summary>
    /// <remarks>
    /// Injectable so channel identity can be tested against this client rather than against a fake that
    /// asserts the guarantee it is supposed to be checking. The bug this exists for was invisible to the
    /// fake precisely because the fake kept its own monotonic counter.
    /// </remarks>
    private readonly string _pipeName;

    /// <summary>Where connect failures and best-effort probes are recorded. Never null.</summary>
    private readonly IOtdLog _log;

    /// <summary>
    /// The current channel and its number, as <b>one</b> value.
    ///
    /// Two fields would not do, and the reason is not theoretical. A reader taking them separately can
    /// catch a reconnect between the two reads and come away with the old channel carrying the new
    /// number — which sends correctly and then decides, wrongly, that it is still current, so an obsolete
    /// result gets published. Or the reverse: the new channel with the old number, refused although it
    /// was fine. One reference read has neither failure.
    /// </summary>
    private volatile Channel? _channel;

    /// <inheritdoc />
    int IDaemonSettingsChannel.Incarnation => _channel?.Incarnation ?? 0;

    /// <summary>One JSON-RPC channel and the number identifying it, established and replaced together.</summary>
    private sealed record Channel(JsonRpc Rpc, int Incarnation);

    /// <summary>
    /// How many channels this client has ever opened. Allocation only; never read as the current identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="_channel"/> because that is nullable and a disconnect clears it. Deriving
    /// the next number from it therefore restarted the count: a drop and reconnect went 1, 0, 1, so two
    /// different channels carried the same identity and every "is this still the channel I had?" check
    /// silently answered yes. Verified against the real client over two local pipes.
    /// </para>
    /// <para>
    /// This does not reintroduce the two-field read the record exists to prevent. That hazard is about
    /// <em>publication</em> -- a reader seeing one channel's RPC beside another's number -- and
    /// publication is still the single assignment below. Nothing reads this field to learn which channel
    /// is current; it only hands a fresh number to the value being published.
    /// </para>
    /// </remarks>
    private int _channelsOpened;


    /// <param name="log">The host's log. Connect failures are throttled and reported here.</param>
    /// <param name="pipeName">The pipe to connect to; the daemon's unless a test supplies its own.</param>
    internal DaemonClient(IOtdLog log, string? pipeName = null)
    {
        _log = log;
        _pipeName = pipeName ?? DefaultPipeName;
    }

    private JsonRpc? _rpc;
    private NamedPipeClientStream? _pipe;

    // The daemon's tablet-debug stream is a single global flag, but several consumers want it (the
    // Test view, the Diagnostics page, the Dynamics tab's live-pressure dot). Reference-count it so
    // one consumer turning it off doesn't starve another. (#102 follow-up)
    private readonly object _debugLock = new();
    // The 0↔1 transition decision lives in a pure, tested helper (#121); this lock guards its use.
    private readonly DebugRefCounter _debugRefs = new();
    // Single-flight reconnect coordinator: only one connect loop runs at a time, and a
    // reconnect requested while one is running (e.g. an immediate disconnect during connect)
    // is honored once the current loop exits — closing the dropped-reconnect race (#33).
    private readonly CoalescingSingleFlight _connectFlight = new();

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<JObject>? DeviceReport;
    /// <summary>
    /// The daemon pushed a tablet add/remove (its <c>TabletsChanged</c> event — fired on plug/unplug
    /// and on sleep/wake). Parameterless: subscribers just re-pull state; the payload (the new tablet
    /// list) is intentionally not surfaced so consumers stay decoupled from its shape (#170).
    /// </summary>
    public event Action? TabletsChanged;
    /// <summary>The daemon forwarded a log message (its <c>Message</c> event). Fires off the RPC
    /// thread — subscribers marshal to the UI thread. (#console)</summary>
    public event Action<LogMessage>? LogReceived;
    /// <summary>Private: the host owns connection state as the thing it shows the user. This is the
    /// client's own view, used only to short-circuit a redundant connect.</summary>
    private bool IsConnected => _rpc != null && !_rpc.IsDisposed;

    /// <summary>
    /// When true (the default), an unexpected transport drop schedules an automatic reconnect.
    /// Callers set this to false around a <em>user-initiated</em> stop so the client doesn't
    /// immediately try to reconnect to the daemon the user just killed (which otherwise spins
    /// and races the subsequent Start). Any explicit connect re-enables it.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Requests a connection. Fire-and-forget: returns immediately and connects in the
    /// background via the single-flight coordinator. The <see cref="Connected"/> event
    /// fires once established. Safe to call repeatedly (e.g. from the disconnect handler
    /// and UI commands) — extra requests coalesce.
    /// </summary>

    // Connect-failure log throttle (#21). A persistent reconnect loop retries ~every 18s (15s connect
    // timeout + 3s backoff); left unthrottled it would spam the single-generation log and wash out one-off
    // Warns. Log the first failure of each kind immediately, then at most once every few minutes until a
    // clean connect resets the counters.
    private const long ConnectLogThrottleMs = 5 * 60 * 1000;
    private long _lastTimeoutLogTick;
    private long _lastConnectErrorLogTick;

    private static bool ShouldLog(ref long lastTick)
    {
        var now = Environment.TickCount64;
        if (lastTick == 0 || now - lastTick >= ConnectLogThrottleMs) { lastTick = now; return true; }
        return false;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        // An explicit connect request always re-enables auto-reconnect for later drops.
        AutoReconnect = true;
        _connectFlight.Trigger(() => ConnectLoopAsync(ct));
        return Task.CompletedTask;
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        if (IsConnected) return; // a prior loop already (re)connected; nothing to do
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough | PipeOptions.CurrentUserOnly
                );

                // Wait generously for the daemon's pipe — a cold daemon can take ~5s to listen, and a
                // too-short timeout drops us into the 3s backoff below for no reason (#246).
                await _pipe.ConnectAsync(15000, ct);
                var rpc = new JsonRpc(_pipe);
                // Before the assignment, deliberately. From the moment `_rpc` points at the new channel a
                // send goes to the new daemon -- earlier than StartListening, earlier than Connected, and
                // far earlier than any host handler. An operation that read the incarnation before this
                // and sends after it must be recognisable as obsolete, and it only is if this moves first.
                // One assignment, so nothing can observe a half-established channel. The number is
                // allocated from a counter that only ever goes up: it must be unique for the life of the
                // client, and taking it from the previous channel could not be -- a disconnect clears
                // that, so the next connection reused the number the last one had.
                _channel = new Channel(rpc, Interlocked.Increment(ref _channelsOpened));
                _rpc = rpc;
                rpc.Disconnected += (_, _) =>
                {
                    // Drop the dead instance so IsConnected reads false immediately (its
                    // IsDisposed flips asynchronously). Guard against a late drop from a
                    // superseded connection clobbering a newer one.
                    if (ReferenceEquals(_rpc, rpc)) _rpc = null;
                    // The channel value goes with it. A hold taken on this one keeps working against the
                    // disposed RPC and fails, which is correct -- what must not happen is a later hold
                    // silently picking up a successor under the same number.
                    if (ReferenceEquals(_channel?.Rpc, rpc)) _channel = null;
                    // The daemon forgets the debug flag on disconnect; clear the count so it isn't
                    // left stale (which would suppress a later enable).
                    lock (_debugLock) _debugRefs.Reset();
                    Disconnected?.Invoke();
                    // Reconnect only on an UNEXPECTED drop — not a user-initiated Stop. If this
                    // fires during the current connect's release window, the coordinator still
                    // honors it (coalesced rerun) — no drop.
                    if (AutoReconnect)
                        ConnectAsync(ct);
                };
                rpc.AddLocalRpcMethod("DeviceReport", new Action<JObject>(OnDeviceReport));
                // The daemon forwards its TabletsChanged event as a same-named notification carrying the
                // new tablet list. We ignore the payload and just signal (accept it as a loose JToken so a
                // null/empty list can't fault the dispatch), then reload authoritatively (#170).
                rpc.AddLocalRpcMethod("TabletsChanged", new Action<JToken?>(OnTabletsChanged));
                // The daemon forwards its Message event as a same-named notification carrying a
                // serialized LogMessage; surface it for the Console page (#console).
                rpc.AddLocalRpcMethod("Message", new Action<JObject>(OnLogMessage));
                rpc.StartListening();
                Connected?.Invoke();
                _lastTimeoutLogTick = 0;      // clean connect — let the next offline spell log immediately
                _lastConnectErrorLogTick = 0;
                return;
            }
            catch (TimeoutException)
            {
                // Expected while the daemon isn't up yet — Debug, and throttled so a long wait doesn't
                // flood the log (#21).
                if (ShouldLog(ref _lastTimeoutLogTick))
                    _log.Debug("Daemon connect timed out; retrying (repeats throttled to 5 min).");
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Unexpected connect failure — Warn with the reason (was silently swallowed), throttled the
                // same way for a persistent failure (#21).
                if (ShouldLog(ref _lastConnectErrorLogTick))
                    _log.Warn("Daemon connect failed; retrying (repeats throttled to 5 min).", ex);
                await Task.Delay(3000, ct);
            }
        }
    }

    // --- Typed API using OTD types ---

    /// <inheritdoc />
    IDaemonSettingsBinding IDaemonSettingsChannel.Bind()
    {
        // One read of one reference. Everything the hold needs travels together, so there is no interval
        // in which a reconnect could pair one channel with another's number.
        var channel = _channel;
        return new Binding(channel?.Rpc, channel?.Incarnation ?? 0);
    }

    /// <summary>
    /// A hold on one JSON-RPC channel, captured by reference.
    ///
    /// The reference is the whole mechanism. Reading <c>_rpc</c> at send time asks "which channel is
    /// current", and the answer can be the replacement — so an operation admitted for one daemon sends to
    /// another, which for settings means one install's configuration arriving at a different one. Holding
    /// the instance asks nothing: the send goes where it was always going, or to a disposed channel that
    /// refuses it.
    ///
    /// A disposed channel is reported as "no transport", the same as never having had one, because to a
    /// caller they are the same fact: the change was not sent.
    /// </summary>
    private sealed class Binding(JsonRpc? rpc, int incarnation) : IDaemonSettingsBinding
    {
        public int Incarnation => incarnation;

        public async Task<Settings?> GetSettingsAsync()
        {
            if (rpc is not { IsDisposed: false }) return null;
            return await rpc.InvokeAsync<Settings>("GetSettings");
        }

        public async Task<bool> SetSettingsAsync(Settings settings)
        {
            if (rpc is not { IsDisposed: false }) return false;
            await rpc.InvokeAsync("SetSettings", settings);
            return true;
        }
    }

    public async Task<Settings?> GetSettingsAsync()
    {
        if (_rpc == null) return null;
        return await _rpc.InvokeAsync<Settings>("GetSettings");
    }

    /// <summary>Pushes settings to the daemon. Returns false when there is no transport — the caller must
    /// not report an unsent change as live (#734). Throws if the daemon is reachable but the call fails.</summary>
    public async Task<bool> SetSettingsAsync(Settings settings)
    {
        if (_rpc == null) return false;
        await _rpc.InvokeAsync("SetSettings", settings);
        return true;
    }

    public async Task<AppInfo?> GetAppInfoAsync()
    {
        if (_rpc == null) return null;
        return await _rpc.InvokeAsync<AppInfo>("GetApplicationInfo");
    }

    // Tablets are returned as JToken because the daemon returns TabletReference[]
    // which includes complex runtime state. We parse what we need.
    public async Task<JArray> GetTabletsAsync()
    {
        if (_rpc == null) return [];
        return await _rpc.InvokeAsync<JArray>("GetTablets");
    }

    /// <summary>
    /// The HID endpoints the daemon can SEE, which is not the same as the tablets it could OPEN. On macOS
    /// enumerating device metadata needs no permission while reading input reports does, so a supported
    /// tablet present here but absent from <c>GetTablets</c> is the signature of a missing Input
    /// Monitoring grant. Loosely typed like GetTablets; empty on any failure.
    /// </summary>
    public async Task<JArray> GetDevicesAsync()
    {
        if (_rpc is not { IsDisposed: false }) return new JArray();
        try { return await _rpc.InvokeAsync<JArray>("GetDevices"); }
        catch (Exception ex)
        {
            _log.Debug("Couldn't read the daemon's device list.", ex);
            return new JArray();
        }
    }

    private void OnDeviceReport(JObject data)
    {
        DeviceReport?.Invoke(data);
    }

    private void OnTabletsChanged(JToken? tablets)
    {
        TabletsChanged?.Invoke();
    }

    private void OnLogMessage(JObject data)
    {
        var message = data.ToObject<LogMessage>();
        if (message != null) LogReceived?.Invoke(message);
    }

    /// <summary>Snapshot of the daemon's current log buffer (seeds the Console on connect). Empty when
    /// not connected or on error.</summary>
    public async Task<List<LogMessage>> GetCurrentLogAsync()
    {
        if (_rpc == null) return [];
        try
        {
            var log = await _rpc.InvokeAsync<List<LogMessage>>("GetCurrentLog");
            return log ?? [];
        }
        catch (Exception ex)
        {
            _log.Warn("Couldn't fetch the daemon's current log buffer.", ex);
            return [];
        }
    }

    /// <summary>
    /// Returns the process ID of the daemon on the other end of the named pipe we're
    /// connected to, or null if not connected / the OS couldn't report it. Used to
    /// verify we're talking to this project's daemon and not a separate OTD instance.
    /// </summary>
    public int? GetServerProcessId()
    {
        var pipe = _pipe;
        if (pipe == null || !pipe.IsConnected) return null;
        try
        {
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid))
                return (int)pid;
        }
        catch (Exception ex)
        {
            // Best-effort ownership probe (can fail for an elevated daemon) — Debug, not a real problem (#21).
            _log.Debug("Couldn't read the daemon's server process id.", ex);
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle Pipe, out uint ServerProcessId);

    public async Task SetTabletDebugAsync(bool enabled)
    {
        if (_rpc == null) return;

        // Only hit the daemon on a 0↔1 transition: the first consumer turns the stream on, the last
        // turns it off. Intermediate acquires/releases just adjust the count.
        bool send;
        lock (_debugLock)
            send = enabled ? _debugRefs.Acquire() : _debugRefs.Release();

        if (!send) return;

        try
        {
            await _rpc.InvokeAsync("SetTabletDebug", enabled);
        }
        catch
        {
            // The enable didn't take, so undo the acquire — otherwise the count stays >0 and a later
            // acquire would suppress the enable RPC, leaving consumers "active" with no stream.
            // (A failed disable is fine to leave at 0: the next enable re-asserts it.) (Codex #119)
            if (enabled)
                lock (_debugLock) _debugRefs.RollbackAcquire();
            throw; // callers already catch and treat as a failed start/stop
        }
    }

    // --- Plugin management ---

    /// <summary>
    /// Downloads and installs (or upgrades) a plugin from its metadata. The daemon
    /// verifies the SHA256, extracts it into the plugin directory, and loads it.
    /// </summary>
    public async Task<bool> DownloadPluginAsync(PluginMetadata metadata)
    {
        if (_rpc == null) return false;
        return await _rpc.InvokeAsync<bool>("DownloadPlugin", metadata);
    }

    /// <summary>
    /// Uninstalls a loaded plugin. Despite the interface naming "friendlyName",
    /// the daemon matches on the plugin's full directory path.
    /// </summary>
    public async Task<bool> UninstallPluginAsync(string directoryPath)
    {
        if (_rpc == null) return false;
        return await _rpc.InvokeAsync<bool>("UninstallPlugin", directoryPath);
    }

    public async Task LoadPluginsAsync()
    {
        if (_rpc == null) return;
        await _rpc.InvokeAsync("LoadPlugins");
    }

    public void Dispose()
    {
        _rpc?.Dispose();
        _pipe?.Dispose();
    }
}
