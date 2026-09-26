using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Plugin.Logging;
using StreamJsonRpc;

namespace OtdInterop;

/// <summary>A one-shot, read-only diagnostic connection. Never starts OTD, reconnects, changes
/// settings, enables debugging, or installs plugins. Dispose closes only this client's pipe.</summary>
public sealed class DiagnosticsConnection : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private JsonRpc? _rpc;

    /// <summary>Creates an unconnected client. A custom pipe is useful for isolated test servers.</summary>
    public DiagnosticsConnection(string pipeName = "OpenTabletDriver.Daemon") =>
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough | PipeOptions.CurrentUserOnly);

    /// <summary>Connects once. Caller supplies a deadline through cancellation.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _rpc = new JsonRpc(_pipe);
        _rpc.AddLocalRpcMethod("Message", new Action<JObject>(_ => { }));
        _rpc.AddLocalRpcMethod("TabletsChanged", new Action<JToken?>(_ => { }));
        _rpc.StartListening();
    }

    private Task<T> ReadAsync<T>(string method, CancellationToken ct) =>
        (_rpc ?? throw new InvalidOperationException("Not connected."))
            .InvokeWithCancellationAsync<T>(method, Array.Empty<object>(), ct);

    /// <summary>Reads settings without normalization, repair, or writeback.</summary>
    public async Task<Settings> GetSettingsAsync(CancellationToken ct)
    {
        var json = await ReadAsync<JObject>("GetSettings", ct).ConfigureAwait(false);
        if (json?["Profiles"] is not JArray profiles) throw new InvalidDataException("Settings contain no Profiles array.");
        foreach (var p in profiles)
            if (p["Tablet"]?.Type != JTokenType.String || p["Bindings"] is not JObject bindings
                || bindings["DisablePressure"]?.Type != JTokenType.Boolean || bindings["DisableTilt"]?.Type != JTokenType.Boolean
                || bindings.Property("TipButton") == null || (p as JObject)?.Property("OutputMode") == null)
                throw new InvalidDataException("Settings contain incomplete profile evidence.");
        return json.ToObject<Settings>() ?? throw new InvalidDataException("Settings are null.");
    }
    /// <summary>Reads detected tablets. RPC failures are propagated, never translated to empty lists.</summary>
    public Task<JArray> GetTabletsAsync(CancellationToken ct) => ReadAsync<JArray>("GetTablets", ct);
    /// <summary>Reads enumerated devices without opening or changing them on the client.</summary>
    public Task<JArray> GetDevicesAsync(CancellationToken ct) => ReadAsync<JArray>("GetDevices", ct);
    /// <summary>Reads paths as JSON, avoiding OTD AppInfo's local fallback getters.</summary>
    public Task<JObject> GetApplicationInfoAsync(CancellationToken ct) => ReadAsync<JObject>("GetApplicationInfo", ct);
    /// <summary>Reads existing daemon log messages; does not trigger detection.</summary>
    public Task<List<LogMessage>> GetCurrentLogAsync(CancellationToken ct) => ReadAsync<List<LogMessage>>("GetCurrentLog", ct);

    /// <summary>The executable serving this pipe on Windows. Other platforms cannot establish the
    /// server identity through this API and return null rather than guessing from process names.</summary>
    public string? ServerExecutablePath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!GetNamedPipeServerProcessId(_pipe.SafePipeHandle, out var pid))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        using var process = Process.GetProcessById(checked((int)pid));
        return process.MainModule?.FileName;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);

    /// <summary>Closes this diagnostic client, leaving the daemon untouched.</summary>
    public void Dispose() { _rpc?.Dispose(); _pipe.Dispose(); }
}
