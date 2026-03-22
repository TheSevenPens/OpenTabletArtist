using System.IO.Pipes;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Bridge.Services;

public class DaemonClient : IDisposable
{
    private const string PipeName = "OpenTabletDriver.Daemon";

    private JsonRpc? _rpc;
    private NamedPipeClientStream? _pipe;
    private readonly ILogger<DaemonClient> _logger;

    public event Action<string>? OnDaemonEvent;
    public bool IsConnected => _rpc != null && !_rpc.IsDisposed;

    public DaemonClient(ILogger<DaemonClient> logger)
    {
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to OTD daemon on pipe '{Pipe}'...", PipeName);

                _pipe = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough | PipeOptions.CurrentUserOnly
                );

                await _pipe.ConnectAsync(5000, ct);
                // OTD uses the default JsonRpc constructor which uses NewLineDelimited framing
                _rpc = new JsonRpc(_pipe);
                _rpc.Disconnected += (_, _) =>
                {
                    _logger.LogWarning("Disconnected from OTD daemon");
                    _ = Task.Run(() => ConnectAsync(ct), ct);
                };
                _rpc.StartListening();

                _logger.LogInformation("Connected to OTD daemon");
                return;
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("OTD daemon not found, retrying in 3s...");
                await Task.Delay(3000, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to OTD daemon, retrying in 3s...");
                await Task.Delay(3000, ct);
            }
        }
    }

    // StreamJsonRpc uses Newtonsoft.Json internally, so we must use JToken (not System.Text.Json)
    // for passthrough. ASP.NET will serialize JToken to JSON automatically.

    public async Task<JToken> GetTabletsAsync()
    {
        if (_rpc == null) return JToken.Parse("[]");
        return await _rpc.InvokeAsync<JToken>("GetTablets");
    }

    public async Task<JToken> GetSettingsAsync()
    {
        if (_rpc == null) return JToken.Parse("null");
        return await _rpc.InvokeAsync<JToken>("GetSettings");
    }

    public async Task SetSettingsAsync(JToken settings)
    {
        if (_rpc == null) return;
        await _rpc.InvokeAsync("SetSettings", settings);
    }

    public async Task<JToken> GetAppInfoAsync()
    {
        if (_rpc == null) return JToken.Parse("null");
        return await _rpc.InvokeAsync<JToken>("GetApplicationInfo");
    }

    public void Dispose()
    {
        _rpc?.Dispose();
        _pipe?.Dispose();
    }
}
