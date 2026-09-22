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

    /// <summary>
    /// A write a discarded session gave up waiting for, and the daemon it was sent to (#919).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The knowledge that has to outlive the coordinator. A write OTA stopped waiting for is still the
    /// daemon's to finish, and it carries whole settings — so if it lands after a replacement session has
    /// applied and saved something else, it silently replaces that edit, and the replacement finds out
    /// only on some later refresh, with the artist's work already gone.
    /// </para>
    /// <para>
    /// Editing stays refused while one is outstanding. Two things end that: the write completing, or the
    /// process it was sent to being gone. Executable and settings paths matching does not establish the
    /// second — an intentional restart keeps both and changes the process — so the pid is what is
    /// compared, and an unknown pid counts as unresolved rather than as proof of anything.
    /// </para>
    /// </remarks>
    private Task<bool>? _outstandingWrite;
    private int? _outstandingWritePid;

    /// <summary>
    /// The driver process this session last identified, remembered while it was still answering.
    /// </summary>
    /// <remarks>
    /// Asked at retire time it is already gone — a dropped transport reports no pid — and an abandoned
    /// write would then have nothing to compare against and would block editing for ever, including
    /// after the driver restart its own message asks for.
    /// </remarks>
    private int? _connectedPid;

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
    public string SettingsProblem => _settings is { HasUnconfirmedWrite: true }
        ? "The last apply could not be confirmed. Restart the OTD daemon before editing or saving." : _problem;

    /// <summary>Retry initialization; useful once an abandoned write has finally completed.</summary>
    internal bool HasOutstandingWrite => WriteStillOutstanding();
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
        Retire();
        _ = InitializeAsync();
    }

    private void OnDisconnected()
    {
        Retire();
        if (!_disposed) Disconnected?.Invoke();
    }

    /// <summary>Discards the settings session, keeping what must outlive it.</summary>
    private void Retire()
    {
        SettingsCoordinator? going;
        lock (_state) going = Interlocked.Exchange(ref _settings, null);

        // Disposed BEFORE the marker is read (#923). Disposal closes admission, so once it returns a send
        // has either published its marker already or will never start -- and only then does reading the
        // marker answer the question. Reading first left a send admitted in that gap untracked.
        going?.Dispose();

        if (going?.OutstandingWrite is { } write && !write.IsCompletedSuccessfully)
        {
            _outstandingWrite = write;
            _outstandingWritePid = _connection.GetServerProcessId() ?? _connectedPid;
            _log.Warn("A settings write was never confirmed by the OTD daemon. Editing stays disabled until "
                      + "it finishes or that daemon process is gone, because it would replace whatever is "
                      + "applied in the meantime.");
        }
    }

    /// <summary>
    /// Whether an abandoned write can still overwrite a new session's work.
    /// </summary>
    /// <remarks>
    /// Resolved by the write completing, or by the pid differing from the one it was sent to — a
    /// different process cannot be holding it. An unknown pid on either side resolves nothing.
    /// </remarks>
    private bool WriteStillOutstanding()
    {
        if (_outstandingWrite is not { } write) return false;

        // Successfully, not merely completed (#922). A connection loss faults the client's task while the
        // server method runs on — the reply has nowhere to go — so a fault says the answer was lost, not
        // that the write was. Only the daemon returning an answer, or a different daemon process, ends
        // this.
        if (write.IsCompletedSuccessfully) { _outstandingWrite = null; _outstandingWritePid = null; return false; }

        var now = _connection.GetServerProcessId();
        if (_outstandingWritePid is { } then && now is { } current && then != current)
        {
            _outstandingWrite = null;
            _outstandingWritePid = null;
            return false;
        }
        return true;
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
            _connectedPid = _connection.GetServerProcessId();
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(info?.SettingsFile))
                _problem = "The OTD daemon or its settings location could not be identified. Settings are read-only.";
            else if ((_targetPath is not null && !PathEquality.Same(_targetPath, path))
                || (_targetSettingsPath is not null && !PathEquality.Same(_targetSettingsPath, info.SettingsFile)))
                _problem = "A different OpenTabletDriver is connected. Restart OpenTabletArtist to use it.";
            else if (WriteStillOutstanding())
            {
                _problem = "A settings change sent to this OTD daemon was never confirmed. Editing is "
                    + "disabled until it finishes or the daemon is restarted, so it cannot overwrite "
                    + "anything you do now.";
            }
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
                    _problem = "Couldn't read the OTD daemon's settings. Refresh to try again.";
                    if (loaded.Error is not null) _log.Warn(SettingsProblem, loaded.Error);
                }
            }
            Connected?.Invoke(new DaemonChange(path, channel));
        }
        catch (Exception ex)
        {
            if (_disposed || channel != _channel.Incarnation) return;
            _problem = "Couldn't read the OTD daemon's settings location. Refresh to try again.";
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
