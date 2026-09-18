using System;

namespace OpenTabletArtist.Domain;

/// <summary>
/// What actually happened when settings were applied (#734). Applying to the daemon and persisting to
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
}

/// <summary>The result of an apply, with the failure attached when there was one.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Error">The exception behind <see cref="SettingsApplyStatus.ApplyFailed"/>, if any.</param>
public readonly record struct SettingsApplyOutcome(SettingsApplyStatus Status, Exception? Error = null)
{
    public static readonly SettingsApplyOutcome Saved = new(SettingsApplyStatus.AppliedAndSaved);
    public static readonly SettingsApplyOutcome NoChange = new(SettingsApplyStatus.NoChange);
    public static readonly SettingsApplyOutcome Unsaved = new(SettingsApplyStatus.AppliedNotSaved);
    public static readonly SettingsApplyOutcome Disconnected = new(SettingsApplyStatus.Disconnected);
    public static readonly SettingsApplyOutcome Skipped = new(SettingsApplyStatus.Skipped);
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
}

/// <summary>The result of a restore, with the failure attached when there was one.</summary>
public readonly record struct SettingsRestoreOutcome(SettingsRestoreStatus Status, Exception? Error = null)
{
    public static readonly SettingsRestoreOutcome Restored = new(SettingsRestoreStatus.Restored);
    public static readonly SettingsRestoreOutcome SourceUnavailable = new(SettingsRestoreStatus.SourceUnavailable);
    public static readonly SettingsRestoreOutcome Disconnected = new(SettingsRestoreStatus.Disconnected);
    public static SettingsRestoreOutcome Failed(Exception? ex) => new(SettingsRestoreStatus.ApplyFailed, ex);

    /// <summary>The daemon is on the saved default now. Only then may an override indicator clear.</summary>
    public bool IsRestored => Status == SettingsRestoreStatus.Restored;
}
