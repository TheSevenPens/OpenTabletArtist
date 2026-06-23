using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Desktop.Updater;
using OtdWindowsHelper.Concurrency;
using StreamJsonRpc;

namespace OtdWindowsHelper.Services;

public class DaemonClient : IDisposable, IDaemonDebugSession
{
    private const string PipeName = "OpenTabletDriver.Daemon";

    private JsonRpc? _rpc;
    private NamedPipeClientStream? _pipe;
    // Single-flight reconnect coordinator: only one connect loop runs at a time, and a
    // reconnect requested while one is running (e.g. an immediate disconnect during connect)
    // is honored once the current loop exits — closing the dropped-reconnect race (#33).
    private readonly CoalescingSingleFlight _connectFlight = new();

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<JObject>? DeviceReport;
    public bool IsConnected => _rpc != null && !_rpc.IsDisposed;

    /// <summary>
    /// Requests a connection. Fire-and-forget: returns immediately and connects in the
    /// background via the single-flight coordinator. The <see cref="Connected"/> event
    /// fires once established. Safe to call repeatedly (e.g. from the disconnect handler
    /// and UI commands) — extra requests coalesce.
    /// </summary>
    public Task ConnectAsync(CancellationToken ct = default)
    {
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

                await _pipe.ConnectAsync(5000, ct);
                _rpc = new JsonRpc(_pipe);
                _rpc.Disconnected += (_, _) =>
                {
                    Disconnected?.Invoke();
                    // Trigger a reconnect. If this fires during the current connect's release
                    // window, the coordinator still honors it (coalesced rerun) — no drop.
                    ConnectAsync(ct);
                };
                _rpc.AddLocalRpcMethod("DeviceReport", new Action<JObject>(OnDeviceReport));
                _rpc.StartListening();
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
        await _rpc.InvokeAsync("SetTabletDebug", enabled);
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
