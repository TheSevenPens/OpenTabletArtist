using System;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Owns the settings OTA believes in, and every way they reach the daemon or the disk (#740).
///
/// Extracted from <see cref="AppSession"/>, which still implements <see cref="ISettingsCoordinator"/> and
/// delegates here — so no consumer changed. What moved is the state that makes the apply path hard to
/// reason about when it is interleaved with connection lifecycle, data loading and daemon process
/// control: the current settings, the two separate revision baselines from #734, the pending unsaved
/// change, the per-app override flag from #737, and the apply-loop circuit breaker.
///
/// Headless and thread-agnostic on purpose. <c>AppSession</c> keeps the
/// <c>Dispatcher.UIThread.VerifyAccess()</c> guards on its entry points because it owns observable state;
/// this class owns none, which is what makes it testable without a dispatcher.
///
/// It also holds no reference back to the session. Apply-then-reload is orchestrated by the caller, so
/// the dependency runs one way.
/// </summary>
public sealed class SettingsCoordinator
{
    private readonly IDaemonTransport _daemon;
    private readonly ISettingsFileStore _store;
    private readonly Func<string> _settingsPath;
    private readonly Func<bool> _isOwnedDaemon;
    private readonly Action<SettingsSaveState> _onSaveState;

    // Apply-loop hardening (#applyloop): a serialized snapshot of the settings as last loaded from the
    // daemon (the no-op guard: applying byte-identical settings is skipped), and a circuit-breaker that
    // stops a runaway apply↔reload loop from hanging the app (a safety net behind the #433 class of bug).
    private string? _lastLoadedSettingsJson;
    private readonly ApplyLoopBreaker _applyLoopBreaker = new();

    // The last settings we actually got onto DISK, tracked separately from what the daemon last handed
    // back (#734). Conflating the two meant a failed write was recorded as the baseline on the next
    // reload, so re-applying the same settings hit the no-op guard and the save was never retried.
    private string? _lastPersistedSettingsJson;
    // Applied by the daemon but not yet persisted — what RetryPersistAsync would write.
    private Settings? _pendingPersistSettings;
    // ...and the file it was meant for (#787). The destination is resolved late, from the *connected*
    // daemon's AppInfo, so without recording where the change belongs a retry follows whichever daemon
    // is connected when it happens to run — writing one daemon's unsaved settings into another's file.
    private string? _pendingPersistPath;

    // Automatic retries spent on the current pending change (#743). Bounded: the common causes of a
    // refused write are a momentary file lock (OTD's own UX saving the same file), which clears within a
    // reload or two, and a permission problem, which never clears on its own. Retrying the second case
    // forever would log a warning every poll for as long as the app is open, so it stops and leaves the
    // failure standing. Any new edit starts the count again.
    private int _automaticRetries;
    // Generous on purpose. Reloads run on window focus as well as the 30-second poll, and the first
    // attempt is spent immediately by the reload the failing apply itself triggers — a tight budget
    // would be gone before the momentary lock this mostly exists for had cleared. The cap is only here
    // so a permission problem stops warning in the log eventually, not to ration attempts.
    private const int MaxAutomaticRetries = 10;

    private Settings? _settings;

    /// <summary>
    /// One mutating operation at a time (#775). Every path here reads shared state, awaits the daemon,
    /// and then writes that state back — so two overlapping operations interleave around the await and
    /// the one that finishes last owns the disk, regardless of which the user asked for last. That let a
    /// still-running apply write its edit over a restore that had already completed successfully, leaving
    /// the daemon on the default and the file holding the discarded change.
    ///
    /// Serializing rather than superseding, because unlike a data reload (<c>LatestOnlyGate</c>) these
    /// operations are not interchangeable: a queued apply still has to reach the daemon, and dropping it
    /// would lose the user's edit. Order is the guarantee; nothing is skipped.
    ///
    /// Never disposed, deliberately. Nothing here touches <c>AvailableWaitHandle</c>, so there is no
    /// resource to release — and disposing a SemaphoreSlim out from under a waiter is its own hazard
    /// (#767).
    /// </summary>
    private readonly SemaphoreSlim _mutations = new(1, 1);

    /// <summary>Runs <paramref name="operation"/> with no other mutating operation in flight.</summary>
    private async Task<T> SerializedAsync<T>(Func<Task<T>> operation)
    {
        await _mutations.WaitAsync().ConfigureAwait(true);
        try { return await operation().ConfigureAwait(true); }
        finally { _mutations.Release(); }
    }

    /// <param name="daemon">The daemon connection.</param>
    /// <param name="store">The settings file seam.</param>
    /// <param name="settingsPath">Where to persist. Read late: it comes from the daemon's own
    /// <c>AppInfo</c> and is empty until the first data load completes.</param>
    /// <param name="isOwnedDaemon">The #465 gate — only rewrite filters on a daemon OTA positively owns.
    /// Read late because ownership is determined on connect, after this is constructed. Deliberately
    /// phrased as "is ours" rather than "is not theirs": an unidentifiable daemon is neither, and the
    /// negative form let OTA rewrite it (#742).</param>
    /// <param name="onSaveState">Reports save progress; the save chip stays observable state on the session.</param>
    public SettingsCoordinator(IDaemonTransport daemon, ISettingsFileStore store,
        Func<string> settingsPath, Func<bool> isOwnedDaemon, Action<SettingsSaveState> onSaveState)
    {
        _daemon = daemon;
        _store = store;
        _settingsPath = settingsPath;
        _isOwnedDaemon = isOwnedDaemon;
        _onSaveState = onSaveState;
    }

    /// <summary>The settings OTA is editing and would persist — the user's default, never a transient
    /// per-app snapshot.</summary>
    public Settings? CurrentSettings => _settings;

    /// <summary>
    /// True while the daemon is running something other than <see cref="CurrentSettings"/> — a transient
    /// per-app snapshot. The session's reload consults this so a temporary override can't become the
    /// editor's baseline (#737).
    /// </summary>
    public bool HasEphemeralOverride { get; private set; }

    /// <summary>
    /// Takes what the data load just read from the daemon as the new baseline. The caller is responsible
    /// for not calling this while <see cref="HasEphemeralOverride"/> is set — the daemon is then holding
    /// a snapshot, not the baseline.
    /// </summary>
    public void AdoptLoadedSettings(Settings? settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Records the no-op guard's baseline after a load. While an override is live the daemon does NOT
    /// hold <see cref="CurrentSettings"/>, so there is no honest value — null disables the guard rather
    /// than letting it skip an apply on the strength of a comparison against settings the daemon isn't
    /// running (#737).
    /// </summary>
    public void RecordLoadedBaseline()
    {
        _lastLoadedSettingsJson = HasEphemeralOverride ? null : SerializeForCompare(_settings);
    }

    /// <summary>
    /// Forgets everything that belonged to the daemon we were talking to, because a different one is
    /// answering now (#787).
    ///
    /// Almost all of this class's state is a fact about one daemon: the change it accepted but did not
    /// persist, the file that change was for, what its settings file last held, and whether it is running
    /// a transient per-app snapshot. None of that describes the new daemon, and each one misleads a
    /// different part of the app if carried across:
    ///
    /// <list type="bullet">
    /// <item>a pending write would target the wrong file (the retry also refuses this on its own, so the
    /// two guards are independent);</item>
    /// <item>a live override would suppress the load path's <see cref="AdoptLoadedSettings"/>, so the new
    /// daemon's settings would never be read and the editor would keep offering to persist the old
    /// daemon's (#737 gates on exactly that);</item>
    /// <item>a stale persisted baseline would let the no-op guard skip an apply the new daemon has never
    /// seen.</item>
    /// </list>
    ///
    /// <see cref="CurrentSettings"/> is deliberately left alone: the caller reloads immediately after
    /// this, and clearing it would blank the editor for that moment. Clearing the override is what makes
    /// that reload adopt the new daemon's settings.
    /// </summary>
    /// <returns>True when an unsaved change was thrown away, so the caller can say so. Everything else
    /// this drops is bookkeeping the user never knew about; a pending write is an edit they made.</returns>
    public bool ResetForNewDaemon()
    {
        var hadUnsaved = HasUnsavedChange;

        DiscardPendingPersist();
        _lastPersistedSettingsJson = null;
        _lastLoadedSettingsJson = null;
        HasEphemeralOverride = false;

        // The chip was describing the old daemon's unsaved change. It is not the new one's problem, and
        // leaving it would claim a change is live on a daemon that never received it.
        if (hadUnsaved) _onSaveState(SettingsSaveState.None);
        return hadUnsaved;
    }

    /// <summary>Applies to the daemon and persists to disk. Reports what actually happened rather than
    /// collapsing apply and persist into one result (#734). Does NOT reload — the caller does.</summary>
    public Task<SettingsApplyOutcome> ApplyAndSaveAsync(Settings settings) =>
        SerializedAsync(() => ApplyAndSaveCoreAsync(settings));

    private async Task<SettingsApplyOutcome> ApplyAndSaveCoreAsync(Settings settings)
    {
        // (a) No-op guard: applying settings byte-identical to what the daemon last returned is a pure
        // write of unchanged data — skip it. Avoids redundant daemon writes/reloads and neutralizes the
        // common "same value written back repeatedly" loop without a save flicker.
        //
        // It must ALSO be what we last persisted (#734). The baseline is the last daemon-LOADED settings,
        // so after a save failure the reload records the unsaved change as the baseline — retrying the
        // identical settings then returned here and persistence was never retried. Requiring both means
        // an unsaved change always gets another chance at disk.
        if (SerializeForCompare(settings) is { } json
            && json == _lastLoadedSettingsJson
            && json == _lastPersistedSettingsJson)
            return SettingsApplyOutcome.NoChange;

        // (b) Circuit-breaker: if applies are firing faster than any legitimate use, a binding loop is
        // running — skip to break it (no reload → the loop can't re-trigger) instead of hanging the app.
        if (!_applyLoopBreaker.Allow(Environment.TickCount64))
        {
            System.Diagnostics.Debug.WriteLine(
                "SettingsCoordinator: apply-loop breaker tripped — skipping ApplyAndSave to avoid a hang (a UI binding is looping).");
            return SettingsApplyOutcome.Skipped;
        }

        Sanitize(settings);
        _settings = settings;
        // A real apply puts the daemon on these settings, so any per-app override is over (#737).
        HasEphemeralOverride = false;

        // The revision this operation owns, taken BEFORE the daemon call (#774). OTA mutates settings in
        // place — the tablet editor mutates its profile and pushes the same instance — so the caller's
        // object can change while the RPC is pending. Sending and persisting one immutable copy is what
        // makes "what the daemon accepted" and "what went to disk" the same thing by construction.
        // #765 snapshotted only for the retry, and only after the await, so both the immediate write and
        // the retry could still persist an edit the daemon had never seen, including one it refused.
        var revision = Snapshot(settings);
        if (revision == null)
        {
            // Cloning failed, which means serialization failed, which means the disk write is about to
            // fail too. Carry on with the caller's object rather than refusing the user's edit outright:
            // send and persist still agree with each other, they are just no longer isolated.
            AppLog.Warn("Couldn't snapshot the settings being applied; a concurrent edit could change " +
                        "what gets persisted.");
            revision = settings;
        }

        _onSaveState(SettingsSaveState.Saving);
        bool applied;
        try
        {
            // False means there is no transport: the change was NOT sent, so it isn't live and we must
            // not say it is (#734). Previously this returned quietly and we reported success.
            applied = await _daemon.SetSettingsAsync(revision);
        }
        catch (Exception ex)
        {
            // Reachable daemon, failed call. Not live, not saved — a different state from "live but
            // unpersisted", and the UI text must not claim otherwise.
            _onSaveState(SettingsSaveState.ApplyFailed);
            AppLog.Warn("Couldn't apply settings to the daemon.", ex);
            throw; // keep the existing error-propagation contract for callers
        }

        if (!applied)
        {
            _onSaveState(SettingsSaveState.Disconnected);
            AppLog.Warn("Couldn't apply settings: not connected to the daemon.");
            return SettingsApplyOutcome.Disconnected;
        }

        // Persist to disk (same as OTD's own UX Save). Apply and persist are separate outcomes: a failed
        // write means the change is live but won't survive a daemon restart, which we must not hide.
        // An empty settings path is NOT a successful save — there is nowhere to write (#734).
        var path = _settingsPath();
        bool saved = !string.IsNullOrEmpty(path) && _store.TrySave(revision, path);

        // Tracked separately from the daemon-loaded baseline so a persistence-only retry is possible.
        _lastPersistedSettingsJson = saved ? SerializeForCompare(revision) : null;
        // The same revision the daemon accepted, so a retry writes that and not whatever the caller's
        // object has become since (#765, #774).
        _pendingPersistSettings = saved ? null : revision;
        _pendingPersistPath = saved ? null : path;
        // A new change gets its own budget, and may be retried immediately — a spent budget must not
        // silently disable recovery for the rest of the session (#743).
        _automaticRetries = 0;

        if (!saved)
            AppLog.Warn(string.IsNullOrEmpty(path)
                ? "Settings applied but not saved: the daemon reported no settings file path."
                : $"Settings applied but not saved: couldn't write {path}.");

        _onSaveState(saved ? SettingsSaveState.Saved : SettingsSaveState.Failed);
        return saved ? SettingsApplyOutcome.Saved : SettingsApplyOutcome.Unsaved;
    }

    /// <summary>
    /// A change the daemon accepted is still missing from disk — live now, gone on the next daemon
    /// restart. <see cref="RetryPendingPersistAsync"/> is what clears it.
    /// </summary>
    public bool HasUnsavedChange => _pendingPersistSettings != null;

    /// <summary>
    /// Retry a pending disk write, if there is one and the retry budget isn't spent (#743). Called from
    /// the session's reload — which runs on window focus and every 30 seconds — so a write refused
    /// because the file was momentarily locked fixes itself with no user action and the chip goes back
    /// to "Saved". Returns <see cref="SettingsApplyStatus.NoChange"/> when there's nothing to do, so it
    /// is free to call on every load.
    /// </summary>
    public Task<SettingsApplyOutcome> RetryPendingPersistAsync() => SerializedAsync(() =>
    {
        if (!HasUnsavedChange || _automaticRetries >= MaxAutomaticRetries)
            return Task.FromResult(SettingsApplyOutcome.NoChange);

        _automaticRetries++;
        return RetryPersistCoreAsync();
    });

    /// <summary>
    /// Retries the disk write for settings the daemon already accepted but that failed to persist (#734).
    /// No daemon write and no reload — the change is already live; only the file is behind.
    /// </summary>
    public Task<SettingsApplyOutcome> RetryPersistAsync() => SerializedAsync(RetryPersistCoreAsync);

    private Task<SettingsApplyOutcome> RetryPersistCoreAsync()
    {
        if (_pendingPersistSettings is not { } pending)
            return Task.FromResult(SettingsApplyOutcome.NoChange);
        var path = _settingsPath();
        if (string.IsNullOrEmpty(path))
            return Task.FromResult(SettingsApplyOutcome.Unsaved);

        // The destination moved, which means a different daemon answered since this change was made
        // (#787). Writing here would put one daemon's settings into another's file — a file OTA was
        // never asked to touch, belonging to an install the user may share with OTD's own UX. Drop the
        // change instead: losing an edit the disk already refused is bad, silently overwriting someone
        // else's configuration is worse.
        if (_pendingPersistPath is { } origin && !ExecutablePath.SameFile(origin, path))
        {
            AppLog.Warn($"Discarding an unsaved settings change made for {origin}: the connected daemon " +
                        $"now uses {path}, and the change does not belong to it.");
            DiscardPendingPersist();
            _onSaveState(SettingsSaveState.None);
            return Task.FromResult(SettingsApplyOutcome.NoChange);
        }

        _onSaveState(SettingsSaveState.Saving);
        bool saved = _store.TrySave(pending, path);
        if (saved)
        {
            _lastPersistedSettingsJson = SerializeForCompare(pending);
            _pendingPersistSettings = null;
        }
        _onSaveState(saved ? SettingsSaveState.Saved : SettingsSaveState.Failed);
        return Task.FromResult(saved ? SettingsApplyOutcome.Saved : SettingsApplyOutcome.Unsaved);
    }

    /// <summary>Applies live without persisting — a temporary override (profile switching, #320). The
    /// saved <c>settings.json</c> default is untouched. Does NOT reload; the caller does.</summary>
    public Task<bool> ApplyLiveOnlyAsync(Settings settings) =>
        SerializedAsync(() => ApplyLiveOnlyCoreAsync(settings));

    private async Task<bool> ApplyLiveOnlyCoreAsync(Settings settings)
    {
        ProfileFilterMaintenance.CleanLegacyFilters(settings);
        if (_isOwnedDaemon()) ProfileFilterMaintenance.DisableUnapprovedFilters(settings); // #465/#742

        // Report whether it landed (#766). False means no transport — the change was never sent, so a
        // caller must not announce a switch that didn't happen. State moves only on success.
        if (!await _daemon.SetSettingsAsync(settings))
        {
            AppLog.Warn("Couldn't apply the live-only settings: not connected to the daemon.");
            return false;
        }

        _settings = settings;
        HasEphemeralOverride = false;   // the daemon is on _settings again (#737)
        return true;
    }

    /// <summary>
    /// Applies to the daemon ONLY — no disk save, no reload, and (unlike <see cref="ApplyLiveOnlyAsync"/>)
    /// no change to <see cref="CurrentSettings"/>. For automatic per-app switching (#167): the editor keeps
    /// showing and persisting the user's default while the daemon runs a transient snapshot.
    /// </summary>
    public Task<bool> ApplyEphemeralAsync(Settings settings) =>
        SerializedAsync(() => ApplyEphemeralCoreAsync(settings));

    private async Task<bool> ApplyEphemeralCoreAsync(Settings settings)
    {
        ProfileFilterMaintenance.CleanLegacyFilters(settings);
        if (_isOwnedDaemon()) ProfileFilterMaintenance.DisableUnapprovedFilters(settings); // #465/#742

        // An override that never reached the daemon is not an override (#766). Setting the flag anyway
        // would suppress the reload's settings read on the strength of one that does not exist.
        if (!await _daemon.SetSettingsAsync(settings))
        {
            AppLog.Warn("Couldn't apply the per-app snapshot: not connected to the daemon.");
            return false;
        }

        // Flag it so the reload stops overwriting the baseline with what the daemon now holds (#737).
        // Leaving _settings untouched here was never enough on its own: the 30-second poll read the
        // daemon back into it, so a transient snapshot silently became the editor's baseline and the
        // source a "restore default" would restore from.
        HasEphemeralOverride = true;
        return true;
    }

    /// <summary>Puts the daemon back on <see cref="CurrentSettings"/>, ending any ephemeral override.</summary>
    public Task<bool> ClearEphemeralOverrideAsync() => SerializedAsync(ClearEphemeralOverrideCoreAsync);

    private async Task<bool> ClearEphemeralOverrideCoreAsync()
    {
        if (_settings is not { } baseline)
        {
            // Nothing to return to, so nothing is overriding anything.
            HasEphemeralOverride = false;
            return true;
        }

        // The override is only over once the daemon is back on the baseline (#766). Clearing the flag on
        // a failed write would leave the tablet on the snapshot while the reload resumed treating the
        // daemon as authoritative — adopting that snapshot as the editor's default, which is exactly
        // what #737 fixed.
        if (!await _daemon.SetSettingsAsync(baseline))
        {
            AppLog.Warn("Couldn't end the per-app override: not connected to the daemon. " +
                        "The override is still in effect.");
            return false;
        }

        HasEphemeralOverride = false;
        return true;
    }

    /// <summary>
    /// Re-reads the saved default from disk (untouched by a live-only override) and applies it. Every way
    /// this can fall short has its own outcome (#734): it used to finish silently when the default
    /// couldn't be loaded, and the caller cleared the override indicator and announced a restoration that
    /// never happened — while the override was still running. Does NOT reload; the caller does.
    /// </summary>
    public Task<SettingsRestoreOutcome> RestoreDefaultAsync() => SerializedAsync(RestoreDefaultCoreAsync);

    private async Task<SettingsRestoreOutcome> RestoreDefaultCoreAsync()
    {
        var path = _settingsPath();
        if (string.IsNullOrEmpty(path) || !_store.TryLoad(path, out var def) || def == null)
        {
            AppLog.Warn("Couldn't restore the saved default: no readable settings file. " +
                        "Any active override is still in effect.");
            return SettingsRestoreOutcome.SourceUnavailable;
        }

        bool applied;
        try
        {
            applied = await _daemon.SetSettingsAsync(def);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Couldn't restore the saved default: the daemon rejected it. " +
                        "Any active override is still in effect.", ex);
            return SettingsRestoreOutcome.Failed(ex);
        }

        if (!applied)
        {
            AppLog.Warn("Couldn't restore the saved default: not connected to the daemon. " +
                        "Any active override is still in effect.");
            return SettingsRestoreOutcome.Disconnected;
        }

        _settings = def;
        HasEphemeralOverride = false;   // restored to the saved default; no override remains (#737)

        // Drop any pending save (#764). A pending save is an edit the daemon took but the disk refused,
        // and restoring the default is the user discarding exactly that. Left in place, the next reload's
        // retry writes it back over the default that was just restored — disk and daemon then disagree,
        // and the next restart resolves it in favour of the edit the user got rid of.
        DiscardPendingPersist();
        _lastPersistedSettingsJson = SerializeForCompare(def);

        // Nothing is outstanding any more, so stop saying otherwise (#776). A failed save followed by a
        // successful restore used to leave the chip reading "Couldn't save — your change is live but
        // won't survive a restart" about a change the user had just deliberately discarded.
        //
        // None rather than Saved: restoring reads the default off disk and applies it, so no save
        // happened, and reporting one would be a smaller version of the same lie. The failed paths above
        // all return early and leave their own state standing.
        _onSaveState(SettingsSaveState.None);
        return SettingsRestoreOutcome.Restored;
    }

    /// <summary>
    /// Repairs applied to every settings object before it reaches the daemon or disk. These run on the
    /// way OUT rather than on load because the shared <c>settings.json</c> is also OTD's own UX's file —
    /// a malformed profile we write would crash their UI, not just ours.
    /// </summary>
    private void Sanitize(Settings settings)
    {
        // Forward guard: never write back a stale/duplicate filter store (e.g. left by a rename).
        ProfileFilterMaintenance.CleanLegacyFilters(settings);
        if (_isOwnedDaemon()) ProfileFilterMaintenance.DisableUnapprovedFilters(settings); // #465/#742: keep only approved filters enabled

        // Never persist a profile with null Absolute-mode areas: the OpenTabletDriver UX does
        // `p.AbsoluteModeSettings.Tablet.Width` on save and would NRE + crash. Repair (fill nulls) so the
        // shared settings.json stays valid for OTD's own UI too (#otd-null-areas).
        int repairedProfiles = ProfileSanitizer.EnsureValidAbsoluteAreas(settings);
        if (repairedProfiles > 0)
            AppLog.Warn($"Repaired {repairedProfiles} profile(s) with missing Absolute-mode areas before saving " +
                        "(would otherwise crash the OpenTabletDriver UX).");
    }

    /// <summary>Forget the pending save and its retry budget — nothing is outstanding.</summary>
    private void DiscardPendingPersist()
    {
        _pendingPersistSettings = null;
        _pendingPersistPath = null;
        _automaticRetries = 0;
    }

    /// <summary>
    /// An independent copy, so later mutation of the caller's object cannot reach what we hold. Round-trips
    /// through the same serializer the equality guard uses, so a settings object that can be compared can
    /// also be snapshotted. Returns null if it can't be cloned, which simply means there is nothing to
    /// retry — better than retrying something that has since changed underneath us.
    /// </summary>
    private static Settings? Snapshot(Settings settings)
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(json);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Couldn't snapshot settings for a persistence retry; the retry is skipped.", ex);
            return null;
        }
    }

    /// <summary>Deterministic string form of the settings for cheap equality comparison (the no-op apply
    /// guard). Best-effort — returns null on any serialization failure, which just disables the guard for
    /// that call (the circuit-breaker still backs it up).</summary>
    private static string? SerializeForCompare(Settings? settings)
    {
        if (settings == null) return null;
        try { return Newtonsoft.Json.JsonConvert.SerializeObject(settings); }
        catch { return null; }
    }
}
