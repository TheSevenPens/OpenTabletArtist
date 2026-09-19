namespace OtdInterop;

/// <summary>What <see cref="OtdSession.NoteConnectedDaemon"/> found when it looked at who is answering.</summary>
///
/// <param name="ExecutablePath">
/// The executable answering now, or null when it cannot be read — an elevated daemon, another user's, or
/// a platform without the lookup. Null is an ordinary answer meaning "cannot see", and the session
/// treats it as a reason to leave its state alone rather than to discard it.
/// </param>
/// <param name="Changed">
/// True when a <em>different</em> daemon is answering than the one this session was talking to, which is
/// a session boundary and not merely a new label.
///
/// False when it is the same one, when this is the first look, and when the path cannot be read. That
/// last case is deliberate: an unreadable path is not evidence of a change, and discarding state on it
/// would throw away a legitimate unsaved edit every time an elevated daemon reconnects.
/// </param>
/// <param name="DiscardedUnsavedChange">
/// True when the change cost the user something: an edit the old daemon had accepted but that never
/// reached disk. The host says so — everything else dropped at a session boundary is bookkeeping the
/// user never knew about, and a pending write is an edit they made.
/// </param>
public readonly record struct DaemonChange(
    string? ExecutablePath,
    bool Changed = false,
    bool DiscardedUnsavedChange = false);
