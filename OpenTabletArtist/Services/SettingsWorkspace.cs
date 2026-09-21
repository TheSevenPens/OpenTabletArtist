using System;
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

    /// <summary>
    /// Observes the driver, and adopts what it finds when nothing local is at stake (#920).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The driver edits its own settings: attaching a tablet it has not seen makes it generate a profile
    /// and write the whole document back. Pausing for that stopped an artist who had done nothing but
    /// plug something in. While idle there is no draft to protect and nothing to merge — one live
    /// snapshot to show — so the honest response is to show what the driver actually holds.
    /// </para>
    /// <para>
    /// <b>Idle is not "saved".</b> Settings applied but not written to disk are live driver state; their
    /// difference from the file is not a reason to refuse a refresh. What makes this unsafe is a local
    /// edit that would be replaced: unsubmitted input, an apply in flight, a failed one awaiting a
    /// decision, or a pause somebody already has to answer.
    /// </para>
    /// <para>
    /// <b>Checked on both sides of the read.</b> Testing eligibility only before the observation is not
    /// enough: the read takes as long as the driver takes, and an edit can arrive — or start and finish —
    /// while it is outstanding. The edit counter is captured first and compared afterwards, so input that
    /// appeared during the observation keeps its pause instead of being adopted over.
    /// </para>
    /// <para>
    /// A pause that was already there is never cleared by this route. Answering it is the artist's, and
    /// a background poll silently resolving it is how a protection becomes a formality.
    /// </para>
    /// </remarks>
    /// <param name="editingIsIdle">
    /// Whether the host has unsubmitted editor input. Decided by OTA rather than the library: only the
    /// host knows what is half-typed into a control, and teaching the library about that is exactly the
    /// coupling this split removed.
    /// </param>
    public async Task<SettingsReloadOutcome> RefreshAsync(Func<bool> editingIsIdle)
    {
        var pausedBefore = _session.IsPaused;
        var editBefore = _edit;

        var observed = await _session.RefreshAsync();
        if (pausedBefore || observed.Status != SettingsReloadStatus.Paused) return observed;

        // Re-asked after the read, on the UI thread this runs on, so the answer describes now.
        if (editBefore != _edit || HasPendingApply || _failed || _saving || !editingIsIdle())
            return observed;

        return await ReloadAsync();
    }

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
