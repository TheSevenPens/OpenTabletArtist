using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// Exclusive control over OpenTabletDriver settings for one daemon connection. The host asks for a
/// change; this decides how to carry it out safely and reports what actually happened.
/// </summary>
///
/// <remarks>
/// <b>Nothing implements this yet.</b> It is the contract the settings authority is being moved towards,
/// not a description of what is in place: <c>SettingsCoordinator</c> carries out these operations today
/// and differs from it in shape — outcomes where this returns <c>bool</c>, and cancellation this accepts
/// that it does not act on.
///
/// Read what follows as the target, and do not take the guarantees below as established. In particular
/// the execution and cancellation rules describe what an implementation must provide; being headless
/// does not by itself establish them.
/// </remarks>
///
/// <remarks>
/// <para>
/// Every settings change goes through here. That is the point of the type: applying settings is not one
/// action but several that fail independently — sending to the daemon, writing to disk, and doing both
/// against a daemon that may be replaced part-way through — and a host that reaches past this to the
/// transport or the file gets none of the protections below.
/// </para>
///
/// <para><b>Ownership.</b></para>
/// <list type="bullet">
/// <item>A <see cref="Settings"/> passed in stays the caller's. It is copied on admission and is never
/// modified, retained, sent, or written. Callers may keep editing their own object immediately after a
/// call returns its task — later edits belong to a later request, not this one.</item>
/// <item>A <see cref="Settings"/> handed back is detached. Nothing else references it, so holding or
/// editing it cannot affect this session.</item>
/// <item>The one demand on the caller: do not mutate the object from another thread <em>during</em> the
/// call that admits it. Copying cannot defend against a concurrent writer.</item>
/// <item>If the copy cannot be made, the operation fails and nothing is sent or written. It does not
/// fall back to the caller's object — that would quietly withdraw the guarantee at the moment it is
/// most needed, since a failure to copy usually means a failure to serialize is coming next.</item>
/// </list>
///
/// <para><b>Sessions.</b> Users run more than one OpenTabletDriver build and switch between them while
/// the host is open, so a connection can be replaced at any point in an operation's life.</para>
/// <list type="bullet">
/// <item>Every operation takes a <see cref="SettingsStamp"/> when it is <b>admitted</b> — before it waits
/// for anything, because waiting is exactly when the daemon can change underneath it.</item>
/// <item>Invalidation takes effect immediately. It never queues behind operations belonging to a
/// connection that has gone; if it did, that work would run first, which is the thing being prevented.
/// </item>
/// <item>Work that is still queued when its session ends is discarded before it is sent.</item>
/// <item>A result that arrives after its session has ended is not written to disk, not published as
/// state, and not reported as success. It comes back as
/// <see cref="SettingsApplyStatus.Superseded"/>.</item>
/// <item>What cannot be undone is not pretended away: a request already sent may well have been acted
/// on. Supersession describes what this session did with the result, not a promise that the old daemon
/// never saw it.</item>
/// </list>
///
/// <para><b>Execution.</b> Implementations are safe to call from any thread and do not require a
/// synchronization context. Do not read that as a licence to call concurrently and hope: operations are
/// ordered, but a host that issues contradictory changes at once gets whichever order they were
/// admitted in. Completions arrive on whatever thread finished the work, so a host with thread affinity
/// — a UI, for instance — marshals them itself. That is deliberate: this library has no way to know what
/// the host's affinity is, and guessing wrongly is worse than leaving it to the caller.</para>
///
/// <para><b>Cancellation.</b> A token stops work that has not left the process. Once a request has been
/// sent, cancelling stops this session waiting for the answer; it does not retract the request, and the
/// daemon may apply it regardless. An operation cancelled after sending therefore reports an uncertain
/// result rather than claiming nothing happened. Cancellation during a disk write does not interrupt the
/// write: a half-written settings file is worse than a slow one.</para>
///
/// <para><b>Failure is reported, not implied.</b> A completed task means the operation finished, not that
/// it worked. Read the outcome. Applying and persisting fail independently, and a change that is live but
/// unwritten will be lost the next time the daemon restarts — which the user needs to be told.</para>
/// </remarks>
public interface IOtdSettingsSession
{
    /// <summary>
    /// The settings this session considers current — the user's own, never a temporary override.
    /// </summary>
    /// <returns>
    /// A detached copy with the stamp it belongs to, or null before anything has been loaded. Each call
    /// copies, so callers that need it repeatedly should hold the result rather than re-reading.
    /// </returns>
    PreparedSettings? GetCurrent();

    /// <summary>
    /// True while the daemon is running something other than <see cref="GetCurrent"/> — a temporary
    /// override applied without changing what the user is editing or what is saved.
    /// </summary>
    bool HasEphemeralOverride { get; }

    /// <summary>Applies to the daemon and writes to disk.</summary>
    /// <param name="requested">The caller's settings. Copied on admission; not modified or retained.</param>
    /// <param name="ct">Stops work that has not been sent yet. See the cancellation note on the interface.</param>
    /// <returns>What happened, distinguishing applied-and-saved from applied-but-unsaved.</returns>
    Task<SettingsApplyOutcome> ApplyAndSaveAsync(Settings requested, CancellationToken ct = default);

    /// <summary>
    /// Writes an earlier change that the daemon accepted but the disk refused, to the file it was
    /// originally meant for.
    /// </summary>
    /// <param name="ct">Stops the retry before it begins.</param>
    /// <returns>
    /// The result of the write, or <see cref="SettingsApplyStatus.NoChange"/> when nothing is pending.
    /// </returns>
    Task<SettingsApplyOutcome> RetryPersistAsync(CancellationToken ct = default);

    /// <summary>
    /// Applies to the daemon without writing to disk, and treats the result as what the user is now
    /// editing. The saved default is untouched, so a restart returns to it.
    /// </summary>
    /// <param name="requested">The caller's settings. Copied on admission; not modified or retained.</param>
    /// <param name="ct">Stops work that has not been sent yet.</param>
    /// <returns>What happened. Only a successful apply moves this session's state.</returns>
    Task<SettingsApplyOutcome> ApplyLiveOnlyAsync(Settings requested, CancellationToken ct = default);

    /// <summary>
    /// Applies to the daemon only: no write, and no change to what the user is editing. For a transient
    /// override the host manages, where the editor must go on showing and saving the user's own settings.
    /// </summary>
    /// <param name="requested">The caller's settings. Copied on admission; not modified or retained.</param>
    /// <param name="ct">Stops work that has not been sent yet.</param>
    /// <returns>
    /// What happened. An override that never reached the daemon is not an override, and is not recorded
    /// as one.
    /// </returns>
    Task<SettingsApplyOutcome> ApplyEphemeralAsync(Settings requested, CancellationToken ct = default);

    /// <summary>Puts the daemon back on <see cref="GetCurrent"/>, ending any temporary override.</summary>
    /// <param name="ct">Stops work that has not been sent yet.</param>
    /// <returns>
    /// What happened. The override is over only once the daemon has taken the settings back; until then
    /// the tablet is still running it, and callers must not clear an indicator saying so.
    /// </returns>
    Task<SettingsApplyOutcome> ClearEphemeralOverrideAsync(CancellationToken ct = default);

    /// <summary>Re-reads the saved default from disk and applies it, discarding any override.</summary>
    /// <param name="ct">Stops work that has not been sent yet.</param>
    /// <returns>
    /// What happened. Every way this can fall short has its own status, because a restore that did not
    /// happen leaves the override running — and telling the user their tablet is back to normal when it
    /// is not is the failure this distinguishes.
    /// </returns>
    Task<SettingsRestoreOutcome> RestoreDefaultAsync(CancellationToken ct = default);
}
