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
    private readonly IDaemonProcessLocator _locator;
    private SettingsCoordinator? _settings;
    private bool _disposed;

    /// <summary>
    /// The executable this session believes it is talking to, or empty before it has been able to look.
    ///
    /// Only ever set to a path that was actually read. An unreadable one leaves this alone, so a daemon
    /// that hides itself for one reconnect does not erase what we knew a moment ago and turn the next
    /// successful read into a false "it changed".
    /// </summary>
    private string _daemonPath = "";

    private OtdSession(IDaemonTransport connection, IDaemonSettingsChannel channel,
        ISettingsFileStore? store, IOtdLog log, IOtdSettingsPolicy policy, IDaemonProcessLocator locator)
    {
        Connection = connection;
        _channel = channel;
        _store = store;
        _log = log;
        _policy = policy;
        _locator = locator;
    }

    /// <summary>
    /// Opens a session against the OpenTabletDriver daemon. Nothing is connected until
    /// <see cref="IDaemonTransport.ConnectAsync"/> is called on <see cref="Connection"/>.
    /// </summary>
    /// <param name="log">Where the session records what it could not do — mostly partial failure, which
    /// is exactly what is invisible from outside.</param>
    /// <param name="policy">The host's own rules, applied to a private copy on the way out.</param>
    /// <param name="locator">
    /// How to find out which executable is answering. The session needs it because a different one is a
    /// session boundary, and reading a process's path is the host's to do.
    /// </param>
    /// <returns>The session. The host owns disposing it.</returns>
    public static OtdSession Create(IOtdLog log, IOtdSettingsPolicy policy, IDaemonProcessLocator locator)
    {
        var client = new DaemonClient(log);
        return new OtdSession(client, client, store: null, log, policy, locator);
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
    /// <param name="locator">How to find out which executable is answering.</param>
    /// <returns>A session over <paramref name="connection"/>.</returns>
    internal static OtdSession ForTesting<T>(T connection, ISettingsFileStore? store,
        IOtdLog log, IOtdSettingsPolicy policy, IDaemonProcessLocator locator)
        where T : IDaemonTransport, IDaemonSettingsChannel =>
        new(connection, connection, store, log, policy, locator);

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
    /// Looks at which daemon is answering and, if it is a different one, drops the state that belonged to
    /// the daemon that has gone.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// Called by the host whenever a connection is established. The judgement is here rather than in the
    /// host because the state being dropped is this library's — the change a daemon accepted but never
    /// wrote, the file that change was for, what its settings file last held, whether it is running a
    /// transient override. None of that describes the new daemon, and each one misleads a different part
    /// of the host if carried across.
    /// </para>
    /// <para>
    /// <b>The trigger is still the host's.</b> Nothing here subscribes to the connection, so this is not
    /// automatic invalidation — calling it at the right moment is something a host can still get wrong.
    /// Making it self-driving is outstanding work under #807.
    /// </para>
    /// <para>
    /// <b>Identity means the executable, not the process.</b> A daemon stopped and started again from the
    /// same path is the same daemon by this test, and does not report a change. That is deliberate and
    /// long-standing: what the state being protected describes is a settings file and an installation,
    /// both of which survive a restart. It does mean this is not a detector for every replacement
    /// process.
    /// </para>
    /// <para>
    /// A daemon whose executable cannot be read is <b>not</b> treated as a change. That is the whole
    /// reason this compares paths instead of connections: users run more than one OpenTabletDriver build
    /// and switch between them, but they also just reconnect, and an elevated daemon is unreadable every
    /// time. Discarding on "cannot see" would throw away a legitimate unsaved edit on an ordinary
    /// reconnect. The pending-write case that leaves open is covered independently, by the settings
    /// session refusing to retry a write whose destination file has moved — which is that one hazard, not
    /// a claim that an unidentifiable replacement daemon is safe in general.
    /// </para>
    /// <para>
    /// Runs under the host's serialized execution context, like everything else here.
    /// </para>
    /// </remarks>
    /// <returns>What is answering, whether it changed, and whether that cost an unsaved edit.</returns>
    public DaemonChange NoteConnectedDaemon()
    {
        var actual = ConnectedDaemonPath();

        // Both conditions matter. No remembered path means this is the first look, and everything this
        // session holds already belongs to whatever is answering now. An unreadable path means we cannot
        // tell, which is not the same as knowing it is different.
        var changed = _daemonPath.Length > 0 && actual != null && !PathEquality.Same(_daemonPath, actual);

        var discarded = false;
        if (changed)
        {
            _log.Warn($"The connected daemon changed from {_daemonPath} to {actual}; "
                      + "dropping settings state that belonged to the previous one.");
            discarded = _settings?.ResetForNewDaemon() ?? false;
        }

        if (actual != null) _daemonPath = actual;
        return new DaemonChange(actual, changed, discarded);
    }

    /// <summary>The executable behind the process answering the connection, or null when it can't be read.</summary>
    private string? ConnectedDaemonPath()
    {
        if (Connection.GetServerProcessId() is { } pid) return _locator.PathOf(pid);

        // The pipe-to-process-id lookup is Windows-only. Elsewhere the daemon is effectively a singleton,
        // so the single running one is a sound answer; kept off the Windows path so its exact pipe
        // attribution -- which is what distinguishes our daemon from a second OTD instance -- is
        // unchanged (#140).
        return OperatingSystem.IsWindows() ? null : _locator.SingleRunningDaemonPath();
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
