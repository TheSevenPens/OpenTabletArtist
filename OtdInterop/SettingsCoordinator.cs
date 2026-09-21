using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>One connection, one file, one gate. No callbacks, retries, or connection migration.</summary>
internal sealed class SettingsCoordinator : IOtdSettingsSession, IDisposable
{
    private readonly IDaemonSettingsBinding _channel;
    private readonly IDaemonSettingsChannel _connection;
    private readonly ISettingsFileStore _store;
    private readonly string _path;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeSpan _timeout;
    private Settings? _current;
    private SettingsFileSnapshot _saved = new(null, null);
    private volatile bool _paused;
    private volatile bool _closed;

    /// <summary>
    /// A write whose completion was never established, so this session is finished (#919).
    /// </summary>
    /// <remarks>
    /// Stated rather than inferred. Read off "disposed but the transport is still up" it also caught a
    /// session closed for any other reason and told those callers to restart their driver.
    /// </remarks>
    private volatile bool _unconfirmed;

    internal SettingsCoordinator(IDaemonSettingsChannel connection, ISettingsFileStore store,
        string path, TimeSpan? timeout = null)
    {
        _connection = connection;
        _channel = connection.Bind();
        _store = store;
        _path = path;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    internal bool IsConnected => !_closed && _channel.Incarnation != 0
        && _channel.Incarnation == _connection.Incarnation;

    /// <summary>Closed because a write's completion could never be established (#919).</summary>
    internal bool HasUnconfirmedWrite => _unconfirmed;

    public PreparedSettings? GetCurrent() => Volatile.Read(ref _current) is { } current
        ? new(SettingsCodec.Clone(current)) : null;
    public bool IsPaused => _paused;
    public bool HasUnsavedChanges => _current is { } current
        && (_saved.Settings is null || !SettingsCodec.Same(current, _saved.Settings));

    private async Task<T> RunAsync<T>(Func<Task<T>> operation, T disconnected)
    {
        try { await _operations.WaitAsync(_lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return disconnected; }
        try { return IsConnected ? await operation().ConfigureAwait(false) : disconnected; }
        finally { _operations.Release(); }
    }

    private async Task<Settings> ReadAsync()
    {
        var settings = await _channel.GetSettingsAsync().WaitAsync(_timeout, _lifetime.Token)
            .ConfigureAwait(false);
        if (!IsConnected || settings is null) throw new IOException("The driver connection is unavailable.");
        return SettingsCodec.Clone(settings);
    }

    public Task<SettingsReloadOutcome> RefreshAsync() => ReadAndAdoptAsync(accept: false);
    public Task<SettingsReloadOutcome> ReloadAsync() => ReadAndAdoptAsync(accept: true);

    private Task<SettingsReloadOutcome> ReadAndAdoptAsync(bool accept) => RunAsync(async () =>
    {
        try
        {
            var observed = await ReadAsync().ConfigureAwait(false);
            if (!accept && _current is not null)
            {
                if (_paused || !SettingsCodec.Same(_current, observed))
                {
                    _paused = true;
                    return new SettingsReloadOutcome(SettingsReloadStatus.Paused);
                }
                return new SettingsReloadOutcome(SettingsReloadStatus.Unchanged);
            }
            var saved = _store.ReadPrimary(_path);
            if (!IsConnected) return new SettingsReloadOutcome(SettingsReloadStatus.Disconnected);
            _saved = saved;
            _current = observed;
            _paused = false;
            return new SettingsReloadOutcome(SettingsReloadStatus.Adopted, GetCurrent());
        }
        catch (Exception ex)
        {
            _paused = true;
            return new SettingsReloadOutcome(IsConnected ? SettingsReloadStatus.Failed
                : SettingsReloadStatus.Disconnected, Error: ex);
        }
    }, new SettingsReloadOutcome(SettingsReloadStatus.Disconnected));

    public Task<SettingsApplyOutcome> ApplyAsync(Settings requested)
    {
        Settings copy;
        try { copy = SettingsCodec.Clone(requested); }
        catch (Exception ex) { return Task.FromResult(SettingsApplyOutcome.Failed(ex)); }
        ProfileSanitizer.EnsureValidAbsoluteAreas(copy);
        return RunAsync(() => ApplyCoreAsync(copy), SettingsApplyOutcome.Disconnected);
    }

    private async Task<SettingsApplyOutcome> ApplyCoreAsync(Settings requested)
    {
        if (_paused) return SettingsApplyOutcome.ChangedElsewhere;
        if (_current is null) return SettingsApplyOutcome.Disconnected;
        try
        {
            var observed = await ReadAsync().ConfigureAwait(false);
            if (!SettingsCodec.Same(_current, observed))
            {
                _paused = true;
                return SettingsApplyOutcome.ChangedElsewhere;
            }
        }
        catch (Exception ex)
        {
            _paused = true;
            return IsConnected ? SettingsApplyOutcome.CouldNotCheck with { Error = ex }
                : SettingsApplyOutcome.Disconnected;
        }
        if (SettingsCodec.Same(requested, _current))
            return SettingsApplyOutcome.NoChange with { Prepared = GetCurrent() };
        // The write and its verification fail differently, and only one of them is terminal.
        try
        {
            if (!IsConnected || !await _channel.SetSettingsAsync(requested)
                    .WaitAsync(_timeout, _lifetime.Token).ConfigureAwait(false))
                return SettingsApplyOutcome.Disconnected;
        }
        catch (Exception ex)
        {
            // The write has not returned, so we do not know whether it landed — and we cannot find out.
            // SetSettings takes no cancellation token and OTD's RPC host serves every connection against
            // the same daemon object, so neither a fresh pipe nor restarting OTA stops work already
            // accepted. Only the daemon going away does. Terminal, and the message says which remedy
            // actually works.
            _unconfirmed = true;
            Dispose();
            return SettingsApplyOutcome.Failed(new IOException(
                "The apply could not be confirmed. Restart the driver before editing or saving.", ex));
        }

        try
        {
            // OTD may recover a failed SetSettings and still complete the RPC normally.
            var confirmed = await ReadAsync().ConfigureAwait(false);
            _current = confirmed;
            if (!SettingsCodec.Same(requested, confirmed))
            {
                _paused = true;
                return SettingsApplyOutcome.Failed(new InvalidOperationException(
                    "The driver changed or rejected part of the settings. Reload and review its current values."));
            }
            return SettingsApplyOutcome.Live with { Prepared = GetCurrent() };
        }
        catch (Exception ex)
        {
            // The write returned; only the read back failed. Nothing is outstanding against the daemon,
            // so what the settings are is merely unknown rather than unknowable — and another read can
            // establish it. Paused, not closed: Reload is a way out from here.
            _paused = true;
            return IsConnected
                ? SettingsApplyOutcome.CouldNotCheck with { Error = ex }
                : SettingsApplyOutcome.Disconnected;
        }
    }

    public Task<SettingsSaveOutcome> SaveAsync() => RunAsync(async () =>
    {
        if (_paused || _current is null) return new SettingsSaveOutcome(SettingsSaveStatus.Paused);
        try
        {
            // Repair the null-area shape that crashes OTD's own settings editor before persistence.
            var safe = SettingsCodec.Clone(_current);
            if (ProfileSanitizer.EnsureValidAbsoluteAreas(safe) > 0)
            {
                var repaired = await ApplyCoreAsync(safe).ConfigureAwait(false);
                if (!repaired.IsLive) return new SettingsSaveOutcome(SettingsSaveStatus.Paused, repaired.Error);
            }
            var observed = await ReadAsync().ConfigureAwait(false);
            var disk = _store.ReadPrimary(_path);
            if (disk.Fingerprint is null)
                return new SettingsSaveOutcome(SettingsSaveStatus.Failed,
                    new IOException("The existing saved settings could not be inspected."));
            if (!SettingsCodec.Same(_current, observed) || disk.Fingerprint != _saved.Fingerprint)
            {
                _paused = true;
                return new SettingsSaveOutcome(SettingsSaveStatus.Paused);
            }
            if (!IsConnected) return new SettingsSaveOutcome(SettingsSaveStatus.Disconnected);
            if (!_store.TrySave(_current, _path)) return new SettingsSaveOutcome(SettingsSaveStatus.Failed);
            _saved = _store.ReadPrimary(_path);
            return new SettingsSaveOutcome(!HasUnsavedChanges ? SettingsSaveStatus.Saved : SettingsSaveStatus.Failed);
        }
        catch (Exception ex) { return new SettingsSaveOutcome(SettingsSaveStatus.Failed, ex); }
    }, new SettingsSaveOutcome(SettingsSaveStatus.Disconnected));

    public Task<SettingsRestoreOutcome> RestoreSavedAsync() => RunAsync(async () =>
    {
        var primary = _store.ReadPrimary(_path);
        var saved = primary.Settings is null ? null : SettingsCodec.Clone(primary.Settings);
        if (saved is null && (!_store.TryLoad(_path, out saved) || saved is null))
            return SettingsRestoreOutcome.SourceUnavailable;
        ProfileSanitizer.EnsureValidAbsoluteAreas(saved);
        var applied = await ApplyCoreAsync(saved).ConfigureAwait(false);
        if (applied.IsLive) _saved = primary;
        return applied.IsLive ? SettingsRestoreOutcome.Restored with { Prepared = applied.Prepared }
            : applied.Status == SettingsApplyStatus.Disconnected ? SettingsRestoreOutcome.Disconnected
            : SettingsRestoreOutcome.Failed(applied.Error);
    }, SettingsRestoreOutcome.Disconnected);

    internal async Task<bool> CloseAsync(TimeSpan within)
    {
        Dispose();
        if (!await _operations.WaitAsync(within).ConfigureAwait(false)) return false;
        _operations.Release();
        return true;
    }

    public void Dispose()
    {
        _closed = true;
        _lifetime.Cancel();
    }
}
