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
/// <item>Every operation notes which connection it belongs to when it is <b>admitted</b> — before it
/// waits for anything, because waiting is exactly when the daemon can change underneath it. That note is
/// internal; the <see cref="SettingsStamp"/> a caller sees is on the result, and says which state the
/// result describes.</item>
/// <item>Invalidation takes effect immediately. It never queues behind operations belonging to a
/// connection that has gone; if it did, that work would run first, which is the thing being prevented.
/// </item>
/// <item>Work that is still queued when its session ends is discarded before it is sent.</item>
/// <item>A result that arrives after its session has ended is not written to disk, not published as
/// state, and not reported as success. It comes back as
/// <see cref="SettingsApplyStatus.Superseded"/>.</item>
/// <item>What cannot be undone is not pretended away: a request already sent may well have been acted
/// on, and an old daemon that accepted one is still running it. Supersession describes what this session
/// did with the result — not written, not published, not called success — and rejecting a completion
/// cannot retract the request that produced it.</item>
/// </list>
///
/// <para><b>Callbacks.</b> An implementation may call back into the host while an operation is running —
/// to report progress on saving, for instance. Those calls happen on the same execution context as the
/// operation, so a host that re-enters this session from one is re-entering an operation in progress and
/// will deadlock on the serialization. A callback that throws propagates out of the operation that made
/// it; nothing here catches on the host's behalf.</para>
///
/// <para><b>Execution — what is actually guaranteed today, and what the host must supply.</b></para>
///
/// <para>Guaranteed: mutating operations are serialized against each other, so no two of them are
/// part-way through at once. That is the extent of it. It does not serialize reading this session's
/// state, and it does not serialize the callbacks an implementation makes.</para>
///
/// <para>Required of the host, because the implementation does not provide it: <b>one serialized
/// execution context</b> for every call into this session, every adoption of a result, every reset, every
/// read of its state, and every callback out of it — <em>including the continuations of the host's own
/// awaits</em>. "One thread starts the calls" is not sufficient; a thread whose awaits resume on
/// arbitrary pool threads has not supplied a context. The only host that exists today supplies the UI
/// thread, and that confinement — not any internal locking — is what makes it safe there. A headless host
/// can supply an equivalent context without any UI framework, and must.</para>
///
/// <para>Being headless does not establish thread safety on its own; a library merely free of UI types is
/// not thereby safe to call from anywhere.</para>
///
/// <para>This is an honest description of what works today, not the finished contract. Proper internal
/// synchronization and an orderly shutdown are still owed, and until they exist a host that cannot supply
/// the context above should not use this.</para>
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

    /// <summary>
    /// Re-reads the daemon's settings and adopts them as what this session considers current.
    ///
    /// The host asks for a refresh; it does not read the daemon itself. Getting this right means
    /// observing the session's state before the read, discarding an answer that was overtaken while in
    /// flight, and not reading at all while a temporary override is running — an ordering the host would
    /// otherwise have to reproduce, with the failure silent when it did not.
    /// </summary>
    /// <returns>What happened. Every outcome is ordinary; none of them needs the host to act.</returns>
    Task<SettingsReloadOutcome> ReloadFromDaemonAsync();

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
    ///
    /// Never carries a prepared result, even on success. This publishes no revision, so there is nothing
    /// a caller could adopt without adopting a transient override as the settings to save.
    /// </returns>
    Task<SettingsApplyOutcome> ApplyEphemeralAsync(Settings requested);

    /// <summary>
    /// Puts the daemon back on <see cref="GetCurrent"/>, ending any temporary override.
    ///
    /// Unconditional, and deliberately so: it sends the current settings whether or not this session
    /// believes an override is running. <see cref="HasEphemeralOverride"/> records what this session was
    /// told, and a host that has just taken over, or reconnected, knows less about the daemon than it
    /// would like. Putting the daemon somewhere known is cheap; leaving a tablet on an override nobody
    /// recorded is not.
    /// </summary>
    /// <returns>
    /// What happened. The override is over only once the daemon has taken the settings back; until then
    /// the tablet is still running it, and callers must not clear an indicator saying so.
    ///
    /// <see cref="SettingsApplyStatus.NoChange"/> means there was nothing to put the daemon back on —
    /// nothing has been loaded — and nothing was sent. Every other case sends, so a success here is
    /// <see cref="SettingsApplyStatus.AppliedLive"/> even when no override was recorded.
    /// </returns>
    Task<SettingsApplyOutcome> ClearEphemeralOverrideAsync();

    /// <summary>
    /// Retries a pending disk write, if there is one and the bounded retry budget is not spent.
    ///
    /// Free to call on every refresh: it reports <see cref="SettingsApplyStatus.NoChange"/> when there is
    /// nothing to do. A write refused because the file was momentarily locked then fixes itself with no
    /// user action.
    /// </summary>
    /// <returns>The result of the write, or <see cref="SettingsApplyStatus.NoChange"/>.</returns>
    Task<SettingsApplyOutcome> RetryPendingPersistAsync();

    /// <summary>
    /// Records the baseline the no-op guard compares against, after the host has finished a refresh.
    ///
    /// Separate from <see cref="ReloadFromDaemonAsync"/> because the host repairs what it read — removing
    /// dead filter stores, disabling ones its policy does not approve — and the guard's subject is what
    /// the daemon holds once that is done. Recording it before those repairs would let the guard skip an
    /// apply that would have carried them.
    /// </summary>
    void RecordLoadedBaseline();

    /// <summary>Re-reads the saved default from disk and applies it, discarding any override.</summary>
    /// <returns>
    /// What happened. Every way this can fall short has its own status, because a restore that did not
    /// happen leaves the override running — and telling the user their tablet is back to normal when it
    /// is not is the failure this distinguishes.
    /// </returns>
    Task<SettingsRestoreOutcome> RestoreDefaultAsync();
}
