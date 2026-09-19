using System;

namespace OtdInterop;

/// <summary>
/// What actually happened when settings were applied. Applying to the daemon and persisting to
/// disk are two operations that fail independently, and "restore" is a third — previously all three
/// collapsed into a single <c>Task</c>, so the UI could report a change as live when it was never sent,
/// or as saved when the write was refused.
/// </summary>
public enum SettingsApplyStatus
{
    /// <summary>The daemon accepted the change and it reached disk. The only fully-good outcome.</summary>
    AppliedAndSaved,

    /// <summary>The daemon accepted the change, but persisting it failed — it is live now and will be
    /// lost on the next daemon restart. Worth retrying the save alone.</summary>
    AppliedNotSaved,

    /// <summary>No daemon transport, so nothing was applied and nothing was saved. Distinct from a
    /// failure: there is nothing wrong except that we aren't connected.</summary>
    Disconnected,

    /// <summary>The daemon was reachable but rejected or failed the change. Not live, not saved.</summary>
    ApplyFailed,

    /// <summary>The settings were already live AND already persisted, so there was nothing to do.</summary>
    NoChange,

    /// <summary>The apply-loop circuit breaker tripped — a UI binding is looping. Deliberately skipped
    /// to keep the app responsive; not a user-visible failure.</summary>
    Skipped,

    /// <summary>
    /// The daemon changed while this operation was queued or in flight, so it belongs to a session that
    /// has ended (#803). Nothing was written to the new daemon and nothing was written to disk.
    ///
    /// Distinct from every other status because none of them is true of it: it isn't live (the daemon it
    /// was for is gone), it isn't a failure (nothing went wrong), and it isn't <see cref="NoChange"/>
    /// (there was a change; it simply no longer has a destination).
    /// </summary>
    Superseded,
}

/// <summary>The result of an apply: what happened, the failure if there was one, and what was actually
/// worked with.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Error">The exception behind <see cref="SettingsApplyStatus.ApplyFailed"/>, if any.</param>
/// <param name="Prepared">
/// The request after policy and repairs, detached — see <see cref="PreparedSettings"/>. Present whenever
/// preparation got far enough to produce one, including on failure: knowing what <em>would</em> have been
/// sent is exactly what a caller needs in order to explain a failure or retry it.
///
/// Null when the operation ended before preparing anything, which is the case for a no-op, a superseded
/// request, and a preparation failure.
///
/// A non-null value is not evidence that anything was applied. <see cref="Status"/> is the only member
/// entitled to say that.
/// </param>
public readonly record struct SettingsApplyOutcome(
    SettingsApplyStatus Status,
    Exception? Error = null,
    PreparedSettings? Prepared = null)
{
    /// <summary>Live on the daemon and written to disk.</summary>
    public static readonly SettingsApplyOutcome Saved = new(SettingsApplyStatus.AppliedAndSaved);
    /// <summary>Already live and already persisted; nothing was sent.</summary>
    public static readonly SettingsApplyOutcome NoChange = new(SettingsApplyStatus.NoChange);
    /// <summary>Live on the daemon, but the write failed — it will not survive a daemon restart.</summary>
    public static readonly SettingsApplyOutcome Unsaved = new(SettingsApplyStatus.AppliedNotSaved);
    /// <summary>No transport, so nothing was sent and nothing was written.</summary>
    public static readonly SettingsApplyOutcome Disconnected = new(SettingsApplyStatus.Disconnected);
    /// <summary>The apply-loop circuit breaker tripped; deliberately not sent.</summary>
    public static readonly SettingsApplyOutcome Skipped = new(SettingsApplyStatus.Skipped);
    /// <summary>The daemon changed while this was queued or in flight; it belongs to a session that has ended.</summary>
    public static readonly SettingsApplyOutcome Superseded = new(SettingsApplyStatus.Superseded);
    /// <summary>The daemon was reachable but the change failed. Not live, not saved.</summary>
    /// <param name="ex">The failure, when one was thrown.</param>
    public static SettingsApplyOutcome Failed(Exception? ex) => new(SettingsApplyStatus.ApplyFailed, ex);

    /// <summary>The daemon is running these settings now. <see cref="SettingsApplyStatus.NoChange"/>
    /// counts — it already was.</summary>
    public bool IsLive => Status is SettingsApplyStatus.AppliedAndSaved
        or SettingsApplyStatus.AppliedNotSaved or SettingsApplyStatus.NoChange;

    /// <summary>
    /// Something was actually written to the daemon, so there is new state worth reading back.
    /// Deliberately narrower than <see cref="IsLive"/>, which includes <see cref="SettingsApplyStatus.NoChange"/>:
    /// reloading after a no-change apply re-arms the apply → reload → binding-write-back loop the no-op
    /// guard exists to break, and the guard returns before the circuit breaker that would otherwise catch
    /// it (#763). Use this to decide whether to reload; use <see cref="IsLive"/> to describe state.
    /// </summary>
    public bool ChangedTheDaemon => Status is SettingsApplyStatus.AppliedAndSaved
        or SettingsApplyStatus.AppliedNotSaved;

    /// <summary>These settings will survive a restart.</summary>
    public bool IsPersisted => Status is SettingsApplyStatus.AppliedAndSaved or SettingsApplyStatus.NoChange;

    /// <summary>Applied but not persisted — the one state where retrying the save alone is worthwhile.</summary>
    public bool NeedsPersistRetry => Status == SettingsApplyStatus.AppliedNotSaved;
}

/// <summary>What happened when reverting the daemon to the saved on-disk default (#734).</summary>
public enum SettingsRestoreStatus
{
    /// <summary>The saved default was read from disk and applied. Any override is genuinely gone.</summary>
    Restored,

    /// <summary>The saved default couldn't be read — no settings path, missing file, or unparseable.
    /// Nothing was applied, so an override that was active is <b>still active</b>. Callers must not
    /// clear an override indicator on this.</summary>
    SourceUnavailable,

    /// <summary>The default was read but the daemon failed to take it. The override is still active.</summary>
    ApplyFailed,

    /// <summary>No daemon transport. Nothing was applied.</summary>
    Disconnected,

    /// <summary>The daemon changed while this was queued or in flight (#803). Nothing was applied, and
    /// the override it would have cleared belonged to the daemon that has gone.</summary>
    Superseded,
}

/// <summary>The result of a restore: what happened, the failure if there was one, and what was read.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Error">The exception behind <see cref="SettingsRestoreStatus.ApplyFailed"/>, if any.</param>
/// <param name="Prepared">
/// The saved default that was read from disk, detached. Null when it could not be read, which is what
/// <see cref="SettingsRestoreStatus.SourceUnavailable"/> reports.
/// </param>
public readonly record struct SettingsRestoreOutcome(
    SettingsRestoreStatus Status,
    Exception? Error = null,
    PreparedSettings? Prepared = null)
{
    /// <summary>The saved default was read and applied; any override is genuinely over.</summary>
    public static readonly SettingsRestoreOutcome Restored = new(SettingsRestoreStatus.Restored);
    /// <summary>The saved default could not be read, so nothing was applied and an override is still active.</summary>
    public static readonly SettingsRestoreOutcome SourceUnavailable = new(SettingsRestoreStatus.SourceUnavailable);
    /// <summary>No transport. Nothing was applied.</summary>
    public static readonly SettingsRestoreOutcome Disconnected = new(SettingsRestoreStatus.Disconnected);
    /// <summary>The daemon changed while this was queued or in flight.</summary>
    public static readonly SettingsRestoreOutcome Superseded = new(SettingsRestoreStatus.Superseded);
    /// <summary>The default was read but the daemon would not take it. The override is still active.</summary>
    /// <param name="ex">The failure, when one was thrown.</param>
    public static SettingsRestoreOutcome Failed(Exception? ex) => new(SettingsRestoreStatus.ApplyFailed, ex);

    /// <summary>The daemon is on the saved default now. Only then may an override indicator clear.</summary>
    public bool IsRestored => Status == SettingsRestoreStatus.Restored;
}
