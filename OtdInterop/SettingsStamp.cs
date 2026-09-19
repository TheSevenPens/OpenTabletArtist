namespace OtdInterop;

/// <summary>
/// Identifies a particular state of a particular daemon connection: which connection, and which revision
/// of the settings that connection's session was publishing.
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
/// Keeping a result without knowing which state it came from is how settings get reverted. A response
/// that was overtaken looks exactly like a current one, and adopting it is not merely a stale display:
/// the next edit is built on those values and sends them back to the daemon.
/// </para>
/// <para>
/// So a result is stamped with the state it describes, and a caller holding one can ask later whether
/// that state is still current. This is a stamp on a <em>result</em>. It is not the mechanism that stops
/// queued work reaching the wrong daemon — that is the session's own invalidation, which takes effect
/// immediately and never queues, and which reports <see cref="SettingsApplyStatus.Superseded"/>.
/// </para>
/// </remarks>
///
/// <param name="Session">
/// The daemon connection this belongs to. Opaque and monotonic: a new value means a different daemon, or
/// the same daemon reached through a new connection. Values are never reused within a process.
/// </param>
/// <param name="Version">
/// Which revision of that session's published settings this describes. It counts published revisions and
/// nothing else, so two stamps carrying the same session and the same value describe the same settings.
///
/// Deliberately not a count of operations, and deliberately not a count of everything the daemon has
/// accepted. An operation that changes the daemon without changing what the session publishes — a
/// transient per-app override — produces no revision, which is why no result is handed back for one.
/// Comparable only against stamps carrying the same <paramref name="Session"/>.
/// </param>
public readonly record struct SettingsStamp(long Session, long Version)
{
    /// <summary>A stamp belonging to no session — the state before anything has connected.</summary>
    public static readonly SettingsStamp None = new(0, 0);

    /// <summary>True when no session is attached.</summary>
    public bool IsNone => Session == 0;

    /// <summary>
    /// True when what this stamp describes is no longer current, given <paramref name="other"/> as the
    /// state now. The test a caller applies before letting something it has been holding overwrite what
    /// it is showing.
    /// </summary>
    /// <remarks>
    /// A different session counts as superseded, and that is the case worth being careful about. The
    /// daemon this stamp belonged to has gone, so what it describes is not merely old — it describes a
    /// machine the user has moved on from, and there is no ordering between the two to appeal to.
    /// Treating a cross-session comparison as "not superseded" would be exactly backwards: it would let
    /// the stalest possible result through.
    ///
    /// Equality is not supersession. A result stamped with the revision that is still current describes
    /// the current state, which is the ordinary case for an operation that has just succeeded.
    /// </remarks>
    /// <param name="other">The state now — usually a stamp just taken from the session.</param>
    public bool SupersededBy(SettingsStamp other) =>
        other.Session != Session || other.Version > Version;
}
