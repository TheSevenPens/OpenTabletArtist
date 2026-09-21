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
    public bool IsPaused => _session.IsPaused || _failed || _awaitingDecision;

    /// <summary>
    /// An adoption that could not finish because input arrived while it was running (#922).
    /// </summary>
    /// <remarks>
    /// The library has already taken the driver's values as its baseline by then, so the pre-apply
    /// comparison would let the artist's edit through against settings it was never weighed with. Held
    /// here instead, on the host's side of the decision, until an explicit Reload settles it — which is
    /// what every other pause means too.
    /// </remarks>
    private bool _awaitingDecision;
    public bool HasUnsavedChanges => _session.HasUnsavedChanges || !_pending.IsCompleted || _failed;
    public bool HasPendingApply => !_pending.IsCompleted;

    /// <summary>
    /// Bumped whenever the document is <b>replaced</b> rather than edited (#922).
    /// </summary>
    /// <remarks>
    /// A dialog that captures the whole settings and submits a profile from them minutes later has no
    /// way to notice that the ground moved underneath it — an adopted outside change, a Reload, a
    /// restore. Its submission then passes the pre-apply comparison, because the baseline it is weighed
    /// against is the new one, and quietly puts the old values back. Something that holds a document
    /// across time can compare this instead.
    /// </remarks>
    public int Generation { get; private set; }

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
        // This workspace's pause, not the library's (#923). A pause raised here because input arrived
        // during an adoption is invisible to the library -- it has already taken the driver's values and
        // cleared its own -- so asking the library let a second outside change adopt straight over it.
        var pausedBefore = IsPaused;
        var editBefore = _edit;

        var observed = await _session.RefreshAsync();
        if (pausedBefore || observed.Status != SettingsReloadStatus.Paused) return observed;

        // Re-asked after the read, on the UI thread this runs on, so the answer describes now.
        if (!StillIdle(editBefore, editingIsIdle)) return observed;

        // Adoption is a SECOND read, and just as long. Asking once before it and publishing whatever
        // comes back discarded input that arrived while it was running — the check has to bracket the
        // whole sequence, not just its beginning (#922).
        //
        // The counter is claimed first, so anything the artist does during the read moves it past this
        // value and is visible afterwards. Adoption is its own edit as far as everything else is
        // concerned: it replaces what the editors are showing.
        var mine = ++_edit;
        var adopted = await AdoptAsync();
        if (adopted.Status != SettingsReloadStatus.Adopted) return adopted;

        if (StillIdle(mine, editingIsIdle)) return adopted;

        // Somebody started editing while this was adopting. Their input is theirs to keep, so it is not
        // published over — and the baseline underneath it has moved, so their next edit must not sail
        // through the pre-apply comparison as though nothing had. Paused until they decide.
        _awaitingDecision = true;
        return observed;
    }

    private bool StillIdle(int expectedEdit, Func<bool> editingIsIdle) =>
        expectedEdit == _edit && !HasPendingApply && !_failed && !_saving && editingIsIdle();

    public Task<SettingsReloadOutcome> ReloadAsync()
    {
        ++_edit;
        return AdoptAsync();
    }

    /// <summary>Takes the driver's current document, without claiming the edit counter.</summary>
    /// <remarks>
    /// Split out because automatic adoption claims the counter itself, one step earlier, so that input
    /// arriving during the read is distinguishable afterwards. Reload claims it here instead.
    /// </remarks>
    private async Task<SettingsReloadOutcome> AdoptAsync()
    {
        var outcome = await _session.ReloadAsync();
        if (outcome.Adopted is { } adopted)
        {
            _draft = adopted.Settings;
            _failed = false;
            _awaitingDecision = false;
            Generation++;
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
            Generation++;
            return SettingsRestoreOutcome.Restored;
        }
        finally { _saving = false; }
    }
}
