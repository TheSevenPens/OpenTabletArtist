using System.IO.Pipes;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace TabletDriverUX.Services;

public class DaemonClient : IDisposable
{
    private const string PipeName = "OpenTabletDriver.Daemon";

    private JsonRpc? _rpc;
    private NamedPipeClientStream? _pipe;

    public event Action? Connected;
    public event Action? Disconnected;
    public bool IsConnected => _rpc != null && !_rpc.IsDisposed;

    public async Task ConnectAsync(CancellationToken ct = default)
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

    public async Task<JToken?> CheckForUpdatesAsync()
    {
        if (_rpc == null) return null;
        try
        {
            return await _rpc.InvokeAsync<JToken?>("CheckForUpdates");
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _rpc?.Dispose();
        _pipe?.Dispose();
    }
}
