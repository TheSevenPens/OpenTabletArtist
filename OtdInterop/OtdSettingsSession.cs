namespace OtdInterop;

/// <summary>
/// Creates the settings session for a daemon connection.
/// </summary>
///
/// <remarks>
/// <para>
/// The implementation is internal, so this is how a host gets one. That is deliberate: the session is
/// the only thing entitled to change settings, and a host able to construct alternatives — or to reach
/// the writer the session uses — would have the boundary in name only.
/// </para>
/// <para>
/// The connection must be one this library made. The session needs the settings channel, which is
/// internal and which <see cref="DaemonTransport.Create"/> supplies; a host can implement or decorate the
/// public <see cref="IDaemonTransport"/> and pass that, but not the channel behind it, so such a call
/// fails here rather than degrading into a session that silently cannot write.
/// </para>
/// <para>
/// <b>One session per connection.</b> A second one is refused. Two sessions over one connection are not
/// two views of the same thing: each has its own mutation gate, session generation, retry state and
/// baseline, so two applies reach the daemon at once, each writes the other's settings out of its own
/// file, and a reset clears half the state. Everything the ordering here guarantees is guaranteed per
/// session. A host that wants a differently configured session makes another connection.
/// </para>
/// </remarks>
public static class OtdSettingsSession
{
    /// <summary>Builds a settings session over an existing connection.</summary>
    /// <param name="daemon">A connection from <see cref="DaemonTransport.Create"/>.</param>
    /// <param name="settingsPath">
    /// Where the daemon's settings file is, read late. It comes from the daemon itself, so it has no
    /// value when the session is built, and it changes when a different daemon answers.
    /// </param>
    /// <param name="isOwnedDaemon">
    /// Whether this daemon is positively known to be the host's own. Positive knowledge, not "not known
    /// to be someone else's" — the host's policy runs against a daemon only when this is true (#742).
    /// </param>
    /// <param name="onSaveState">Reports save progress, which the host shows.</param>
    /// <param name="log">Where the session records partial failures.</param>
    /// <param name="policy">The host's own rules, applied to a private copy on the way out.</param>
    /// <returns>The session. The host owns nothing else it needs to change settings.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="daemon"/> did not come from <see cref="DaemonTransport.Create"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="daemon"/> already has a settings session.
    /// </exception>
    public static IOtdSettingsSession Create(IDaemonTransport daemon,
        Func<string> settingsPath, Func<bool> isOwnedDaemon, Action<SettingsSaveState> onSaveState,
        IOtdLog log, IOtdSettingsPolicy policy) =>
        new SettingsCoordinator(Channel(daemon), settingsPath, isOwnedDaemon, onSaveState, log, policy);

    /// <summary>
    /// Builds a settings session that writes through the supplied store rather than the library's own.
    ///
    /// For a host that must make a write fail on demand. Failing writes are central behaviour here —
    /// applied-but-not-saved, the bounded retry, the destination a pending write is bound to — and none
    /// of it is reachable from outside without this. Supplying an alternative writer is not the same as
    /// being handed the one that writes the file the daemon is running from.
    /// </summary>
    /// <param name="daemon">A connection from <see cref="DaemonTransport.Create"/>.</param>
    /// <param name="store">The writer to use instead of the library's.</param>
    /// <param name="settingsPath">Where the daemon's settings file is, read late.</param>
    /// <param name="isOwnedDaemon">Whether this daemon is positively known to be the host's own.</param>
    /// <param name="onSaveState">Reports save progress, which the host shows.</param>
    /// <param name="log">Where the session records partial failures.</param>
    /// <param name="policy">The host's own rules, applied to a private copy on the way out.</param>
    /// <returns>The session, writing through <paramref name="store"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="daemon"/> did not come from <see cref="DaemonTransport.Create"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="daemon"/> already has a settings session.
    /// </exception>
    public static IOtdSettingsSession Create(IDaemonTransport daemon, ISettingsFileStore store,
        Func<string> settingsPath, Func<bool> isOwnedDaemon, Action<SettingsSaveState> onSaveState,
        IOtdLog log, IOtdSettingsPolicy policy) =>
        new SettingsCoordinator(Channel(daemon), store, settingsPath, isOwnedDaemon, onSaveState, log, policy);

    /// <summary>
    /// The settings channel behind a connection, claimed for the session about to be built.
    ///
    /// The cast is because the host supplies the connection and the channel it needs is internal. The
    /// alternative — the library creating the connection too — would take away the seam that lets a host
    /// test against a daemon that is not there, which is worth more than avoiding one cast. It fails
    /// loudly rather than leaving a session that cannot write and would not say so.
    ///
    /// The claim is the part that matters. Making the implementation internal stopped a host building
    /// its own session; it did nothing about this factory building a second one over the same
    /// connection, which is the same loss of ordering by a shorter route.
    /// </summary>
    private static IDaemonSettingsChannel Channel(IDaemonTransport daemon)
    {
        if (daemon as IDaemonSettingsChannel is not { } channel)
            throw new ArgumentException(
                $"{nameof(daemon)} must be a connection from {nameof(DaemonTransport)}.{nameof(DaemonTransport.Create)}.",
                nameof(daemon));

        if (!channel.TryClaimExclusiveUse())
            throw new InvalidOperationException(
                "This connection already has a settings session. One connection has one settings "
                + "authority: a second would have its own ordering, retry state and baseline, and "
                + "neither would see what the other was doing. Make another connection instead.");

        return channel;
    }
}
