namespace OtdInterop;

/// <summary>Reconnects transport, but opens a new settings session for each connection.</summary>
public sealed class OtdSession : IDisposable
{
    private readonly IDaemonTransport _connection;
    private readonly IDaemonSettingsChannel _channel;
    private readonly ISettingsFileStore _store;
    private readonly IOtdLog _log;
    private readonly IDaemonProcessLocator _locator;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private readonly object _state = new();
    private volatile SettingsCoordinator? _settings;
    private volatile bool _disposed;
    private string? _targetPath;
    private string? _targetSettingsPath;
    private string? _connectedPath;

    private OtdSession(IDaemonTransport connection, IDaemonSettingsChannel channel,
        ISettingsFileStore? store, IOtdLog log, IDaemonProcessLocator locator)
    {
        _connection = connection;
        _channel = channel;
        _store = store ?? new SettingsFileStore(log);
        _log = log;
        _locator = locator;
        Capabilities = new DaemonCapabilities(connection, () => _disposed);
        connection.Connected += OnConnected;
        connection.Disconnected += OnDisconnected;
    }

    /// <summary>Create an OTD pipe connection. The caller supplies logging and executable lookup.</summary>
    public static OtdSession Create(IOtdLog log, IDaemonProcessLocator locator)
    {
        var client = new DaemonClient(log);
        return new(client, client, null, log, locator);
    }

    internal static OtdSession ForTesting<T>(T connection, ISettingsFileStore? store,
        IOtdLog log, IDaemonProcessLocator locator) where T : IDaemonTransport, IDaemonSettingsChannel =>
        new(connection, connection, store, log, locator);

    /// <summary>Borrowed device, diagnostic and plugin operations; no raw settings writer.</summary>
    public IDaemonCapabilities Capabilities { get; }
    /// <summary>Settings for the current connection; null until identified and loaded.</summary>
    public IOtdSettingsSession? Settings => _settings;
    /// <summary>A loaded, unpaused settings session is available.</summary>
    public bool CanEditSettings => _settings is { IsConnected: true, IsPaused: false };
    private string _problem = "";
    /// <summary>Why settings are unavailable, or an empty string.</summary>
    public string SettingsProblem => _settings is { IsConnected: false } && ConnectionId != 0
        ? "The last apply could not be confirmed. Restart the driver before editing or saving." : _problem;
    /// <summary>Current transport identity, or zero while disconnected. Reject obsolete queued UI notifications using this value.</summary>
    public int ConnectionId => _channel.Incarnation;
    /// <summary>Executable identified on the most recent connection.</summary>
    public string? ConnectedExecutablePath => _connectedPath;
    /// <summary>Raised after initialization, including read-only connections. Runs on the transport thread.</summary>
    public event Action<DaemonChange>? Connected;
    /// <summary>Raised when transport disconnects. Runs on the transport thread.</summary>
    public event Action? Disconnected;
    /// <summary>Whether an unexpected drop retries the same pipe. Different daemon identities remain read-only.</summary>
    public bool AutoReconnect { get => _connection.AutoReconnect; set => _connection.AutoReconnect = value; }
    /// <summary>Pipe-server process identity when supported and connected.</summary>
    public int? ConnectedProcessId() => _connection.GetServerProcessId();
    /// <summary>Begin connecting in the background; explicit requests enable automatic reconnect.</summary>
    public Task ConnectAsync(CancellationToken ct) => _disposed ? Task.CompletedTask : _connection.ConnectAsync(ct);

    private void OnConnected()
    {
        lock (_state) Interlocked.Exchange(ref _settings, null)?.Dispose();
        _ = InitializeAsync();
    }

    private void OnDisconnected()
    {
        lock (_state) Interlocked.Exchange(ref _settings, null)?.Dispose();
        if (!_disposed) Disconnected?.Invoke();
    }

    /// <summary>Retry initial metadata/settings reads. A failed or replaced settings session is never migrated.</summary>
    public async Task InitializeAsync()
    {
        var channel = _channel.Incarnation;
        await _initialization.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || channel == 0 || channel != _channel.Incarnation || _settings is not null) return;
            string? path = _connection.GetServerProcessId() is { } pid ? _locator.PathOf(pid)
                : OperatingSystem.IsWindows() ? null : _locator.SingleRunningDaemonPath();
            var info = await _connection.GetAppInfoAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (_disposed || channel != _channel.Incarnation) return;
            _connectedPath = path;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(info?.SettingsFile))
                _problem = "The driver or its settings location could not be identified. Settings are read-only.";
            else if ((_targetPath is not null && !PathEquality.Same(_targetPath, path))
                || (_targetSettingsPath is not null && !PathEquality.Same(_targetSettingsPath, info.SettingsFile)))
                _problem = "A different OpenTabletDriver is connected. Restart OpenTabletArtist to use it.";
            else
            {
                _targetPath ??= path;
                _targetSettingsPath ??= info.SettingsFile;
                var settings = new SettingsCoordinator(_channel, _store, info.SettingsFile);
                var loaded = await settings.ReloadAsync().ConfigureAwait(false);
                if (_disposed || channel != _channel.Incarnation) { settings.Dispose(); return; }
                if (loaded.Status == SettingsReloadStatus.Adopted)
                {
                    lock (_state)
                    {
                        if (_disposed || channel != _channel.Incarnation) { settings.Dispose(); return; }
                        _settings = settings;
                        _problem = "";
                    }
                }
                else
                {
                    settings.Dispose();
                    _problem = "Couldn't read the driver's settings. Refresh to try again.";
                    if (loaded.Error is not null) _log.Warn(SettingsProblem, loaded.Error);
                }
            }
            Connected?.Invoke(new DaemonChange(path, channel));
        }
        catch (Exception ex)
        {
            if (_disposed || channel != _channel.Incarnation) return;
            _problem = "Couldn't read the driver's settings location. Refresh to try again.";
            _log.Warn(SettingsProblem, ex);
            Connected?.Invoke(new DaemonChange(_connectedPath, channel));
        }
        finally { _initialization.Release(); }
    }

    /// <summary>Close admission and transport, then bound the wait for local settings operations. Does not save.</summary>
    public async Task<bool> CloseAsync(TimeSpan? settleWithin = null)
    {
        var settings = _settings;
        Dispose();
        return settings is null || await settings.CloseAsync(settleWithin ?? TimeSpan.FromSeconds(5))
            .ConfigureAwait(false);
    }

    /// <summary>Cancel settings work and disconnect without saving. Safe to call repeatedly.</summary>
    public void Dispose()
    {
        lock (_state)
        {
            if (_disposed) return;
            _disposed = true;
            _settings?.Dispose();
        }
        _connection.AutoReconnect = false;
        _connection.Connected -= OnConnected;
        _connection.Disconnected -= OnDisconnected;
        _connection.Dispose();
    }
}
