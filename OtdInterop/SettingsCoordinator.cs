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
        try
        {
            if (!IsConnected || !await _channel.SetSettingsAsync(requested)
                    .WaitAsync(_timeout, _lifetime.Token).ConfigureAwait(false))
                return SettingsApplyOutcome.Disconnected;

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
            // A timed-out write may still land. Do not let a reload or Save race it.
            Dispose();
            return SettingsApplyOutcome.Failed(new IOException(
                "The apply could not be confirmed. Restart the driver before editing or saving.", ex));
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
