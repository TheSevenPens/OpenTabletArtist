using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>The application's one editable document. Called on the host's UI thread; no UI dependency.</summary>
public sealed class SettingsWorkspace
{
    private readonly IOtdSettingsSession _session;
    private readonly bool _owned;
    private Settings? _draft;
    private Task<SettingsApplyOutcome> _pending = Task.FromResult(SettingsApplyOutcome.NoChange);
    private int _edit;
    private bool _failed;
    private bool _saving;
    public SettingsWorkspace(IOtdSettingsSession session, bool owned)
    {
        _session = session;
        _owned = owned;
        _draft = session.GetCurrent()?.Settings;
    }
    public Settings? Current => _draft is null ? null : SettingsCodec.Clone(_draft);
    public bool IsPaused => _session.IsPaused || _failed;
    public bool HasUnsavedChanges => _session.HasUnsavedChanges || !_pending.IsCompleted || _failed;
    public bool HasPendingApply => !_pending.IsCompleted;

    public Task<SettingsApplyOutcome> ApplyAsync(Settings requested)
    {
        if (IsPaused || _saving) return Task.FromResult(SettingsApplyOutcome.ChangedElsewhere);
        var copy = SettingsCodec.Clone(requested);
        OtaSettingsPolicy.Prepare(copy, _owned);
        _draft = copy;
        var edit = ++_edit;
        return _pending = ApplyCoreAsync(copy, edit);
    }

    public Task<SettingsApplyOutcome> ApplyProfileAsync(Profile profile)
    {
        var settings = Current;
        if (settings is null) return Task.FromResult(SettingsApplyOutcome.Disconnected);
        var index = settings.Profiles.ToList().FindIndex(p => p.Tablet == profile.Tablet);
        if (index < 0) return Task.FromResult(SettingsApplyOutcome.Disconnected);
        settings.Profiles[index] = profile;
        return ApplyAsync(settings);
    }

    private async Task<SettingsApplyOutcome> ApplyCoreAsync(Settings requested, int edit)
    {
        var outcome = await _session.ApplyAsync(requested);
        if (edit == _edit)
        {
            _failed = !outcome.IsLive;
            if (outcome.Prepared is { } confirmed) _draft = confirmed.Settings;
        }
        return outcome;
    }

    public async Task<SettingsSaveOutcome> SaveAsync()
    {
        if (_saving) return new(SettingsSaveStatus.Paused);
        _saving = true;
        try
        {
            await _pending;
            if (IsPaused) return new(SettingsSaveStatus.Paused);
            var outcome = await _session.SaveAsync();
            if (outcome.IsSaved) _draft = _session.GetCurrent()?.Settings;
            return outcome;
        }
        finally { _saving = false; }
    }

    public Task<SettingsReloadOutcome> RefreshAsync() => _session.RefreshAsync();

    public async Task<SettingsReloadOutcome> ReloadAsync()
    {
        ++_edit;
        var outcome = await _session.ReloadAsync();
        if (outcome.Adopted is { } adopted)
        {
            _draft = adopted.Settings;
            _failed = false;
        }
        return outcome;
    }

    public async Task<SettingsRestoreOutcome> RestoreAsync()
    {
        if (_saving || IsPaused) return SettingsRestoreOutcome.Failed(null);
        _saving = true;
        try
        {
            await _pending;
            if (IsPaused) return SettingsRestoreOutcome.Failed(null);
            var outcome = await _session.RestoreSavedAsync();
            if (!outcome.IsRestored)
            {
                _failed = outcome.Status != SettingsRestoreStatus.SourceUnavailable;
                return outcome;
            }
            ++_edit;
            _draft = outcome.Prepared?.Settings ?? _session.GetCurrent()?.Settings;
            _failed = false;
            return SettingsRestoreOutcome.Restored;
        }
        finally { _saving = false; }
    }
}
