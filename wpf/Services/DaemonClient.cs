using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Desktop.Updater;
using StreamJsonRpc;

namespace OtdWindowsHelper.Services;

public class DaemonClient : IDisposable
{
    private const string PipeName = "OpenTabletDriver.Daemon";

    private JsonRpc? _rpc;
    private NamedPipeClientStream? _pipe;
    // 0 = no connect loop active, 1 = a connect loop is running. Keeps reconnect single-flight
    // so the disconnect handler and UI-triggered connects can't spawn overlapping loops (#19).
    private int _connecting;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<JObject>? DeviceReport;
    public bool IsConnected => _rpc != null && !_rpc.IsDisposed;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;
        // Single-flight: if a connect loop is already running, don't start another.
        if (Interlocked.CompareExchange(ref _connecting, 1, 0) != 0)
            return;

        try
        {
            await ConnectLoopAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _connecting, 0);
        }
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
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
                    _ = Task.Run(() => ConnectAsync(ct), ct);
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
