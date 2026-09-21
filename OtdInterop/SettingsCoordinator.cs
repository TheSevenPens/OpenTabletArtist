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
    /// <summary>
    /// What is live and what is on disk, as one value (#919).
    /// </summary>
    /// <remarks>
    /// Two fields read independently cannot be a coherent observation, however carefully each one is
    /// read: <see cref="HasUnsavedChanges"/> compares them, and between the two reads an operation can
    /// replace either. Published as a single reference instead, swapped after each state change under
    /// the operation gate that already serializes the writers. Readers take the reference once and the
    /// documents inside it are not mutated afterwards.
    /// </remarks>
    private sealed record State(Settings? Current, SettingsFileSnapshot Saved);

    private State _state = new(null, new(null, null));
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

    private Task<bool>? _outstandingWrite;

    /// <summary>
    /// A write this session stopped waiting for, or null. Completed once the daemon is finally done
    /// with it (#919).
    /// </summary>
    /// <remarks>
    /// Survives this session deliberately. The write is still the daemon's to finish, and until it does,
    /// anything else editing that daemon can be overwritten by it without warning — so the owner carries
    /// this across a reconnect rather than discarding it with the coordinator.
    /// </remarks>
    internal Task<bool>? OutstandingWrite => Volatile.Read(ref _outstandingWrite);

    private State Published => Volatile.Read(ref _state);

    public PreparedSettings? GetCurrent() => Published.Current is { } current
        ? new(SettingsCodec.Clone(current)) : null;
    public bool IsPaused => _paused;

    public bool HasUnsavedChanges
    {
        get
        {
            var published = Published;
            return published.Current is { } current
                && (published.Saved.Settings is null || !SettingsCodec.Same(current, published.Saved.Settings));
        }
    }

    /// <summary>Swaps in the next published state. Callers hold the operation gate.</summary>
    private void Publish(Settings? current, SettingsFileSnapshot? saved = null) =>
        Volatile.Write(ref _state, new State(current, saved ?? Published.Saved));

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
            if (!accept && Published.Current is { } showing)
            {
                if (_paused || !SettingsCodec.Same(showing, observed))
                {
                    _paused = true;
                    return new SettingsReloadOutcome(SettingsReloadStatus.Paused);
                }
                return new SettingsReloadOutcome(SettingsReloadStatus.Unchanged);
            }
            var saved = _store.ReadPrimary(_path);
            if (!IsConnected) return new SettingsReloadOutcome(SettingsReloadStatus.Disconnected);
            Publish(observed, saved);
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
        if (Published.Current is not { } baseline) return SettingsApplyOutcome.Disconnected;
        try
        {
            var observed = await ReadAsync().ConfigureAwait(false);
            if (!SettingsCodec.Same(baseline, observed))
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
        if (SettingsCodec.Same(requested, baseline))
            return SettingsApplyOutcome.NoChange with { Prepared = GetCurrent() };
        // The write and its verification fail differently, and only one of them is terminal.
        //
        // The RPC task is kept, not just awaited. Giving up on the wait does not stop the write, and
        // whoever owns this connection has to be able to find out when it finally lands — a replacement
        // session cannot be trusted with the daemon until then (#919).
        var write = _channel.SetSettingsAsync(requested);
        try
        {
            if (!IsConnected || !await write.WaitAsync(_timeout, _lifetime.Token).ConfigureAwait(false))
                return SettingsApplyOutcome.Disconnected;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _outstandingWrite, write);
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
            Publish(confirmed);
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
        if (_paused || Published.Current is null) return new SettingsSaveOutcome(SettingsSaveStatus.Paused);
        try
        {
            // Repair the null-area shape that crashes OTD's own settings editor before persistence.
            var safe = SettingsCodec.Clone(Published.Current!);
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
            var published = Published;
            if (published.Current is not { } live
                || !SettingsCodec.Same(live, observed) || disk.Fingerprint != published.Saved.Fingerprint)
            {
                _paused = true;
                return new SettingsSaveOutcome(SettingsSaveStatus.Paused);
            }
            if (!IsConnected) return new SettingsSaveOutcome(SettingsSaveStatus.Disconnected);
            if (!_store.TrySave(live, _path)) return new SettingsSaveOutcome(SettingsSaveStatus.Failed);
            Publish(live, _store.ReadPrimary(_path));
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
        if (applied.IsLive) Publish(Published.Current, primary);
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
