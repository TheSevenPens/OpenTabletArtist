using System.IO.Pipes;
using System.Text.Json;
using StreamJsonRpc;

namespace Bridge.Services;

public class DaemonClient : IDisposable
{
    private const string PipeName = "OpenTabletDriver.Daemon";

    private JsonRpc? _rpc;
    private NamedPipeClientStream? _pipe;
    private readonly ILogger<DaemonClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
                _rpc = new JsonRpc(new HeaderDelimitedMessageHandler(_pipe));
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

    public async Task<JsonElement> GetTabletsAsync()
    {
        if (_rpc == null) return JsonDocument.Parse("[]").RootElement;
        return await _rpc.InvokeAsync<JsonElement>("GetTablets");
    }

    public async Task<JsonElement> GetSettingsAsync()
    {
        if (_rpc == null) return JsonDocument.Parse("null").RootElement;
        return await _rpc.InvokeAsync<JsonElement>("GetSettings");
    }

    public async Task SetSettingsAsync(JsonElement settings)
    {
        if (_rpc == null) return;
        await _rpc.InvokeAsync("SetSettings", settings);
    }

    public async Task<JsonElement> GetAppInfoAsync()
    {
        if (_rpc == null) return JsonDocument.Parse("null").RootElement;
        return await _rpc.InvokeAsync<JsonElement>("GetApplicationInfo");
    }

    public void Dispose()
    {
        _rpc?.Dispose();
        _pipe?.Dispose();
    }
}
