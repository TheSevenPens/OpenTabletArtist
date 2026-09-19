using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// Exclusive control over OpenTabletDriver settings for one daemon connection. The host asks for a
/// change; this decides how to carry it out safely and reports what actually happened.
/// </summary>
///
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
/// <para><b>Execution — what is actually guaranteed today.</b> Mutating operations are serialized
/// against each other, and work belonging to a daemon that has gone is rejected rather than run. That is
/// the extent of it.</para>
///
/// <para>What is <em>not</em> guaranteed: this is not safe to call concurrently from arbitrary threads.
/// Several members read and write session state outside that serialization, and the implementation
/// continues on whatever context its awaits resume on. In the only host that exists it is called from a
/// single UI thread, and that confinement — not any internal locking — is what makes it safe there.
/// Being headless does not establish otherwise; a library merely free of UI types is not thereby
/// thread-safe.</para>
///
/// <para>Completions arrive on whatever thread finished the work, so a host with thread affinity
/// marshals them itself. That part is deliberate: this library cannot know what the host's affinity is,
/// and guessing wrongly is worse than leaving it to the caller.</para>
///
/// <para><b>Cancellation is not offered.</b> An earlier draft of this contract took a token on every
/// operation. Nothing implemented it and no caller passed one, and a token that is accepted and ignored
/// is worse than none: it reads as a guarantee. It was removed rather than faked.
///
/// The reason it is not trivial to add, for whoever does: once a request has been sent, cancelling can
/// only stop this session waiting for the answer. It cannot retract the request, and the daemon may
/// apply it regardless — so such an operation would have to report an uncertain result rather than claim
/// nothing happened, and no status says that today. Cancelling a disk write is worse still, since a
/// half-written settings file is the thing atomic writes exist to prevent.</para>
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
    /// <returns>What happened, distinguishing applied-and-saved from applied-but-unsaved.</returns>
    Task<SettingsApplyOutcome> ApplyAndSaveAsync(Settings requested);

    /// <summary>
    /// Writes an earlier change that the daemon accepted but the disk refused, to the file it was
    /// originally meant for.
    /// </summary>
    /// <returns>
    /// The result of the write, or <see cref="SettingsApplyStatus.NoChange"/> when nothing is pending.
    /// </returns>
    Task<SettingsApplyOutcome> RetryPersistAsync();

    /// <summary>
    /// Applies to the daemon without writing to disk, and treats the result as what the user is now
    /// editing. The saved default is untouched, so a restart returns to it.
    /// </summary>
    /// <param name="requested">The caller's settings. Copied on admission; not modified or retained.</param>
    /// <returns>What happened. Only a successful apply moves this session's state.</returns>
    Task<SettingsApplyOutcome> ApplyLiveOnlyAsync(Settings requested);

    /// <summary>
    /// Applies to the daemon only: no write, and no change to what the user is editing. For a transient
    /// override the host manages, where the editor must go on showing and saving the user's own settings.
    /// </summary>
    /// <param name="requested">The caller's settings. Copied on admission; not modified or retained.</param>
    /// <returns>
    /// What happened. An override that never reached the daemon is not an override, and is not recorded
    /// as one.
    /// </returns>
    Task<SettingsApplyOutcome> ApplyEphemeralAsync(Settings requested);

    /// <summary>Puts the daemon back on <see cref="GetCurrent"/>, ending any temporary override.</summary>
    /// <returns>
    /// What happened. The override is over only once the daemon has taken the settings back; until then
    /// the tablet is still running it, and callers must not clear an indicator saying so.
    /// </returns>
    Task<SettingsApplyOutcome> ClearEphemeralOverrideAsync();

    /// <summary>Re-reads the saved default from disk and applies it, discarding any override.</summary>
    /// <returns>
    /// What happened. Every way this can fall short has its own status, because a restore that did not
    /// happen leaves the override running — and telling the user their tablet is back to normal when it
    /// is not is the failure this distinguishes.
    /// </returns>
    Task<SettingsRestoreOutcome> RestoreDefaultAsync();
}
