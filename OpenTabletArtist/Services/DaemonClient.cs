using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Desktop.Updater;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Concurrency;
using StreamJsonRpc;

namespace OpenTabletArtist.Services;

public class DaemonClient : IDisposable, IDaemonDebugSession, IDaemonLogSource
{
    private const string PipeName = "OpenTabletDriver.Daemon";

    private JsonRpc? _rpc;
    private NamedPipeClientStream? _pipe;

    // The daemon's tablet-debug stream is a single global flag, but several consumers want it (the
    // Test view, the Diagnostics page, the Dynamics tab's live-pressure dot). Reference-count it so
    // one consumer turning it off doesn't starve another. (#102 follow-up)
    private readonly object _debugLock = new();
    private int _debugRefCount;
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
    public bool IsConnected => _rpc != null && !_rpc.IsDisposed;

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
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough | PipeOptions.CurrentUserOnly
                );

                // Wait generously for the daemon's pipe — a cold daemon can take ~5s to listen, and a
                // too-short timeout drops us into the 3s backoff below for no reason (#246).
                await _pipe.ConnectAsync(15000, ct);
                var rpc = new JsonRpc(_pipe);
                _rpc = rpc;
                rpc.Disconnected += (_, _) =>
                {
                    // Drop the dead instance so IsConnected reads false immediately (its
                    // IsDisposed flips asynchronously). Guard against a late drop from a
                    // superseded connection clobbering a newer one.
                    if (ReferenceEquals(_rpc, rpc)) _rpc = null;
                    // The daemon forgets the debug flag on disconnect; clear the count so it isn't
                    // left stale (which would suppress a later enable).
                    lock (_debugLock) _debugRefCount = 0;
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
                return;
            }
            catch (TimeoutException)
            {
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                await Task.Delay(3000, ct);
            }
        }
    }

    // --- Typed API using OTD types ---

    public async Task<Settings?> GetSettingsAsync()
    {
        if (_rpc == null) return null;
        return await _rpc.InvokeAsync<Settings>("GetSettings");
    }

    public async Task SetSettingsAsync(Settings settings)
    {
        if (_rpc == null) return;
        await _rpc.InvokeAsync("SetSettings", settings);
    }

    public async Task<AppInfo?> GetAppInfoAsync()
    {
        if (_rpc == null) return null;
        return await _rpc.InvokeAsync<AppInfo>("GetApplicationInfo");
    }

    public async Task<SerializedUpdateInfo?> CheckForUpdatesAsync()
    {
        if (_rpc == null) return null;
        try
        {
            return await _rpc.InvokeAsync<SerializedUpdateInfo?>("CheckForUpdates");
        }
        catch
        {
            return null;
        }
    }

    // Tablets are returned as JToken because the daemon returns TabletReference[]
    // which includes complex runtime state. We parse what we need.
    public async Task<JArray> GetTabletsAsync()
    {
        if (_rpc == null) return [];
        return await _rpc.InvokeAsync<JArray>("GetTablets");
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
        catch
        {
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
        catch { }
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
            send = enabled ? ++_debugRefCount == 1
                           : _debugRefCount > 0 && --_debugRefCount == 0;

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
                lock (_debugLock) { if (_debugRefCount > 0) _debugRefCount--; }
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
