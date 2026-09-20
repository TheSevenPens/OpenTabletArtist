using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What a host is told about a reconnect, and when (#828).
///
/// The host used to call <c>NoteConnectedDaemon</c> itself at the right moment, so calling it late or not
/// at all was possible and silent. The session subscribes to its own connection now and identifies the
/// daemon on the host's execution context.
///
/// These assert the ordering that makes that worth having, with the context <b>held</b> so that "while
/// identification is still pending" is a deliberate step rather than a race to lose.
/// </summary>
public class ReconnectOrderingTests
{
    /// <summary>
    /// A host hears about a connection only after the session has finished with it.
    ///
    /// The transport raises as soon as its channel is usable, which is before anything has looked at
    /// which daemon answered. A host acting on that would be acting while the session still described
    /// the previous one.
    /// </summary>
    [Fact]
    public void TheHostIsToldOnlyAfterIdentificationHasRun()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        h.Daemon.Reconnect();                   // the channel is up, and the transport has said so

        Assert.Empty(told);                   // ...and the host has not been told anything yet
        Assert.Equal(1, h.Context.Pending);

        h.Context.Drain();

        Assert.Equal("A/OpenTabletDriver.Daemon.exe", Assert.Single(told).ExecutablePath);
    }

    /// <summary>
    /// The change handed over says what the session did, and asking afterwards finds nothing left.
    ///
    /// The host is not sent to find out separately, and cannot get a different answer by asking later —
    /// which is what made the old arrangement fragile.
    /// </summary>
    [Fact]
    public async Task TheChangeHandedOverSaysWhatTheSessionDiscarded()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        // An edit the daemon took and the disk refused, so there is something to lose.
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await h.Settings.ApplyAndSaveAsync(Tablet("edited"))).Status);

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        var change = Assert.Single(told);
        Assert.True(change.Changed);
        Assert.True(change.DiscardedUnsavedChange);

        Assert.False(h.Session.NoteConnectedDaemon().Changed);
    }

    /// <summary>
    /// A to B to C, with nothing drained between them: <b>one</b> notification, naming C.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A queued notification belongs to the channel that raised it. B's has been superseded by the time
    /// the context reaches it, so it is discarded rather than run against today's world.
    /// </para>
    /// <para>
    /// I had this wrong twice. First I asserted "B then C", which is unachievable — identification reads
    /// the world when it runs, so naming B would name a daemon that no longer exists. Then I asserted
    /// "C twice", and defended it as merely redundant. It is not: the second notification replays a
    /// historical transition with today's identity, which reports a transition that never happened as
    /// though it had. A host acting on it — OTA starts a data load — is acting on an occasion that did
    /// not occur.
    /// </para>
    /// </remarks>
    [Fact]
    public void RapidTransitions_ReportOnlyTheOneThatSurvived()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.MoveTo("C/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        var change = Assert.Single(told);
        Assert.Equal("C/OpenTabletDriver.Daemon.exe", change.ExecutablePath);
        Assert.True(change.Changed);
    }

    /// <summary>
    /// A connection that has already gone is not reported as connected.
    ///
    /// The transport says it has a channel; it drops before the host's context gets to the notification.
    /// Raising Connected then tells a host it is connected when it is not — and OTA answers that by
    /// setting <c>IsConnected</c> and starting a data load against nothing.
    /// </summary>
    [Fact]
    public void AConnectionThatHasAlreadyGone_IsNotReportedAsConnected()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        h.Daemon.Reconnect();
        h.Daemon.RaiseDisconnected();
        h.Context.Drain();

        Assert.Empty(told);
    }

    /// <summary>
    /// Work already queued when disposal happens does not run afterwards.
    ///
    /// Unsubscribing from the transport does not cover this: the notification had already been posted.
    /// And the assumption that a host has stopped listening by then is not true of OTA, which subscribes
    /// with lambdas it never detaches and disposes its load gate and cancellation source first.
    /// </summary>
    [Fact]
    public void WorkQueuedBeforeDisposal_DoesNotRunAfterIt()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        h.Daemon.Reconnect();
        h.Session.Dispose();
        h.Context.Drain();

        Assert.Empty(told);
    }

    /// <summary>
    /// A disposed session posts nothing further, so a drop arriving during teardown cannot run work
    /// against a session that has gone.
    /// </summary>
    [Fact]
    public void AfterDisposal_ATransitionPostsNothing()
    {
        var h = Make();
        h.Session.Dispose();

        h.Daemon.Reconnect();

        Assert.Equal(0, h.Context.Pending);
    }

    /// <summary>
    /// A subscriber that throws is reported, not swallowed.
    /// </summary>
    /// <remarks>
    /// Posted work has no caller to throw to — this is reached from the transport's own notification —
    /// so without a report the failure would vanish entirely.
    ///
    /// It is <c>Deliver</c> that catches this, not <c>Report</c>. The comment here used to say the latter,
    /// which was true when this was written and stopped being true the moment delivery started isolating
    /// subscribers from each other: nothing a subscriber throws reaches the posted work's caller any more.
    /// <see cref="WorkTheHostsContextRefuses_IsReported"/> is what covers the other path.
    /// </remarks>
    [Fact]
    public void ASubscriberThatThrows_IsReported()
    {
        var log = new RecordingLog();
        var h = Make(log);
        h.Session.Connected += _ => throw new InvalidOperationException("a bad subscriber");

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        Assert.Contains(log.Warnings, w => w.Contains("a bad subscriber"));
    }

    /// <summary>
    /// A subscriber that throws does not stop the ones after it hearing.
    ///
    /// A plain multicast invoke stops at the first throw, so later subscribers never hear about the
    /// connection — and which ones depends on subscription order. I claimed a bad subscriber "loses its
    /// notification and nothing else" while that was untrue.
    /// </summary>
    [Fact]
    public void ASubscriberThatThrows_DoesNotSilenceTheOthers()
    {
        var h = Make(new RecordingLog());
        var second = 0;
        h.Session.Connected += _ => throw new InvalidOperationException("a bad subscriber");
        h.Session.Connected += _ => second++;

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        Assert.Equal(1, second);
    }

    /// <summary>
    /// A subscriber that throws does not undo what the session already did.
    ///
    /// The identification and any invalidation happen before the host is told, so a bad subscriber can
    /// lose its own notification and nothing else.
    /// </summary>
    [Fact]
    public void ASubscriberThatThrows_DoesNotUndoTheInvalidation()
    {
        var h = Make();
        h.Session.Connected += _ => throw new InvalidOperationException("a bad subscriber");

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        // The session is on B, so asking again reports no further change.
        Assert.False(h.Session.NoteConnectedDaemon().Changed);
    }

    /// <summary>
    /// A read held across a reconnect: the host is told about the new daemon while the old one's answer is
    /// still outstanding, and that answer is then discarded rather than adopted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case that makes the execution context worth requiring. A settings read is not instantaneous,
    /// and a daemon switch does not wait for it -- so there is a real interval in which a call that went
    /// to A is still in flight and B is already answering. Adopting A's reply there would make B's
    /// baseline describe a daemon that is no longer connected, and the next edit would be built on it and
    /// sent to B.
    /// </para>
    /// <para>
    /// Both halves are asserted because either alone is satisfiable by accident: a session that told the
    /// host nothing would also never adopt, and one that adopted everything would still report the
    /// transition.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AReadHeldAcrossAReconnect_IsDiscarded_AndTheHostHearsWhileItIsStillInFlight()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        var a = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Daemon.GetSettingsHandler = () => a.Task;

        var held = h.Settings.ReloadFromDaemonAsync();       // asked A, and A has not answered
        Assert.False(held.IsCompleted);

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");           // the daemon changes underneath it

        Assert.Empty(told);                                  // still behind the context, as everything is
        h.Context.Drain();
        Assert.Equal("B/OpenTabletDriver.Daemon.exe", Assert.Single(told).ExecutablePath);

        a.SetResult(new Settings());                         // A answers at last
        var outcome = await held;

        Assert.Equal(SettingsReloadStatus.Overtaken, outcome.Status);
        Assert.Null(outcome.Adopted);
    }

    /// <summary>
    /// A read held across a reconnect that <b>nothing announced</b> is still discarded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case the channel hold exists for, and the one the epoch cannot cover. An announced reconnect
    /// invalidates, which moves the observation epoch, so a read in flight is caught by that alone -- and
    /// the sibling test above passes with the channel check deleted. A silent reconnect moves nothing:
    /// the transport replaced its channel and no notification has run yet, so the epoch the read recorded
    /// is still current and the only thing that knows is the hold the read was taken through.
    /// </para>
    /// <para>
    /// Found by mutation. Removing the channel check in <c>ReloadFromDaemonAsync</c> left the entire suite
    /// green, which made a guard whose own comment explains why the epoch is insufficient into one that
    /// nothing held.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AReadHeldAcrossASilentReconnect_IsDiscarded()
    {
        var h = Make();

        var a = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Daemon.GetSettingsHandler = () => a.Task;

        var held = h.Settings.ReloadFromDaemonAsync();
        Assert.False(held.IsCompleted);

        h.Daemon.ReconnectSilently();               // a new channel, and nothing has said so
        Assert.Equal(0, h.Context.Pending);        // nothing was posted, so no epoch will move

        a.SetResult(new Settings());
        var outcome = await held;

        Assert.Equal(SettingsReloadStatus.Overtaken, outcome.Status);
        Assert.Null(outcome.Adopted);
    }

    /// <summary>
    /// A host whose context runs the library's work somewhere it does not consider its own is told so.
    /// </summary>
    /// <remarks>
    /// The only place this is checkable: work that IS on the context, asking the context whether it is.
    /// The library cannot verify a host's threading from outside, so the whole arrangement rests on the
    /// host keeping its word -- and a promise nothing ever checks is how this would fail silently.
    /// </remarks>
    [Fact]
    public void AContextThatRunsWorkSomewhereElse_IsRefused()
    {
        var log = new RecordingLog();
        var h = Make(log);
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;
        var lookupsBefore = h.Locator.PathOfCalls;

        h.Context.IsCurrent = false;                         // the host's context breaks its own promise

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        Assert.Contains(log.Warnings, w => w.Contains("Refused to"));

        // Reporting it is not the point; not doing the work is. The session has just established that
        // the serialization it requires is absent, and identification mutates the state that
        // serialization protects.
        Assert.Equal(lookupsBefore, h.Locator.PathOfCalls);
        Assert.Empty(told);
    }

    /// <summary>
    /// A host that disposes its session from inside the process locator is not then told about the
    /// connection.
    /// </summary>
    /// <remarks>
    /// Identification calls out to host-supplied code, which may do anything — including tear the session
    /// down. Checking validity only on the way into the posted work meant that call came back to a
    /// disposed session and delivered anyway. Codex's probe; it failed before the recheck was added.
    /// </remarks>
    [Fact]
    public void ASessionDisposedWhileIdentifying_TellsNobody()
    {
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var disposing = new DisposeDuringLookup(locator);
        var daemon = new FakeDaemonTransport { ServerProcessId = 1 };
        var context = new ControllableContext();
        var session = OtdSession.ForTesting(daemon, new RefusingStore(), NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, disposing, context);
        session.NoteConnectedDaemon();              // establish A, with the locator still harmless

        var told = new List<DaemonChange>();
        session.Connected += told.Add;

        disposing.Session = session;               // armed only now, so setup does not trip it
        locator.Path = "B/OpenTabletDriver.Daemon.exe";
        daemon.Reconnect();
        context.Drain();

        Assert.Empty(told);
    }

    /// <summary>
    /// A transition overtaken <b>during</b> identification commits nothing, so the one that actually
    /// happened is still reported as a change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this prevents is worse than a lost notification, which is why suppressing the event
    /// alone was not enough. The lookup calls out to the host's locator; if a new daemon arrives while it
    /// is running, committing what it found records this session as already being on that daemon. The
    /// real transition then compares equal to what is recorded and reports no change — so the host is
    /// never told about the daemon it is now talking to, and never reloads for it.
    /// </para>
    /// <para>
    /// Written after mutation showed the recheck I had added was redundant: <c>Deliver</c>'s own
    /// per-subscriber check already covered the disposal case, so deleting the recheck changed nothing.
    /// It was guarding the wrong boundary.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATransitionOvertakenWhileIdentifying_CommitsNothing()
    {
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var daemon = new FakeDaemonTransport { ServerProcessId = 1 };
        var context = new ControllableContext();
        var overtaking = new ReconnectDuringLookup(locator, daemon);
        var session = OtdSession.ForTesting(daemon, new RefusingStore(), NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, overtaking, context);
        session.NoteConnectedDaemon();             // A established, with the locator still harmless

        var told = new List<DaemonChange>();
        session.Connected += told.Add;

        overtaking.Arm("C/OpenTabletDriver.Daemon.exe");
        daemon.Reconnect();                        // B's transition, which C will overtake mid-lookup
        context.Drain();

        // One notification, naming C, and reported as a change. Commit the overtaken lookup instead and
        // this is a single notification naming C with Changed false: the host sees a connection to a
        // daemon it is never told is a different one.
        var only = Assert.Single(told);
        Assert.Equal("C/OpenTabletDriver.Daemon.exe", only.ExecutablePath);
        Assert.True(only.Changed);
    }

    /// <summary>
    /// A subscriber that disposes the session stops the ones after it being told.
    /// </summary>
    /// <remarks>
    /// Isolating subscribers from each other's exceptions is not a reason to keep announcing a connection
    /// that has since gone. This is the distinction between "the previous subscriber failed" — carry on —
    /// and "the previous subscriber made this untrue" — stop. Codex's probe; the second subscriber used
    /// to be told regardless.
    /// </remarks>
    [Fact]
    public void ASubscriberThatDisposesTheSession_StopsTheOnesAfterIt()
    {
        var h = Make();
        var second = 0;
        h.Session.Connected += _ => h.Session.Dispose();
        h.Session.Connected += _ => second++;

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        Assert.Equal(0, second);
    }

    /// <summary>
    /// A queued disconnect whose successor has already connected is not reported.
    /// </summary>
    /// <remarks>
    /// The disconnect path used to check only disposal. A drop and a reconnect both landing before the
    /// host's context ran either would tell a host it was disconnected while a channel was up — and OTA
    /// clears what it shows on that. Nothing is captured at the raise, because a drop has no channel of
    /// its own to name: what makes it obsolete is that a newer one exists.
    /// </remarks>
    [Fact]
    public void ADisconnectOvertakenByAReconnect_IsNotReported()
    {
        var h = Make();
        var disconnects = 0;
        h.Session.Disconnected += () => disconnects++;

        h.Daemon.RaiseDisconnected();               // queued behind the context
        h.Daemon.Reconnect();                       // and a new channel arrives before it runs
        h.Context.Drain();

        Assert.Equal(0, disconnects);
    }

    /// <summary>A disconnect that nothing overtook is still reported.</summary>
    /// <remarks>
    /// The other half, without which the test above is satisfied by never reporting a disconnect at all.
    /// </remarks>
    [Fact]
    public void ADisconnectThatStands_IsReported()
    {
        var h = Make();
        var disconnects = 0;
        h.Session.Disconnected += () => disconnects++;

        h.Daemon.RaiseDisconnected();
        h.Context.Drain();

        Assert.Equal(1, disconnects);
    }

    // --- harness --------------------------------------------------------------------------------

    /// <summary>A locator that disposes a session while it is being asked who is answering.</summary>
    /// <remarks>
    /// The shape of a host that tears down from inside its own callback, which is ordinary during
    /// shutdown: the transport reconnects, identification asks the host where the daemon lives, and the
    /// host is already on its way out.
    /// </remarks>
    /// <summary>A locator that lets a different daemon take the connection while it is being asked.</summary>
    /// <remarks>
    /// The window is real and needs no contrivance to reach: the lookup reads a process id from the live
    /// connection and resolves it through the host, and a daemon can be stopped and replaced throughout.
    /// Arming it explicitly is only so the test's own setup does not trip it.
    /// </remarks>
    private sealed class ReconnectDuringLookup(FakeProcessLocator inner, FakeDaemonTransport daemon)
        : IDaemonProcessLocator
    {
        private string? _becomes;

        /// <summary>The next lookup brings up <paramref name="path"/> on a new channel while it runs.</summary>
        public void Arm(string path) => _becomes = path;

        public string? PathOf(int processId)
        {
            if (_becomes is not { } next) return inner.PathOf(processId);

            _becomes = null;
            inner.Path = next;
            daemon.Reconnect();                    // a new channel, and its own queued transition
            return inner.PathOf(processId);
        }

        public string? SingleRunningDaemonPath() => inner.SingleRunningDaemonPath();
    }

    private sealed class DisposeDuringLookup(IDaemonProcessLocator inner) : IDaemonProcessLocator
    {
        /// <summary>
        /// The session to dispose, or null to behave. Assigned after construction, because the session
        /// needs this locator to exist first — and because the test's own setup asks for a lookup.
        /// </summary>
        public OtdSession? Session { get; set; }

        public string? PathOf(int processId)
        {
            Session?.Dispose();
            return inner.PathOf(processId);
        }

        public string? SingleRunningDaemonPath() => inner.SingleRunningDaemonPath();
    }

    /// <summary>
    /// A subscriber that throws is still reported when the host's context runs the work on another
    /// thread and completes its task there — which is what a real one does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other test here drives a context that runs and completes inline, so the whole notification
    /// path has already finished by the time <c>Drain</c> returns. That is a legitimate way to drive the
    /// session and it is not the arrangement that ships: Avalonia's dispatcher runs the work on the UI
    /// thread and completes its task there, long after the posting call returned.
    /// </para>
    /// <para>
    /// What this adds over its inline sibling is that the path is exercised with real concurrency between
    /// the transport's thread, the context's thread and the test's — so the assertion is that the report
    /// arrives at all, rather than that it arrives before a synchronous drain returns.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASubscriberThatThrows_IsReportedWhenTheContextCompletesAsynchronously()
    {
        var log = new SignallingLog();
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var daemon = new FakeDaemonTransport { ServerProcessId = 1 };
        using var context = new ElsewhereContext();
        using var session = OtdSession.ForTesting(daemon, new RefusingStore(), log,
            OtaSettingsPolicy.Instance, locator, context);
        session.NoteConnectedDaemon();
        session.Connected += _ => throw new InvalidOperationException("a bad subscriber");

        // Registered before the transition, because the warning may arrive before the next line runs.
        // Waiting on "the first warning" would not do: identifying the new daemon legitimately logs one
        // of its own, and this test would then assert against that and pass whatever happened here.
        var reported = log.Expect(w => w.Contains("a bad subscriber"));

        locator.Path = "B/OpenTabletDriver.Daemon.exe";
        daemon.Reconnect();

        Assert.Contains("a bad subscriber", await reported.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Work the host's execution context refuses is reported rather than lost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason <see cref="IOtdExecutionContext.PostAsync"/> returns a task at all. This is reached from
    /// the transport's own notification, so there is no caller to throw to, and the session posts it
    /// fire-and-forget — a faulted task nobody observed would take the failure with it.
    /// </para>
    /// <para>
    /// Not hypothetical: a dispatcher rejects work once its host has begun shutting down, which is exactly
    /// when a transport is likely to be dropping and raising. Refusing asynchronously is the realistic
    /// shape, since a dispatcher accepts the call and fails the task afterwards.
    /// </para>
    /// <para>
    /// Written because mutation found nothing holding this. Deleting the report in <c>Report</c> left the
    /// whole suite green: both subscriber tests were passing through <c>Deliver</c>'s catch, which now
    /// takes the subscriber exception before it can reach here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WorkTheHostsContextRefuses_IsReported()
    {
        var log = new SignallingLog();
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var daemon = new FakeDaemonTransport { ServerProcessId = 1 };
        var context = new DeferringContext();
        using var session = OtdSession.ForTesting(daemon, new RefusingStore(), log,
            OtaSettingsPolicy.Instance, locator, context);
        session.NoteConnectedDaemon();

        var reported = log.Expect(w => w.Contains("identify the connected daemon"));

        daemon.Reconnect();                         // accepted, and the posting call has returned

        // Parked at the await, with nothing yet to report. This is what makes the test about the
        // asynchronously-faulted post rather than about a synchronous throw that happened to be caught.
        Assert.False(reported.IsCompleted);

        context.Fail(new InvalidOperationException("this host is done"));

        Assert.Contains("this host is done",
            await reported.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    // --- harness --------------------------------------------------------------------------------

    /// <summary>
    /// A context that runs posted work somewhere else and completes its task there, like a real one.
    /// </summary>
    /// <remarks>
    /// One dedicated thread rather than the pool, so <see cref="IsCurrent"/> can answer honestly: a
    /// context that reported false for its own work would trip the library's access check and make this
    /// test about the wrong thing.
    /// </remarks>
    private sealed class ElsewhereContext : IOtdExecutionContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = new();
        private readonly System.Threading.Thread _thread;

        public ElsewhereContext()
        {
            _thread = new System.Threading.Thread(() =>
            {
                foreach (var work in _queue.GetConsumingEnumerable()) work();
            })
            { IsBackground = true, Name = "test-elsewhere" };
            _thread.Start();
        }

        public bool IsCurrent => System.Threading.Thread.CurrentThread == _thread;

        public Task PostAsync(Action work)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try { work(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
            });
            return done.Task;
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    /// <summary>
    /// A context that accepts work, never runs it, and fails it when the test says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deterministically forces the session's reporting path through an <b>incomplete</b> await: the post
    /// returns a pending task, the call that made it returns, and only then does anything go wrong. That
    /// is the case a real dispatcher produces when its host is shutting down, and it is the one a
    /// synchronous throw cannot stand in for.
    /// </para>
    /// <para>
    /// No thread of its own, which is the point. The threaded context alongside covers thread affinity;
    /// this covers ordering, and ordering should not be established by winning a race.
    /// </para>
    /// </remarks>
    private sealed class DeferringContext : IOtdExecutionContext
    {
        private TaskCompletionSource? _posted;

        /// <summary>True, because the host is not what is being faulted here — the post is.</summary>
        public bool IsCurrent => true;

        public Task PostAsync(Action work)
        {
            _posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _posted.Task;
        }

        /// <summary>Fails the outstanding post, as a dispatcher does once it will take no more.</summary>
        public void Fail(Exception why) => _posted!.SetException(why);
    }

    /// <summary>A log a test can wait on for a particular warning, since reporting is fire-and-forget.</summary>
    /// <remarks>
    /// Written from the context's thread and read from the test's, so a completion source does the
    /// synchronising rather than a field the test polls: polling would pass on a slow machine for the
    /// wrong reason and fail on a fast one for no reason.
    ///
    /// Matching rather than taking the first, because identifying a new daemon logs a warning of its own
    /// and it wins the race. A test that waited for whatever came first would assert against that and
    /// stop being about the subscriber at all.
    /// </remarks>
    private sealed class SignallingLog : IOtdLog
    {
        private readonly object _gate = new();
        private readonly List<string> _warnings = new();
        private Func<string, bool>? _wanted;
        private TaskCompletionSource<string>? _waiting;

        /// <summary>Completes with the first warning matching <paramref name="predicate"/>, past or future.</summary>
        public Task<string> Expect(Func<string, bool> predicate)
        {
            lock (_gate)
            {
                foreach (var seen in _warnings)
                    if (predicate(seen))
                        return Task.FromResult(seen);

                _wanted = predicate;
                _waiting = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _waiting.Task;
            }
        }

        public void Warn(string message, Exception? error = null)
        {
            var line = error == null ? message : $"{message} :: {error.Message}";
            lock (_gate)
            {
                _warnings.Add(line);
                if (_wanted?.Invoke(line) == true) _waiting!.TrySetResult(line);
            }
        }

        public void Info(string message) { }

        public void Debug(string message, Exception? error = null) { }
    }

    private sealed class RefusingStore : ISettingsFileStore
    {
        public void Save(Settings s, string p) { }
        public bool TrySave(Settings s, string p) => false;
        public bool TryLoad(string p, out Settings? s) { s = null; return false; }
    }

    /// <summary>Everything a test needs to move the world underneath the session.</summary>
    private sealed record Harness(
        OtdSession Session,
        FakeDaemonTransport Daemon,
        FakeProcessLocator Locator,
        ControllableContext Context,
        IOtdSettingsSession Settings)
    {
        /// <summary>A new channel answering as a different executable: one reconnect, one identity change.</summary>
        public void MoveTo(string path)
        {
            Locator.Path = path;
            Daemon.Reconnect();
        }
    }

    private static Harness Make(IOtdLog? log = null)
    {
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var daemon = new FakeDaemonTransport { ServerProcessId = 1 };
        var context = new ControllableContext();
        var session = OtdSession.ForTesting(daemon, new RefusingStore(), log ?? NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, locator, context);
        var settings = session.OpenSettings(() => "A/settings.json", () => true, _ => { });
        session.NoteConnectedDaemon();              // establish A as the daemon this session knows
        return new Harness(session, daemon, locator, context, settings);
    }

    private static Settings Tablet(string n) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = n } } };
}
