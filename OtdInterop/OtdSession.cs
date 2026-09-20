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
        ISettingsFileStore? store, IOtdLog log, IOtdSettingsPolicy policy, IDaemonProcessLocator locator,
        IOtdExecutionContext context, LifecycleProbe? probe)
    {
        _probe = probe;
        Connection = connection;
        Capabilities = new DaemonCapabilities(connection);
        _channel = channel;
        _store = store;
        _log = log;
        _policy = policy;
        _locator = locator;
        _context = context;

        // Subscribed here rather than left to the host. The transport raises on its own thread the moment
        // its channel is usable; everything this session then has to do -- identify the daemon, drop what
        // belonged to the one that has gone -- touches state a host may be reading, so it is posted to the
        // host's context rather than done where the notification arrived.
        connection.Connected += OnTransportConnected;
        connection.Disconnected += OnTransportDisconnected;
    }

    private readonly IOtdExecutionContext _context;

    /// <summary>
    /// Where a test can be told that a thread has reached a lock it is about to contend for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deadlock these guard against is an ordering between two threads, and the point that matters --
    /// "the closer is past everything else and about to take the publication gate" -- has no observable
    /// boundary outside this class. The test that first covered it waited 100ms and asserted the close
    /// had not finished, which also passes when the competing thread has not started: on that schedule
    /// the inline callback becomes the first closer and the inversion is never exercised. A test that
    /// passes for the wrong reason is what this whole area of the review has been about.
    /// </para>
    /// <para>
    /// So: a seam, deliberately the narrowest one available. It is reachable only through
    /// <see cref="ForTesting{T}"/>, so no host can install one, and it is a plain notification -- it
    /// decides nothing and the code reads the same with it absent.
    /// </para>
    /// </remarks>
    internal sealed class LifecycleProbe
    {
        /// <summary>Called just before a teardown contends for the publication gate.</summary>
        public Action? ReachingTeardown { get; init; }

        /// <summary>Called just before a lookup's publication contends for the publication gate.</summary>
        public Action? PublishingLookup { get; init; }
    }

    private readonly LifecycleProbe? _probe;

    /// <summary>
    /// The transport has a channel. Identify the daemon, invalidate if it is a different one, and only
    /// then tell the host.
    /// </summary>
    /// <remarks>
    /// Posted rather than run here: this arrives on the transport's thread, and what it does touches the
    /// same fields the host reads. Nothing is awaited by the transport, so a slow host context delays
    /// this session's notification and not the connection.
    ///
    /// <b>Identification happening promptly is not what makes this safe.</b> Even a fast context can be
    /// delayed arbitrarily, and the window between the channel becoming usable and this running is real
    /// either way. What makes work in that window safe is that operations bind to the channel they were
    /// admitted on (#830), and -- still to come -- that this session refuses to persist against a
    /// connection it has not finished preparing.
    /// </remarks>
    private void OnTransportConnected()
    {
        // Captured here, where the transition happened -- not read when the work runs. A queued
        // notification belongs to the channel that raised it, and by the time the host's context gets to
        // it that channel may have been replaced or dropped.
        var channel = _channel.Incarnation;
        Post("identify the connected daemon", () =>
        {
            if (!StillTheCurrentTransition(channel)) return;

            // Looking up who is answering calls out to the host's process locator, which may do anything:
            // dispose this session, or stop the daemon so that by the time it returns a different channel
            // is up. So the check goes HERE, between the call-out and the commit, rather than only on the
            // way in or only before delivery.
            //
            // Suppressing the notification alone would not have been enough, and the failure is worse
            // than a lost notification. Committing what the lookup found would record this session as
            // being on the daemon that arrived during it -- so the transition that actually happened,
            // when its own turn came, would compare equal to what was already recorded and be reported
            // as no change at all. The host would never hear about the daemon it is now talking to.
            var found = ConnectedDaemonPath();
            if (!StillTheCurrentTransition(channel)) return;

            bool StillMine() => StillTheCurrentTransition(channel);

            var commit = CommitConnectedDaemon(found);
            AnnounceCommit(commit, StillMine);

            // And again, because announcing is host code too. This transition is then stale as a
            // notification. What it records about a discarded edit is not stale, and stays owed.
            if (!StillMine()) return;

            // Built here, after the announcements, so it carries any discard they produced.
            // Asked for every channel, not only when the daemon changed. A daemon this session could not
            // read is deliberately NOT reported as a change (#823), and it can still be a different
            // install with a different settings file -- so identity is the wrong thing to gate this on.
            LearnWhereToPersist(channel);

            var change = new DaemonChange(commit.Actual, commit.Changed, _discardOwed);
            Deliver(Connected, h => h(change), nameof(Connected), StillMine, ClaimDiscard);
        });
    }

    /// <remarks>
    /// The same policy as a connection, for the same reason. A queued disconnect whose successor has
    /// already arrived would tell a host it is disconnected while a channel is up -- and OTA acts on that
    /// by clearing what it shows. Nothing is captured here because a drop has no channel of its own to
    /// name: what makes this one obsolete is that a newer connection exists, which is exactly what a
    /// non-zero incarnation means.
    /// </remarks>
    private void OnTransportDisconnected() => Post("report a disconnect", () =>
    {
        if (!StillDisconnected()) return;
        Deliver(Disconnected, h => h(), nameof(Disconnected), StillDisconnected);
    });

    /// <summary>Whether a queued disconnect still describes the world.</summary>
    private bool StillDisconnected() => !_disposed && _channel.Incarnation == 0;

    // ---------------------------------------------------------------------------------------------
    // THE RULE, for everything above and below.
    //
    // A call into host code may reenter this session, dispose it, or supersede the transition being
    // handled. Nothing established before such a call authorises a mutation or a notification after it
    // without being reconsidered. LOGGING IS SUCH A CALL -- that one is easy to miss, and missing it is
    // how the commit came to assign its identity after the world had already moved (#844).
    //
    // The call-outs on this path, audited:
    //
    //   IOtdExecutionContext.PostAsync   where the work runs at all, and it may run it inline
    //   IDaemonProcessLocator            asked who is answering; a check follows, before the commit
    //   IOtdLog                          after the commit's state changes, and rechecked before the next
    //   the save-state callback          after a recheck of its own, because the logger above can reenter
    //   Connected subscribers            checked between each one
    //
    // The three validity checks are deliberately NOT consolidated. They answer different questions --
    // do not begin obsolete work; do not commit an obsolete observation; do not keep announcing
    // something an earlier subscriber made untrue -- and sharing a predicate is not a reason to share a
    // location.
    //
    // This audit covers the transition path. The settings operations reach host code too, and auditing
    // those is separate work.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether queued work for <paramref name="channel"/> still describes something worth telling anyone
    /// about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ways it stops being so, and both were reachable. The channel it belongs to may have been
    /// replaced -- so identifying "whatever is answering now" would report a transition that never
    /// happened as though it had, and report it under the wrong occasion. Or the connection may simply
    /// have gone, in which case raising Connected tells a host it is connected when it is not; OTA
    /// responds to that by setting IsConnected and starting a data load.
    /// </para>
    /// <para>
    /// And a disposed session says nothing at all. Work already queued when disposal happens still runs,
    /// which unsubscribing from the transport does not prevent -- the notification had already been
    /// posted. A host whose own teardown has begun is not in a state to be told anything.
    /// </para>
    /// </remarks>
    private bool StillTheCurrentTransition(int channel) =>
        !_disposed && channel != 0 && _channel.Incarnation == channel;

    /// <summary>
    /// Runs work on the host's context, and says so when it fails.
    /// </summary>
    /// <remarks>
    /// The task is not discarded, and that is the whole reason <see cref="IOtdExecutionContext.PostAsync"/>
    /// returns one. Work posted here has no caller to throw to: a subscriber that throws, or a host
    /// context that refuses the work, would otherwise be a failure to do something this session was asked
    /// to do with nothing anywhere to show for it.
    ///
    /// Reported rather than rethrown. There is nobody to rethrow to — this is reached from the
    /// transport's own notification — and the session's state is already correct by the time a subscriber
    /// runs, so a bad subscriber loses its notification and nothing else.
    /// </remarks>
    private void Post(string what, Action work) => _ = Report(what, work);

    /// <summary>
    /// Calls every subscriber, even when one of them throws — for as long as there is still something
    /// true to tell them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain multicast invoke stops at the first subscriber that throws, so the ones after it never
    /// hear about the connection at all. I wrote that "a bad subscriber loses its notification and
    /// nothing else" while that was not true: it lost everyone else's too, and which ones depended on
    /// subscription order. Each failure is reported separately, because one bad subscriber should not
    /// hide a second.
    /// </para>
    /// <para>
    /// <paramref name="stillWorthTelling"/> is rechecked between subscribers, because a subscriber is
    /// host code and may do anything — including dispose the session, or stop the daemon and bring the
    /// connection down. Isolating subscribers from each other's exceptions is not a reason to keep
    /// announcing a connection that has since gone; the first subscriber disposing the session used to
    /// leave the second one being told about it anyway.
    /// </para>
    /// </remarks>
    private void Deliver<T>(T? handlers, Action<T> call, string what, Func<bool> stillWorthTelling,
        Action? onFirstDelivery = null) where T : Delegate
    {
        if (handlers == null) return;

        var first = true;
        foreach (var handler in handlers.GetInvocationList())
        {
            if (!stillWorthTelling()) return;

            // Immediately before the first subscriber actually runs, and not before that. Claiming at the
            // top consumed the discard even when there were NO subscribers, or when validity was lost
            // before any of them ran -- so the fact was marked delivered with nobody to have received it.
            //
            // Attempted delivery to a real recipient is the contract, not successful handling: a
            // subscriber that throws is isolated and the fact stays claimed, because retrying on
            // subscriber failure would make "was this reported" depend on subscriber behaviour.
            if (first)
            {
                first = false;
                onFirstDelivery?.Invoke();
            }

            try { call((T)handler); }
            catch (Exception ex) { _log.Warn($"A subscriber to {what} threw; it was isolated.", ex); }
        }
    }

    private async Task Report(string what, Action work)
    {
        try
        {
            await _context.PostAsync(() =>
            {
                // The one place this can be checked: work that IS on the context asking the context
                // whether it is. A host whose PostAsync runs work somewhere else has broken the promise
                // the whole arrangement rests on, and would otherwise do so silently.
                //
                // Refused, not merely reported. This session has just established that the serialization
                // it requires is absent, and the work about to run mutates the state that serialization
                // protects. Running it anyway is not a recovery: it is the step that corrupts. Losing a
                // notification is the smaller loss, and the host is told which one and why.
                if (!_context.IsCurrent)
                {
                    _log.Warn($"Refused to {what}: the host's execution context ran posted work somewhere "
                              + "it does not consider its own, and this library's state is not safe "
                              + "under that.");
                    return;
                }

                work();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warn($"Couldn't {what} on the host's execution context.", ex);
        }
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
    /// <param name="context">
    /// Where this session runs work it starts itself — chiefly identifying the daemon after a reconnect.
    /// Required; see <see cref="IOtdExecutionContext"/> for why there is no default.
    /// </param>
    /// <returns>The session. The host owns disposing it.</returns>
    public static OtdSession Create(IOtdLog log, IOtdSettingsPolicy policy, IDaemonProcessLocator locator,
        IOtdExecutionContext context)
    {
        var client = new DaemonClient(log);
        // No probe: the seam exists for tests of this class's own thread ordering and is not something a
        // host can install.
        return new OtdSession(client, client, store: null, log, policy, locator, context, probe: null);
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
    /// <param name="context">Where posted work runs; inline when omitted, for tests not about ordering.</param>
    /// <param name="probe">
    /// Notifications for tests that <em>are</em> about ordering between threads, and nothing else --
    /// see <see cref="LifecycleProbe"/>. Omitted, this class behaves exactly as it does for a host.
    /// </param>
    /// <returns>A session over <paramref name="connection"/>.</returns>
    internal static OtdSession ForTesting<T>(T connection, ISettingsFileStore? store,
        IOtdLog log, IOtdSettingsPolicy policy, IDaemonProcessLocator locator,
        IOtdExecutionContext? context = null, LifecycleProbe? probe = null)
        where T : IDaemonTransport, IDaemonSettingsChannel =>
        new(connection, connection, store, log, policy, locator, context ?? new InlineContext(), probe);

    /// <summary>
    /// What a host may do with this connection: read, watch, and manage plugins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A forwarding object, not this session's connection wearing a smaller interface — see
    /// <see cref="IDaemonCapabilities"/> for why that distinction is the whole of the guarantee. Nothing
    /// here can close the connection, reconnect it, or change settings.
    /// </para>
    /// <para>
    /// <b>Borrowed for this session's lifetime.</b> Disposal does not currently stop it: a holder can go
    /// on calling and subscribing afterwards, reaching a connection that is gone. Nothing here is
    /// enforced yet, and whether a late call throws or reports itself disconnected is a decision the
    /// shutdown work #807 still owes. Unsubscribing must keep working either way, or a page cleaning up
    /// after the session cannot detach.
    /// </para>
    /// </remarks>
    public IDaemonCapabilities Capabilities { get; }

    /// <summary>The connection itself. Internal: owning one and using one are different things.</summary>
    private IDaemonTransport Connection { get; }

    /// <summary>
    /// A connection was established <b>and this session has finished with it</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not the transport's event forwarded. The transport raises as soon as its channel is usable, which
    /// is before anything has looked at which daemon answered — so a host acting on that would be acting
    /// while this session still described the previous one. This fires after identification and any
    /// invalidation have run, on the host's own execution context.
    /// </para>
    /// <para>
    /// That ordering is the point of #828. The host used to have to call
    /// <see cref="RefreshDaemonIdentityAndTakeChange"/> itself at the right moment, and calling it late or not at all
    /// was possible and silent.
    /// </para>
    /// </remarks>
    public event Action<DaemonChange>? Connected;

    /// <summary>The connection dropped. Raised on the host's execution context.</summary>
    public event Action? Disconnected;

    /// <summary>
    /// When true, an unexpected drop schedules an automatic reconnect.
    ///
    /// Cleared around a stop the user asked for, so "stopped" stays stopped rather than racing the
    /// daemon the user has just killed. Any explicit <see cref="ConnectAsync"/> turns it back on.
    /// </summary>
    public bool AutoReconnect
    {
        get => Connection.AutoReconnect;
        set => Connection.AutoReconnect = value;
    }

    /// <summary>Requests a connection. Fire-and-forget; <see cref="Connected"/> reports success.</summary>
    /// <param name="ct">Cancels the attempt, and the reconnect loop behind it.</param>
    public Task ConnectAsync(CancellationToken ct) => Connection.ConnectAsync(ct);

    /// <summary>
    /// The process id answering the connection right now, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// A fact about the connection, offered because stopping the daemon a host is actually talking to
    /// needs it. What that id means — whose daemon it is, whether to ask before stopping it — is the
    /// host's to decide, and this does not hand over any authority to make those decisions.
    ///
    /// <b>An observation, not a handle.</b> It describes the moment it was taken. The daemon can exit
    /// between this returning and a host acting on it, and the id can be reused by an unrelated process,
    /// so a stop issued against a stale value is a stop aimed at whatever holds that id now. Long-standing
    /// and not introduced by moving this here — recorded so the next caller does not assume otherwise.
    /// </remarks>
    /// <returns>The id as of this call.</returns>
    public int? ConnectedProcessId() => Connection.GetServerProcessId();

    /// <summary>
    /// The settings authority for this connection. One per session; a second call is refused.
    /// </summary>
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
    public IOtdSettingsSession OpenSettings(Func<bool> isOwnedDaemon,
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

        // DiscoverDestinationAsync is handed over at construction rather than assigned afterwards, and
        // is not invoked by it. It is how the coordinator asks again when a retry finds it still does not
        // know where to write -- the only rediscovery trigger there is, bounded by the user pressing
        // Retry rather than a background loop nobody asked for.
        var settings = _store is { } store
            ? new SettingsCoordinator(_channel, store, isOwnedDaemon, onSaveState, _log, _policy,
                                      DiscoverDestinationAsync)
            : new SettingsCoordinator(_channel, isOwnedDaemon, onSaveState, _log, _policy,
                                      DiscoverDestinationAsync);

        _settings = settings;

        // A connection established before the settings were opened has already had its transition, so
        // nothing would ask this one where to write. Asking here covers the ordinary startup order, in
        // which a host connects and then opens settings.
        if (_channel.Incarnation is var channel and not 0) LearnWhereToPersist(channel);

        return settings;
    }

    /// <summary>
    /// Asks the connected daemon where it keeps its settings, and records it against that channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library's own question now, and the point of #828's readiness half. It used to be the host's:
    /// OTA read <c>AppInfo</c> during its data load and handed the path back through a delegate. That
    /// load runs <em>after</em> the connection is usable, so between a daemon answering and the host
    /// noticing, the delegate returned the previous daemon's file — and an apply admitted in that window
    /// was live on the new daemon and written into the old one's settings.
    /// </para>
    /// <para>
    /// Fire-and-forget on purpose. This is a network call, and a transition must not wait behind one:
    /// #828 is explicit that serializing a short state change is not the same as monopolising the host's
    /// context for an entire RPC. Until the answer lands the connection simply has no destination, which
    /// the coordinator already treats as applied-but-not-saved — visible, retryable, and never the wrong
    /// file.
    /// </para>
    /// </remarks>
    private void LearnWhereToPersist(int channel) => _ = DiscoverDestinationAsync(channel);

    /// <summary>
    /// Asks the daemon on <paramref name="channel"/> where it keeps its settings, once at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single way discovery happens: a connection establishing, and a host retrying a save that has
    /// nowhere to go, both come through here. They used not to, so a retry could run alongside a
    /// transition's own lookup with nothing coordinating them.
    /// </para>
    /// <para>
    /// <b>Coalesced per channel, and only while a flight is still running.</b> The previous arrangement
    /// cleared the slot from the lookup's own <c>finally</c>, so a lookup that completed synchronously —
    /// which a failure does — cleared the slot before the caller had filled it, and the finished task
    /// then became the permanent answer: the daemon was never asked again, however many times the user
    /// pressed Retry. Nothing clears the slot now; a flight is replaced when a new one is needed, which
    /// is what the completion check decides. Keying on the channel matters separately — a caller asking
    /// about a new connection must not be handed an obsolete one's outstanding lookup.
    /// </para>
    /// <para>
    /// <b>The task completes when the answer has been recorded, not when it arrives.</b> Recording runs on
    /// the host's context, so a caller that awaits this and then reads the destination sees it. Completing
    /// at the reply meant an explicit retry had to be pressed twice: the first asked, and the second was
    /// the one that could act on it.
    /// </para>
    /// </remarks>
    private Task DiscoverDestinationAsync(int channel)
    {
        Lookup started;
        lock (_lookupGate)
        {
            // Joining what is already running, which is not new work: the flight it joins carries the one
            // registration. Counting the caller instead leaked one every time, because only the single
            // RunLookupAsync ever released it -- so a session whose work had all finished still reported a
            // timed-out close, for ten seconds by default and forever on an infinite window.
            if (_lookup is { Channel: var running, Done.Task: var task } && running == channel
                && !task.IsCompleted)
            {
                return task;
            }

            // Work this session starts on its own account, and it counts: the reply's continuation posts
            // through the host's execution context, which is exactly what a successful close tells the
            // caller it may now tear down. StillTheCurrentTransition stops an obsolete answer being
            // adopted; it does nothing about the call still being outstanding.
            if (!TryBeginLookup()) return Task.CompletedTask;

            // Deliberately NOT RunContinuationsAsynchronously, so that a continuation can run inline on
            // the context that completes this. That is a preference, not the guarantee: what keeps a
            // caller confined is its own synchronization context, which IOtdExecutionContext requires of
            // any host calling the asynchronous operations.
            started = new Lookup(channel, new TaskCompletionSource());
            _lookup = started;
        }

        // The second instance of the same mistake, found by auditing for the first: RunLookupAsync's
        // synchronous prefix reaches the publication gate whenever the RPC completes synchronously, so
        // starting it under _lookupGate held that lock across the wait for _publishGate -- and an inline
        // host callback holding _publishGate can reach here and want _lookupGate. Recorded under the gate
        // so coalescing still sees it; started outside.
        _ = RunLookupAsync(started);
        return started.Done.Task;
    }

    /// <summary>One outstanding destination lookup, and the channel it is about.</summary>
    private sealed record Lookup(int Channel, TaskCompletionSource Done);

    private readonly object _lookupGate = new();
    private Lookup? _lookup;

    private readonly object _liveGate = new();
    private int _liveLookups;
    private TaskCompletionSource? _lookupsQuiet;

    /// <summary>
    /// Makes giving up on the lookups and handing one to the host a single decision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading the flag under a lock and posting afterwards made them two, with a window between: a reply
    /// could pass the check, be descheduled, and post after the close that abandoned it had already
    /// returned. Another flag check only moves that window; the post has to be <em>initiated</em> under
    /// the same gate the abandonment takes, so abandonment happens wholly before the dispatch or wholly
    /// after it and there is no third possibility.
    /// </para>
    /// <para>
    /// <b>Its own lock, deliberately, and the innermost one.</b> Not <c>_liveGate</c>: an execution
    /// context may run posted work inline, so host code can run while this is held, and a host that
    /// re-enters the session would take <c>_closeGate</c> -- which <see cref="CloseAsync"/> already holds
    /// while <c>StopAdmitting</c> takes <c>_liveGate</c>. That is the inversion. Nothing acquires this
    /// while holding another of this session's locks, and only the publisher and the teardown acquire it
    /// at all.
    /// </para>
    /// <para>
    /// Held across <em>initiating</em> the post and never across awaiting it. With a dispatching context
    /// that is an enqueue; with one that runs work inline it is the action, so a teardown can wait on a
    /// host callback for that long. That is the cost of the guarantee, and it is bounded by the host's
    /// own post rather than by anything this library waits for.
    /// </para>
    /// </remarks>
    private readonly object _publishGate = new();

    private volatile bool _lookupsAbandoned;

    /// <summary>Registers a lookup, or refuses it because this session is closing.</summary>
    private bool TryBeginLookup()
    {
        lock (_liveGate)
        {
            if (_admissionStopped) return false;

            _liveLookups++;
            return true;
        }
    }

    private void EndLookup()
    {
        lock (_liveGate)
        {
            if (--_liveLookups == 0) _lookupsQuiet?.TrySetResult();
        }
    }

    /// <summary>Waits until no lookup is outstanding, or the time runs out.</summary>
    private async Task<bool> LookupsQuietAsync(TimeSpan within)
    {
        Task quiet;
        lock (_liveGate)
        {
            // Torn down already, by a Dispose. What is outstanding was abandoned with the transport.
            if (_lookupsAbandoned) return false;

            if (_liveLookups == 0) return true;

            _lookupsQuiet ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            quiet = _lookupsQuiet.Task;
        }

        try
        {
            // True means the wait ended, not that the work was welcome: an abandonment ends it too, and
            // RunCloseAsync's own !_tornDown is what turns that into the caller's answer.
            await quiet.WaitAsync(within).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    private async Task RunLookupAsync(Lookup flight)
    {
        // One cleanup for every way out of this method, including the early return below. It was two
        // statements in a finally guarding only the publication await, so an early return would have
        // skipped both -- leaking the registration this flight holds and leaving anyone awaiting it on a
        // task nobody would complete.
        try
        {
            string? path = null;
            Exception? failed = null;
            try
            {
                path = (await Connection.GetAppInfoAsync().ConfigureAwait(false))?.SettingsFile ?? "";
            }
            catch (Exception ex)
            {
                failed = ex;
                _log.Warn("Couldn't ask the connected daemon where it keeps its settings; nothing will "
                          + "be written to disk for this connection until it answers. Retrying a pending "
                          + "save asks again.", ex);
            }

            Task publication;

            _probe?.PublishingLookup?.Invoke();

            // The reply came back to a session that may have given up on it. Posting then would reach the
            // host context that a false close has just told the caller it may tear down, which is the
            // promise CloseAsync documents. The staleness check inside the posted action is not this and
            // cannot be: it runs after the post has arrived, having already done what was not allowed.
            //
            // Decided and dispatched under one gate -- see _publishGate for why checking and then posting
            // is not enough, and why this is not _liveGate.
            lock (_publishGate)
            {
                if (_lookupsAbandoned) return;

                // Reported through the host's context like everything else this session decides, and the
                // lookup is only finished once that has run -- or has been observed not to.
                publication = Report("record where the connected daemon keeps its settings", () =>
                {
                    try
                    {
                        // The answer describes the channel it was asked on. A reply arriving after that
                        // channel has gone says nothing about its replacement.
                        if (!StillTheCurrentTransition(flight.Channel)) return;

                        if (failed != null)
                        {
                            // Recorded rather than left blank, so a later attempt can tell "the call failed"
                            // from "nobody has asked yet" and from "this daemon has no settings file". They
                            // look the same from outside and want different responses.
                            _settings?.DestinationLookupFailed(flight.Channel);
                            return;
                        }

                        _settings?.LearnDestination(path ?? "", flight.Channel);
                    }
                    finally
                    {
                        // Settled here when the publication runs, which lets an awaiting continuation run
                        // inline on this context rather than wherever the reply happened to arrive. A
                        // permitted optimisation, not the guarantee -- what confines the caller is its own
                        // synchronization context, which IOtdExecutionContext requires of it.
                        flight.Done.TrySetResult();
                    }
                });
            }

            // Awaited outside the gate. Report never throws: it catches and logs, and awaiting it is how
            // this learns the attempt has concluded, however it concluded.
            await publication.ConfigureAwait(false);
        }
        finally
        {
            EndLookup();
            // The backstop, and the whole point of awaiting. A host can refuse a post -- shutting down,
            // or running work somewhere it does not consider its own -- and then the action above never
            // runs and never settles anything. Completing only from inside it left a retry awaiting a
            // task nobody would ever complete, and every later retry on this channel coalesced onto that
            // dead flight. Nothing is claimed by settling here: the destination stays unknown, the edit
            // stays pending, and the next retry starts a fresh lookup.
            flight.Done.TrySetResult();
        }
    }

    /// <summary>
    /// Looks at which daemon is answering and, if it is a different one, drops the state that belonged to
    /// the daemon that has gone.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// <b>A command, not an enquiry, and the name is deliberately long enough to say so.</b> It looks
    /// everything up, commits what it finds, calls host code doing it, and <b>consumes any pending
    /// discard obligation</b> — the returned change is the one and only report of it. It was called
    /// <c>NoteConnectedDaemon</c>, which read like a question; a test used it as an inert checkpoint and
    /// it quietly swallowed the state that test was asserting about. Nothing here is free of side
    /// effects.
    /// </para>
    /// <para>
    /// <b>Not a host's entry point.</b> The session subscribes to its own connection and does this itself
    /// on every transition (#828); this exists so that a test, or the switch-check tool, can drive the
    /// same decision without standing up a transport that transitions.
    /// </para>
    /// <para>
    /// <b>It assumes the connection holds still while it runs, and the automatic path does not.</b> A
    /// transition captures the channel it belongs to and refuses to commit or announce against any other;
    /// this captures nothing, so its only guard is that the session has not been disposed. If its locator
    /// or the logger reconnects the transport mid-call, it can commit an overtaken observation or
    /// announce for a connection that has gone. That is tolerable for what calls it — the tool's
    /// unreadable-daemon scenario holds one connection still throughout — and it would not be tolerable
    /// for general diagnostics. Anything reaching for it in a reentrant setting needs the capture and
    /// checks the transition path has.
    /// </para>
    /// <para>
    /// Internal for that reason. It was public while the host owned the trigger, and leaving it public
    /// afterwards would have left a mutating escape hatch into work the session has already done: a host
    /// calling it between a transport connecting and the posted identification running would consume the
    /// change, and the notification would then report nothing. The documentation here said the host owned
    /// the trigger for a round after that stopped being true.
    /// </para>
    /// <para>
    /// The judgement lives here rather than in the host because the state being dropped is this library's
    /// — the change a daemon accepted but never wrote, the file that change was for, what its settings
    /// file last held, whether it is running a transient override. None of that describes the new daemon,
    /// and each one misleads a different part of the host if carried across.
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
    internal DaemonChange RefreshDaemonIdentityAndTakeChange()
    {
        var commit = CommitConnectedDaemon(ConnectedDaemonPath());
        AnnounceCommit(commit, () => !_disposed);

        var change = new DaemonChange(commit.Actual, commit.Changed, _discardOwed);
        ClaimDiscard();                 // returned straight to the caller, who is a recipient
        return change;
    }

    /// <summary>
    /// Records <paramref name="actual"/> as the daemon this session knows, and reports what that cost.
    /// </summary>
    /// <remarks>
    /// Separate from the lookup so that a caller which can be overtaken -- the automatic identification
    /// on a transition -- has somewhere to check between the two. Everything here mutates: the remembered
    /// path, and the settings state a different daemon invalidates. None of it should happen on behalf of
    /// a transition that has already been superseded.
    /// </remarks>
    private DaemonCommit CommitConnectedDaemon(string? actual)
    {
        // Both conditions matter. No remembered path means this is the first look, and everything this
        // session holds already belongs to whatever is answering now. An unreadable path means we cannot
        // tell, which is not the same as knowing it is different.
        var previous = _daemonPath;
        var changed = previous.Length > 0 && actual != null && !PathEquality.Same(previous, actual);

        if (actual != null) _daemonPath = actual;
        // Work accepted by a channel this session has not identified yet belongs to the daemon arriving,
        // not the one leaving, so it survives the reset. Anything else -- including work on a channel
        // already identified, where a change of identity means one of the two readings was wrong -- does
        // not, because it cannot be attributed with any confidence.
        var survivor = _channel.Incarnation != _identifiedChannel ? _channel.Incarnation : 0;
        var discarded = changed && (_settings?.ResetForNewDaemon(survivor) ?? false);

        // A discard is owed to whoever is told next, not to this transition in particular. If this one is
        // superseded before it delivers, the edit is still gone and somebody has to hear about it.
        if (discarded) _discardOwed = true;

        _identifiedChannel = _channel.Incarnation;

        return new DaemonCommit(previous, actual, changed, discarded);
    }

    /// <summary>
    /// The channel this session last committed an identity for.
    /// </summary>
    /// <remarks>
    /// Only used to tell "this connection is new to me" from "I have looked at this one before", which is
    /// what decides whether an accepted-but-unsaved edit belongs to the daemon arriving or the one
    /// leaving. Zero before the first look, which is also what no channel reads as.
    /// </remarks>
    private int _identifiedChannel;

    /// <summary>What a commit did, before anyone outside the library has been told any of it.</summary>
    private readonly record struct DaemonCommit(string Previous, string? Actual, bool Changed, bool Discarded);

    /// <summary>
    /// Tells the host what a commit did, rechecking between each call out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from the commit, and separate <em>internally</em>, because these are two calls into host
    /// code and not one. The logger runs first and can do anything: dispose the session, or bring up
    /// another daemon whose transition completes and announces its own state. Announcing this commit's
    /// discard afterwards would then be an old notification arriving on a session that has moved on, or
    /// on one that is gone. Checking only after both had run was too late for the second.
    /// </para>
    /// <para>
    /// Skipping an announcement does not discard the fact. <c>_discardOwed</c> was set by the commit and
    /// is untouched here, so the edit remains owed to whoever is told next.
    /// </para>
    /// </remarks>
    private void AnnounceCommit(DaemonCommit commit, Func<bool> stillValid)
    {
        if (!commit.Changed || !stillValid()) return;

        _log.Warn($"The connected daemon changed from {commit.Previous} to {commit.Actual}; "
                  + "dropping settings state that belonged to the previous one.");

        if (!commit.Discarded || !stillValid()) return;

        _settings?.AnnounceDiscardedChange();
    }

    /// <summary>
    /// Whether an unsaved edit has been discarded and nobody has been told yet.
    /// </summary>
    /// <remarks>
    /// Carried across transitions because the information belongs to the host, not to the occasion that
    /// produced it. A transition that discards an edit and is then superseded before it delivers would
    /// otherwise take that fact with it: the surviving transition finds nothing pending to report, and
    /// the user's edit is gone with no notification anywhere.
    /// </remarks>
    private bool _discardOwed;

    /// <summary>Marks the discard as reported, because the change carrying it is being handed over.</summary>
    private void ClaimDiscard() => _discardOwed = false;

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
    /// Closes the connection without waiting. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <b>Does not settle work in flight</b>, because it cannot: there is nothing to await it on. An
    /// operation already running is left running, against a transport this is about to dispose.
    /// <see cref="CloseAsync"/> is the one that waits, and a host that can await should use it.
    ///
    /// The settings authority is deliberately not torn down, because it has nothing to release and
    /// something to answer: a host asking afterwards whether a change went unsaved should get the truth
    /// rather than an exception. Reading what already happened is allowed; starting something new is what
    /// <see cref="OpenSettings"/> refuses.
    /// </remarks>
    public void Dispose() => Close();

    /// <summary>How long <see cref="CloseAsync"/> waits for work in flight before closing anyway.</summary>
    private static readonly TimeSpan DefaultSettleWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Closes the connection, after letting work already in flight finish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What <see cref="Dispose"/> could never be: it cannot wait, so it closes under whatever is running
    /// and an apply that had reached the daemon can fail on its way to disk with nothing able to say
    /// whether it landed. A host that can await should use this.
    /// </para>
    /// <para>
    /// Refuse, settle, close — the order the switch-check pump arrived at for the same question. New work
    /// is refused first so the wait cannot chase an operation admitted behind it, then the settings
    /// session is given its window, then the transport goes.
    /// </para>
    /// <para>
    /// Bounded on purpose. The operation in flight may be waiting on a daemon that has stopped answering,
    /// and an application exiting cannot be held open by one. <see cref="Timeout.InfiniteTimeSpan"/> is
    /// accepted as the deliberate exception, for a caller who would rather hang than close under work --
    /// a test, or a tool with nothing else to do. It is not a default and should not be one: a host with a
    /// window to close wants an answer within a time it chose.
    /// </para>
    /// <para>
    /// <b>What a false answer leaves behind.</b> The work is abandoned, not cancelled: the RPC it is
    /// waiting on has already reached the daemon and cannot be recalled, so the task a caller is holding
    /// for it may complete long afterwards -- or never, if the daemon never answers. Three things follow,
    /// and a host that ignores them will see the symptoms rather than the cause:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Do not await those tasks after a false answer.</b> Awaiting one is the hang this method exists
    /// to bound, moved somewhere else. Drop them, or await them under a timeout of the caller's own.
    /// </description></item>
    /// <item><description>
    /// <b>Their results are not reported.</b> Abandoned work does not call back into the host: no save
    /// state, no settings event. What it returns to a caller still holding the task is what it found, and
    /// may describe a session that has gone.
    /// </description></item>
    /// <item><description>
    /// <b>They may still have landed.</b> A false answer says the session stopped waiting, not that the
    /// daemon did nothing. What it holds, and what reached disk, is unknown -- which is the whole reason
    /// the answer is a <c>bool</c> rather than nothing.
    /// </description></item>
    /// </list>
    /// <para>
    /// The second of those is a guarantee about <em>ordering</em>, and it costs one thing worth knowing:
    /// a reply that was already being handed to the host when the window ran out is not abandoned
    /// half-way. Closing waits for that handoff to finish, so that it cannot return while a post is still
    /// on its way. With a dispatching context the wait is an enqueue; with a context that runs posted
    /// work inline it is the host's own callback. Nothing else is waited for.
    /// </para>
    /// <para>
    /// <b>With <see cref="Dispose"/>.</b> One lifecycle, two entry points, and they may overlap. Calling
    /// this more than once returns the same operation and the same answer. Disposing while this is
    /// draining does not wait for it: it stops admission, abandons what is running and tears the transport
    /// down at once, and this is then released -- rather than spending the rest of its window on work the
    /// Dispose has already given up on -- answering false, because a close something else interrupted did
    /// not settle. Disposing first makes a call here answer false immediately, for the same reason: there
    /// is nothing left that waiting could settle.
    /// </para>
    /// </remarks>
    /// <param name="settleWithin">
    /// How long to wait. Defaults to ten seconds. <see cref="TimeSpan.Zero"/> closes without waiting;
    /// <see cref="Timeout.InfiniteTimeSpan"/> waits for as long as it takes.
    /// </param>
    /// <returns>
    /// True when everything in flight finished; false when the wait ran out, or a <see cref="Dispose"/>
    /// interrupted it, and it closed anyway. See the abandonment note above for what a false answer
    /// obliges the caller to do.
    /// </returns>
    public Task<bool> CloseAsync(TimeSpan? settleWithin = null)
    {
        var window = settleWithin ?? DefaultSettleWindow;

        // Validated before anything changes. An interval the wait rejects used to throw after the session
        // had already marked itself closed, so the transport was never disposed and the Dispose that
        // followed did nothing -- a bad argument leaving the connection open for the life of the process.
        if (window < TimeSpan.Zero && window != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(settleWithin), window,
                "A settle window must not be negative. Zero closes without waiting; "
                + "Timeout.InfiniteTimeSpan waits for as long as it takes.");
        }

        // One close, however many callers. The flag alone answered "true" to everyone after the first,
        // which conflated closing, closed-after-giving-up and everything-settled: a second teardown path
        // could be told the work had finished while it was still running.
        //
        // Published under the gate; run outside it. Calling an async method runs its synchronous prefix
        // on this thread, and a zero-window close reaches TearDown -- and so the publication gate --
        // without ever yielding. Starting it under _closeGate therefore held that lock across the wait
        // for _publishGate, which closes a cycle: an inline host callback running under _publishGate that
        // re-enters this method waits for _closeGate, which the closer will not release until it gets the
        // gate the callback is holding. TearDown ending its own _closeGate block first is not enough; it
        // is the caller's lock that matters.
        TaskCompletionSource<bool> mine;
        lock (_closeGate)
        {
            if (_closing is { } already) return already;

            mine = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _closing = mine.Task;
        }

        _ = SettleCloseAsync(mine, window);
        return mine.Task;
    }

    /// <summary>Runs the close and hands its answer, or its failure, to every caller sharing it.</summary>
    private async Task SettleCloseAsync(TaskCompletionSource<bool> result, TimeSpan window)
    {
        try
        {
            result.TrySetResult(await RunCloseAsync(window).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            // Shared like the answer is. Letting this escape would leave every caller waiting on a task
            // that never completes, and lose the exception on an unobserved one.
            result.TrySetException(ex);
        }
    }

    private readonly object _closeGate = new();
    private Task<bool>? _closing;
    private bool _admissionStopped;
    private bool _tornDown;

    private async Task<bool> RunCloseAsync(TimeSpan window)
    {
        StopAdmitting();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan Remaining()
        {
            if (window == Timeout.InfiniteTimeSpan) return window;

            var left = window - clock.Elapsed;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        try
        {
            // One deadline over both: the settings session's own operations, and the lookups this session
            // started for itself. Waiting for each in turn with the full window would make the worst case
            // twice what the caller asked for.
            var settled = _settings is not { } settings
                          || await settings.CloseAsync(Remaining()).ConfigureAwait(false);

            settled &= await LookupsQuietAsync(Remaining()).ConfigureAwait(false);

            // A Dispose that arrived while this was draining has already torn the transport down. What is
            // still running was abandoned, whatever the waits above found, and saying otherwise would
            // report a graceful settlement that something else interrupted.
            lock (_closeGate) return settled && !_tornDown;
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>
    /// Stops this session admitting new work, here and in the settings session it handed out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared by both ways of closing, and it was not: only the asynchronous close stopped the settings
    /// session, so a handle retained past a synchronous <c>Dispose</c> went on writing to disk for a
    /// session that had gone.
    /// </para>
    /// <para>
    /// The call into the settings session is belt and braces rather than the only thing holding it: both
    /// callers stop its admission by another route as well — <c>Close</c> through <c>Abandon</c>, and the
    /// asynchronous close through the coordinator's own close. Deleting it fails no test. It is here so
    /// the method is true to its name, and because "some other path happens to do it" is how the gap it
    /// fixes appeared in the first place.
    /// </para>
    /// </remarks>
    private void StopAdmitting()
    {
        lock (_liveGate) _admissionStopped = true;

        _disposed = true;
        _settings?.StopAdmitting();
    }

    /// <summary>Detaches and disposes the transport. Idempotent, and never waits.</summary>
    private void TearDown()
    {
        lock (_closeGate)
        {
            if (_tornDown) return;

            _tornDown = true;
        }

        // The transport is going, so nothing outstanding on it will be heard from. Latched under the
        // publish gate and outside the one above, so this waits for a dispatch already under way rather
        // than racing it -- and so a close cannot return while a reply that passed the check is still on
        // its way to the host.
        _probe?.ReachingTeardown?.Invoke();
        lock (_publishGate) _lookupsAbandoned = true;

        // A close waiting on those lookups is released here rather than left to spend its window on them.
        lock (_liveGate) _lookupsQuiet?.TrySetResult();

        Detach();
        Connection.Dispose();
    }

    /// <summary>Closes without waiting, for <see cref="Dispose"/>.</summary>
    /// <remarks>
    /// Runs even while an asynchronous close is draining, which it did not: the drain set the disposed
    /// flag first and this returned having done nothing, so a host that gave up on a graceful close and
    /// disposed could not make anything happen. Whatever is still running is abandoned -- it may finish,
    /// and nothing it does will reach the host.
    /// </remarks>
    private void Close()
    {
        StopAdmitting();
        _settings?.Abandon();
        TearDown();
    }

    /// <summary>
    /// Stops listening to the transport, before it is disposed.
    /// </summary>
    /// <remarks>
    /// Order matters: a drop raised during teardown would otherwise post work onto a context for a
    /// session that has gone.
    /// </remarks>
    private void Detach()
    {
        Connection.Connected -= OnTransportConnected;
        Connection.Disconnected -= OnTransportDisconnected;
    }
}
