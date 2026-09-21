namespace OtdInterop;

/// <summary>
/// A conflict this session observed: what the daemon held instead of what was expected, and which daemon
/// held it (#906).
/// </summary>
///
/// <remarks>
/// <para>
/// Handed back with a <see cref="SettingsApplyStatus.ChangedElsewhere"/> result, and presented again to
/// authorise an overwrite. It exists so that consent to overwrite is attached to <b>a particular conflict
/// on a particular daemon</b> rather than to a caller's persistence: without it, "apply again" is the only
/// way to express intent, and re-editing a value is not consent to discard somebody else's work.
/// </para>
/// <para>
/// It is deliberately not a permission slip. Presenting one only says "I saw this and I still want mine";
/// the daemon is read again before anything is written, and if it has moved on since, the overwrite is
/// refused and a fresh conflict reported. So a token cannot be held onto and used later to write over an
/// edit its holder never saw.
/// </para>
/// <para>
/// Opaque on purpose. What it carries is this library's business, and a caller that reasons about the
/// contents is reasoning about a comparison it does not own.
/// </para>
/// </remarks>
public sealed record SettingsConflict
{
    internal SettingsConflict(Guid issuer, int channel, string daemonState)
    {
        Issuer = issuer;
        Channel = channel;
        DaemonState = daemonState;
    }

    /// <summary>
    /// Which session saw it. Channel numbers are per-session counters, so two sessions hand out the same
    /// small integers and a token from one would otherwise be accepted by the other (#910).
    /// </summary>
    internal Guid Issuer { get; }

    /// <summary>The connection the conflict was seen on. A different one is a different question.</summary>
    internal int Channel { get; }

    /// <summary>What the daemon was holding, normalised the way the comparison normalises.</summary>
    internal string DaemonState { get; }
}
