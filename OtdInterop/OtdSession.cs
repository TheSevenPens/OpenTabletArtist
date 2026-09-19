namespace OtdInterop;

/// <summary>
/// One daemon connection and the one settings authority over it.
/// </summary>
///
/// <remarks>
/// <para>
/// This is how a host gets both, and it exists because getting them separately went wrong. The previous
/// arrangement handed the host a connection and let it ask a factory for a settings session over that
/// connection — and the factory would build a second one, over the same connection, for the asking.
/// </para>
/// <para>
/// Two sessions are not two views of the same thing. Each has its own mutation gate, session generation,
/// retry state and baseline, so neither can see what the other is doing: two applies reach the daemon at
/// once, each writes the other's settings out of its own file, and a reset clears half the state.
/// Everything the ordering in this library guarantees is guaranteed <em>per session</em>. So the session
/// owns the connection, <see cref="OpenSettings"/> hands out the one authority, and a second call is
/// refused — the invariant is a property of the type rather than a rule the host has to follow.
/// </para>
/// <para>
/// It also removes a cast. When the host supplied the connection, the library had to check at runtime
/// that the thing it was handed carried the internal settings channel, because the public connection
/// interface can be implemented by anyone. Creating the connection here makes that a compile-time fact.
/// The seam a host needs for testing is not lost: it moves to this type's internal construction, which
/// is where it belongs — it is the library's test support, not part of the API the host is offered.
/// </para>
/// </remarks>
public sealed class OtdSession : IDisposable
{
    private readonly IDaemonSettingsChannel _channel;
    private readonly ISettingsFileStore? _store;
    private readonly IOtdLog _log;
    private readonly IOtdSettingsPolicy _policy;
    private IOtdSettingsSession? _settings;
    private bool _disposed;

    private OtdSession(IDaemonTransport connection, IDaemonSettingsChannel channel,
        ISettingsFileStore? store, IOtdLog log, IOtdSettingsPolicy policy)
    {
        Connection = connection;
        _channel = channel;
        _store = store;
        _log = log;
        _policy = policy;
    }

    /// <summary>
    /// Opens a session against the OpenTabletDriver daemon. Nothing is connected until
    /// <see cref="IDaemonTransport.ConnectAsync"/> is called on <see cref="Connection"/>.
    /// </summary>
    /// <param name="log">Where the session records what it could not do — mostly partial failure, which
    /// is exactly what is invisible from outside.</param>
    /// <param name="policy">The host's own rules, applied to a private copy on the way out.</param>
    /// <returns>The session. The host owns disposing it.</returns>
    public static OtdSession Create(IOtdLog log, IOtdSettingsPolicy policy)
    {
        var client = new DaemonClient(log);
        return new OtdSession(client, client, store: null, log, policy);
    }

    /// <summary>
    /// A session over a connection that is not real, for tests of the host.
    /// </summary>
    /// <remarks>
    /// Internal because it is the library's test support and not something the host is offered. A host
    /// API that grows a parameter every time a test needs to control something is an API shaped by its
    /// tests, and the store below is exactly that parameter: it existed on the supported factory so a
    /// write could be made to fail on demand, which is a property of this library's behaviour and no
    /// business of the application's.
    /// </remarks>
    /// <typeparam name="T">
    /// A stand-in that is both a connection and a settings channel. The constraint rather than a cast:
    /// casting here would have been the same reasoning that put one in the supported factory, and would
    /// fail at construction instead of at compile time. It also states the thing that matters — both
    /// capabilities on <em>one</em> instance — which two parameters would leave to the caller.
    /// </typeparam>
    /// <param name="connection">The stand-in connection.</param>
    /// <param name="store">The writer to use instead of the library's own, or null for the library's.</param>
    /// <param name="log">Where the session records partial failures.</param>
    /// <param name="policy">The host's rules.</param>
    /// <returns>A session over <paramref name="connection"/>.</returns>
    internal static OtdSession ForTesting<T>(T connection, ISettingsFileStore? store,
        IOtdLog log, IOtdSettingsPolicy policy)
        where T : IDaemonTransport, IDaemonSettingsChannel =>
        new(connection, connection, store, log, policy);

    /// <summary>
    /// The daemon connection: lifecycle, device queries, the log stream, the debug stream and the plugin
    /// verbs. Deliberately no way to read or write settings — that is <see cref="OpenSettings"/>.
    /// </summary>
    /// <remarks>
    /// <b>Borrowed, not given.</b> This session owns it and disposes it. The type is
    /// <see cref="IDisposable"/> because the underlying connection is, not because a caller should use
    /// that — doing so leaves this session holding a connection that is gone, with a settings authority
    /// still reporting over it. Dispose the session.
    ///
    /// Connecting and clearing <see cref="IDaemonTransport.AutoReconnect"/> around a user-initiated stop
    /// are the host's to drive, and stay here for now. Separating the capabilities a host legitimately
    /// needs from the ownership operations it does not is part of the lifecycle work #807 still owes; a
    /// narrower interface over this same object would not be enough on its own, since it could be cast
    /// back, so that will want a forwarding object rather than a cast-away.
    /// </remarks>
    public IDaemonTransport Connection { get; }

    /// <summary>
    /// The settings authority for this connection. One per session; a second call is refused.
    /// </summary>
    /// <param name="settingsPath">
    /// Where the daemon's settings file is, read late. It comes from the daemon itself, so it has no
    /// value when the session is created, and it changes when a different daemon answers.
    /// </param>
    /// <param name="isOwnedDaemon">
    /// Whether this daemon is positively known to be the host's own. Positive knowledge, not "not known
    /// to be someone else's" — the host's policy runs against a daemon only when this is true (#742).
    /// </param>
    /// <param name="onSaveState">Reports save progress, which the host shows.</param>
    /// <remarks>
    /// These are construction arguments, not a service lookup: a second call would carry a different
    /// path, ownership test and save callback, and silently discarding the second caller's would be
    /// worse than refusing. Configure once where the application is composed and share what comes back.
    ///
    /// Called under the same serialized execution context that <see cref="IOtdSettingsSession"/>
    /// requires. The check-then-assign below is not a thread-safe one-time initialization on its own,
    /// and is not trying to be.
    /// </remarks>
    /// <returns>The one settings authority for this connection.</returns>
    /// <exception cref="InvalidOperationException">
    /// Settings have already been opened on this session, or the session has been disposed.
    /// </exception>
    public IOtdSettingsSession OpenSettings(Func<string> settingsPath, Func<bool> isOwnedDaemon,
        Action<SettingsSaveState> onSaveState)
    {
        if (_disposed)
            throw new InvalidOperationException(
                "This session has been disposed; its connection is gone, so there is nothing for a "
                + "settings authority to be an authority over.");

        if (_settings != null)
            throw new InvalidOperationException(
                "This session's settings are already open. One connection has one settings authority: a "
                + "second would have its own ordering, retry state and baseline, and neither would see "
                + "what the other was doing. Reuse the authority this returned.");

        return _settings = _store is { } store
            ? new SettingsCoordinator(_channel, store, settingsPath, isOwnedDaemon, onSaveState, _log, _policy)
            : new SettingsCoordinator(_channel, settingsPath, isOwnedDaemon, onSaveState, _log, _policy);
    }

    /// <summary>
    /// Closes the connection. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <b>Not a shutdown.</b> It closes the connection and stops this session issuing new work — nothing
    /// more. Operations already in flight are not awaited, cancelled or settled, and a callback from one
    /// can still arrive afterwards. #807 still owes that contract, and calling this complete ownership of
    /// teardown would be the kind of claim that stops anyone finishing it.
    ///
    /// The settings authority is deliberately not torn down, because it has nothing to release and
    /// something to answer: a host asking afterwards whether a change went unsaved should get the truth
    /// rather than an exception. Reading what already happened is allowed; starting something new is what
    /// <see cref="OpenSettings"/> refuses.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Connection.Dispose();
    }
}
