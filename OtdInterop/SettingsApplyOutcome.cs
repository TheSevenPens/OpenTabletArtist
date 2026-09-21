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

    /// <summary>
    /// The daemon accepted the change and nothing was written, because nothing was meant to be.
    ///
    /// Live-only and per-app applies deliberately leave the saved default alone: the point is a
    /// temporary override the user's own settings survive. Distinct from
    /// <see cref="AppliedNotSaved"/>, which describes a write that was wanted and failed — conflating
    /// them would make a deliberate override look like something to retry, and make a failed save look
    /// like a choice.
    /// </summary>
    AppliedLive,

    /// <summary>No daemon transport, so nothing was applied and nothing was saved. Distinct from a
    /// failure: there is nothing wrong except that we aren't connected.</summary>
    Disconnected,

    /// <summary>The daemon was reachable but rejected or failed the change. Not live, not saved.</summary>
    ApplyFailed,

    /// <summary>
    /// The request needed nothing done. Ordinarily that means the settings were already live and already
    /// persisted, which is what the no-op guard detects.
    ///
    /// One caller uses it for a weaker thing: ending an override when no settings have ever been loaded.
    /// There is nothing to put the daemon back on and nothing to say about disk, so read
    /// <see cref="SettingsApplyOutcome.IsPersisted"/> only on results from an operation that was asked to
    /// persist something.
    /// </summary>
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

    /// <summary>
    /// Someone else changed the daemon's settings since this session last read them, so nothing was sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a failure and not <see cref="NoChange"/>: the change was wanted, it is well-formed, and it
    /// would have gone through. What stopped it is that sending it would have overwritten an edit made
    /// somewhere else — OpenTabletDriver's own UX, or a helper — because <c>SetSettings</c> replaces
    /// the whole object and keeps no version (#491, docs/design/settings-sync.md).
    /// </para>
    /// <para>
    /// The caller's change is still theirs: not applied, not saved, and not discarded. What to do next is
    /// a decision only they can make, because reloading drops their edit and applying anyway drops the
    /// other one.
    /// </para>
    /// </remarks>
    ChangedElsewhere,

    /// <summary>
    /// The daemon could not say what it holds, so nothing was sent rather than written over an answer
    /// nobody has (#905).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ChangedElsewhere"/>, which is a conflict this session observed. This is
    /// the absence of an observation: the read failed, timed out, or came back empty. Writing anyway
    /// would abandon the protection exactly where the state is least certain, so the change is held on
    /// the same terms and the caller is told which of the two happened.
    /// </remarks>
    CouldNotCheck,
}

/// <summary>The result of an apply: what happened, the failure if there was one, and what was actually
/// worked with.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Error">The exception behind <see cref="SettingsApplyStatus.ApplyFailed"/>, if any.</param>
/// <param name="Prepared">
/// The revision this operation published, after policy and repairs, detached — see
/// <see cref="PreparedSettings"/>. This is what the caller may adopt as the settings it is now editing.
///
/// Null when the operation published no revision. That covers the obvious cases — a no-op, a superseded
/// request, a preparation failure — and one deliberate one: a transient per-app override changes the
/// daemon without changing what the session publishes, so there is no revision it could honestly be
/// stamped as, and nothing is handed back. A caller cannot mistake an override for the settings to save
/// because it is never given the chance to.
///
/// A non-null value is not evidence that anything was applied. <see cref="Status"/> is the only member
/// entitled to say that.
/// </param>
/// <param name="Conflict">
/// What was seen instead, when <see cref="SettingsApplyStatus.ChangedElsewhere"/> held the change — and
/// the thing to present back to authorise overwriting it (#906). Null for every other status, including
/// <see cref="SettingsApplyStatus.CouldNotCheck"/>: nothing was observed there, so there is nothing to
/// consent to.
/// </param>
/// <param name="Held">
/// What this change was weighed against when it was held, to present with it if it is submitted again
/// (#906). Null for every status that is not held. Distinct from <paramref name="Conflict"/>: that says
/// what the daemon had <em>instead</em> and authorises replacing it; this says what the draft was
/// compared <em>with</em>, and keeps that comparison from drifting.
/// </param>
public readonly record struct SettingsApplyOutcome(
    SettingsApplyStatus Status,
    Exception? Error = null,
    PreparedSettings? Prepared = null,
    SettingsConflict? Conflict = null,
    SettingsHold? Held = null)
{
    /// <summary>Live on the daemon and written to disk.</summary>
    public static readonly SettingsApplyOutcome Saved = new(SettingsApplyStatus.AppliedAndSaved);
    /// <summary>Already live and already persisted; nothing was sent.</summary>
    public static readonly SettingsApplyOutcome NoChange = new(SettingsApplyStatus.NoChange);
    /// <summary>Live on the daemon, but the write failed — it will not survive a daemon restart.</summary>
    public static readonly SettingsApplyOutcome Unsaved = new(SettingsApplyStatus.AppliedNotSaved);
    /// <summary>No transport, so nothing was sent and nothing was written.</summary>
    public static readonly SettingsApplyOutcome Disconnected = new(SettingsApplyStatus.Disconnected);
    /// <summary>Live on the daemon; nothing was written, and nothing was meant to be.</summary>
    public static readonly SettingsApplyOutcome Live = new(SettingsApplyStatus.AppliedLive);
    /// <summary>The apply-loop circuit breaker tripped; deliberately not sent.</summary>
    public static readonly SettingsApplyOutcome Skipped = new(SettingsApplyStatus.Skipped);
    /// <summary>The daemon changed while this was queued or in flight; it belongs to a session that has ended.</summary>
    public static readonly SettingsApplyOutcome Superseded = new(SettingsApplyStatus.Superseded);
    /// <summary>Nothing was sent: the daemon's settings moved under this session (#491).</summary>
    public static readonly SettingsApplyOutcome ChangedElsewhere =
        new(SettingsApplyStatus.ChangedElsewhere);
    /// <summary>Nothing was sent: the daemon never said what it holds (#905).</summary>
    public static readonly SettingsApplyOutcome CouldNotCheck =
        new(SettingsApplyStatus.CouldNotCheck);
    /// <summary>The daemon was reachable but the change failed. Not live, not saved.</summary>
    /// <param name="ex">The failure, when one was thrown.</param>
    public static SettingsApplyOutcome Failed(Exception? ex) => new(SettingsApplyStatus.ApplyFailed, ex);

    /// <summary>The daemon is running these settings now. <see cref="SettingsApplyStatus.NoChange"/>
    /// counts — it already was.</summary>
    public bool IsLive => Status is SettingsApplyStatus.AppliedAndSaved
        or SettingsApplyStatus.AppliedNotSaved or SettingsApplyStatus.AppliedLive
        or SettingsApplyStatus.NoChange;

    /// <summary>
    /// Something was actually written to the daemon, so there is new state worth reading back.
    /// Deliberately narrower than <see cref="IsLive"/>, which includes <see cref="SettingsApplyStatus.NoChange"/>:
    /// reloading after a no-change apply re-arms the apply → reload → binding-write-back loop the no-op
    /// guard exists to break, and the guard returns before the circuit breaker that would otherwise catch
    /// it (#763). Use this to decide whether to reload; use <see cref="IsLive"/> to describe state.
    /// </summary>
    public bool ChangedTheDaemon => Status is SettingsApplyStatus.AppliedAndSaved
        or SettingsApplyStatus.AppliedNotSaved or SettingsApplyStatus.AppliedLive;

    /// <summary>
    /// These settings will survive a restart. Meaningful only on a result from an operation that was
    /// asked to persist — <see cref="SettingsApplyStatus.NoChange"/> also comes back from operations that
    /// never write, and says nothing about disk there.
    /// </summary>
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
