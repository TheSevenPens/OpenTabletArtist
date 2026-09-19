namespace OtdInterop;

/// <summary>
/// Identifies which daemon connection a piece of work belongs to, and where it sits in the order of
/// operations against that connection.
/// </summary>
///
/// <remarks>
/// <para>
/// A settings operation is not instantaneous. It is admitted, it may wait behind other operations, it is
/// sent, and its result comes back — and the daemon underneath can be stopped and replaced at any point
/// in that sequence. Users do this: they run more than one OpenTabletDriver build, in folders of their
/// choosing, and switch between them while this application is open.
/// </para>
/// <para>
/// Without a stamp, two things go wrong, and both have happened. An operation admitted for daemon A is
/// delivered to daemon B when it finally reaches the front of the queue. And an operation that was
/// already in flight when the switch happened comes back afterwards and writes A's settings into B's
/// settings file. Serializing the operations does not prevent either one, because invalidation must not
/// itself queue — if it waited behind work belonging to a daemon that has gone, that work would run
/// first, which is the failure being prevented.
/// </para>
/// <para>
/// So the stamp is captured when an operation is <b>admitted</b> — before it waits — and checked again
/// before anything leaves the process and before any result is published. <see cref="Version"/> orders
/// operations within one session, so a result that arrives late cannot overwrite the state of a newer
/// one that has already completed.
/// </para>
/// </remarks>
///
/// <param name="Session">
/// The daemon connection this belongs to. Opaque and monotonic: a new value means a different daemon, or
/// the same daemon reached through a new connection. Values are never reused within a process.
/// </param>
/// <param name="Version">
/// Which state of that session this describes. It counts the moments the session's settings change —
/// each accepted mutation and each adopted read — so a caller can tell whether the ground has moved
/// since it last looked. Comparable only against stamps carrying the same <paramref name="Session"/>.
///
/// Deliberately not a count of operations. What a caller wants to know is whether the state it is
/// holding is still the current one, and operations that change nothing do not make it stale.
/// </param>
public readonly record struct SettingsStamp(long Session, long Version)
{
    /// <summary>A stamp belonging to no session — the state before anything has connected.</summary>
    public static readonly SettingsStamp None = new(0, 0);

    /// <summary>True when no session is attached.</summary>
    public bool IsNone => Session == 0;

    /// <summary>
    /// True when <paramref name="other"/> belongs to the same session and was admitted no earlier than
    /// this one. The test a caller applies before letting a completed operation overwrite what it is
    /// currently showing: a result that fails it describes either a daemon that has gone or an older
    /// operation whose answer arrived out of order.
    /// </summary>
    /// <param name="other">The stamp to compare against, usually the caller's current one.</param>
    public bool SupersededBy(SettingsStamp other) =>
        other.Session == Session && other.Version >= Version;
}
