using System;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// Owns the settings OTA believes in, and every way they reach the daemon or the disk (#740).
///
/// Extracted from the host session, which still implements the host-facing settings contract and
/// delegates here — so no consumer changed. What moved is the state that makes the apply path hard to
/// reason about when it is interleaved with connection lifecycle, data loading and daemon process
/// control: the current settings, the two separate revision baselines from #734, the pending unsaved
/// change, the per-app override flag from #737, and the apply-loop circuit breaker.
///
/// Headless on purpose, which is not the same as thread-safe. Mutating operations are serialized
/// against each other; several members read and write session state outside that, so this relies on the
/// host supplying one serialized execution context, exactly as <see cref="IOtdSettingsSession"/>
/// describes. <c>AppSession</c> supplies Avalonia's UI thread and keeps the
/// <c>Dispatcher.UIThread.VerifyAccess()</c> guards on its own entry points; a test supplies a single
/// thread. What being headless buys is testability without a dispatcher, not freedom from the
/// requirement.
///
/// It also holds no reference back to the session. Apply-then-reload is orchestrated by the caller, so
/// the dependency runs one way.
/// </summary>
internal sealed class SettingsCoordinator : IOtdSettingsSession
{
    private readonly IDaemonSettingsChannel _daemon;
    // Injected rather than a static call to the app's logger: what this type reports is mostly partial
    // failure -- live but unsaved, discarded because the daemon changed -- and those are exactly the
    // events nobody can see from outside. Where the lines go is the host's decision, not this type's.
    private readonly IOtdLog _log;
    private readonly ISettingsFileStore _store;
    /// <summary>
    /// Where this connection's settings live, and which channel that was read on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bound to a channel because the path alone cannot tell two daemons apart. Verified against the two
    /// real installs this is tested with: both report
    /// <c>%LOCALAPPDATA%\OpenTabletDriver\settings.json</c>, so comparing paths would call a switch
    /// between them no change at all.
    /// </para>
    /// <para>
    /// Null until something has read it for the current channel, and never inherited across one. That is
    /// the readiness #828 asks for: a connection is not ready to be persisted to until this session knows
    /// where to persist, and the previous daemon's answer is not a guess worth making.
    /// </para>
    /// </remarks>
    private Destination? _destination;

    /// <summary>The settings file for one channel, as that channel itself reported it.</summary>
    private sealed record Destination(DestinationKnowledge Knowledge, string Path, int Channel);

    /// <summary>What this session found out when it asked a channel where it keeps its settings.</summary>
    /// <remarks>
    /// Three different situations produced the same empty path and therefore the same message, which
    /// claimed the daemon had reported no settings file even when nothing had asked it yet. They differ
    /// in what a host should do: wait, try again, or accept that this daemon has no file.
    /// </remarks>
    internal enum DestinationKnowledge
    {
        /// <summary>Asked, and the answer has not come back. It may still.</summary>
        Pending,

        /// <summary>Asked, and the call failed. Nothing will change without asking again.</summary>
        Unavailable,

        /// <summary>Answered, and this daemon has no settings file. Permanent for this connection.</summary>
        NoFile,

        /// <summary>Answered with a path.</summary>
        Known,
    }

    /// <summary>
    /// Records where the daemon on <paramref name="channel"/> keeps its settings.
    /// </summary>
    /// <remarks>
    /// Called by the session that owns this coordinator, once it has asked the daemon. Not a host entry
    /// point: a host supplying this was the defect, because a host learns it from a data load that runs
    /// after the connection is already usable, so there was a window in which the answer was the previous
    /// daemon's.
    /// </remarks>
    internal void LearnDestination(string path, int channel) =>
        _destination = new Destination(
            string.IsNullOrEmpty(path) ? DestinationKnowledge.NoFile : DestinationKnowledge.Known,
            path, channel);

    /// <summary>Records that asking <paramref name="channel"/> failed, so a retry has something to fix.</summary>
    internal void DestinationLookupFailed(int channel) =>
        _destination = new Destination(DestinationKnowledge.Unavailable, "", channel);

    /// <summary>What is known about where work bound to <paramref name="origin"/> should be written.</summary>
    private DestinationKnowledge KnowledgeFor(Origin origin) =>
        _destination is { } known && known.Channel == origin.Channel.Incarnation
            ? known.Knowledge
            : DestinationKnowledge.Pending;

    /// <summary>
    /// Asks the session to look again, when looking is the thing that has not happened.
    /// </summary>
    /// <remarks>
    /// Bounded by the user: this runs on an explicit retry and nowhere else, so a daemon that never
    /// answers costs one call per attempt rather than a background loop nobody asked for. Without it a
    /// single failed lookup left the connection unable to persist for its whole life, and the save chip's
    /// Retry only ever retried the disk.
    ///
    /// Taken at construction and never reassigned. It was a settable property because the session creates
    /// this and this has to call back into the session, but a method group passes through a constructor
    /// perfectly well, and a type whose whole design is one authority should not also offer a way in.
    /// </remarks>
    private readonly Func<int, Task>? _rediscoverDestination;

    /// <summary>
    /// Where work bound to <paramref name="origin"/> should be written, or empty when this session does
    /// not yet know.
    /// </summary>
    /// <remarks>
    /// Empty is an ordinary answer and already has a meaning here: applied but not saved. It is what a
    /// daemon reporting no settings file has always produced, and the same treatment is right for a
    /// daemon that has not been asked yet — live on the daemon, not on any disk, and visibly so.
    /// </remarks>
    private string DestinationFor(Origin origin) =>
        _destination is { } known && known.Channel == origin.Channel.Incarnation ? known.Path : "";

    /// <summary>Says which of the three kinds of "nowhere to write" this is, for a log a human reads.</summary>
    private string WhyNowhereToWrite(Origin origin) => KnowledgeFor(origin) switch
    {
        DestinationKnowledge.NoFile => "the daemon reported no settings file path",
        DestinationKnowledge.Unavailable => "the daemon could not be asked where it keeps its settings",
        _ => "this session has not yet been told where the connected daemon keeps its settings",
    };
    private readonly Func<bool> _isOwnedDaemon;
    // The host's own rules about what may be written. This type does not hold an opinion about which
    // third-party filters an application tolerates -- that is a product decision about that
    // application's users -- but the decision has to take effect inside the operation, after the
    // request is isolated and before anything is sent (#807).
    private readonly IOtdSettingsPolicy _policy;
    private readonly Action<SettingsSaveState> _onSaveState;

    // Apply-loop hardening (#applyloop): a serialized snapshot of the settings as last loaded from the
    // daemon (the no-op guard: applying byte-identical settings is skipped), and a circuit-breaker that
    // stops a runaway apply↔reload loop from hanging the app (a safety net behind the #433 class of bug).
    private string? _lastLoadedSettingsJson;
    private readonly ApplyLoopBreaker _applyLoopBreaker = new();

    // The last settings we actually got onto DISK, tracked separately from what the daemon last handed
    // back (#734). Conflating the two meant a failed write was recorded as the baseline on the next
    // reload, so re-applying the same settings hit the no-op guard and the save was never retried.
    private string? _lastPersistedSettingsJson;
    // Applied by the daemon but not yet persisted — what RetryPersistAsync would write.
    private Settings? _pendingPersistSettings;
    // ...and the file it was meant for (#787). The destination is resolved late, from the *connected*
    // daemon's AppInfo, so without recording where the change belongs a retry follows whichever daemon
    // is connected when it happens to run — writing one daemon's unsaved settings into another's file.
    /// <summary>
    /// Where a pending write belongs: the channel that accepted it, and its file if that was known then.
    /// </summary>
    /// <remarks>
    /// A bare path could not say "no destination was known when this was accepted", because the empty
    /// string it stored for that was then compared against the real path and read as a destination that
    /// had <em>moved</em> — so a retry discarded the edit under a rule written for a daemon that had
    /// gone. Not known yet and known-and-changed are different facts, and they were the same value.
    /// </remarks>
    private PendingDestination? _pendingDestination;

    /// <param name="Channel">The channel whose daemon accepted this change.</param>
    /// <param name="Path">Where it was to be written, or null when nothing knew yet.</param>
    private sealed record PendingDestination(int Channel, string? Path);

    // Automatic retries spent on the current pending change (#743). Bounded: the common causes of a
    // refused write are a momentary file lock (OTD's own UX saving the same file), which clears within a
    // reload or two, and a permission problem, which never clears on its own. Retrying the second case
    // forever would log a warning every poll for as long as the app is open, so it stops and leaves the
    // failure standing. Any new edit starts the count again.
    private int _automaticRetries;
    // Generous on purpose. Reloads run on window focus as well as the 30-second poll, and the first
    // attempt is spent immediately by the reload the failing apply itself triggers — a tight budget
    // would be gone before the momentary lock this mostly exists for had cleared. The cap is only here
    // so a permission problem stops warning in the log eventually, not to ration attempts.
    private const int MaxAutomaticRetries = 10;

    private Settings? _settings;

    /// <summary>
    /// One mutating operation at a time (#775). Every path here reads shared state, awaits the daemon,
    /// and then writes that state back — so two overlapping operations interleave around the await and
    /// the one that finishes last owns the disk, regardless of which the user asked for last. That let a
    /// still-running apply write its edit over a restore that had already completed successfully, leaving
    /// the daemon on the default and the file holding the discarded change.
    ///
    /// Serializing rather than superseding, because unlike a data reload (<c>LatestOnlyGate</c>) these
    /// operations are not interchangeable: a queued apply still has to reach the daemon, and dropping it
    /// would lose the user's edit. Order is the guarantee; nothing is skipped.
    ///
    /// Never disposed, deliberately. Nothing here touches <c>AvailableWaitHandle</c>, so there is no
    /// resource to release — and disposing a SemaphoreSlim out from under a waiter is its own hazard
    /// (#767).
    /// </summary>
    private readonly SemaphoreSlim _mutations = new(1, 1);

    /// <summary>
    /// Which daemon this coordinator's state belongs to. Bumped by <see cref="ResetForNewDaemon"/>.
    ///
    /// Serializing alone was not enough (#803). An operation can be <em>queued</em> when the daemon
    /// changes, or already awaiting its RPC — the reset runs between the two, since it must not wait
    /// behind work belonging to a daemon that has gone. Without a generation, a queued edit is then sent
    /// to the new daemon, and an in-flight one writes its result into the new daemon's file.
    /// </summary>
    private int _sessionGeneration;

    /// <summary>
    /// Changes every time anything a read of the daemon could observe changes.
    ///
    /// Reading from the daemon is not instantaneous, and a mutation can complete while a read is still
    /// outstanding. The response then describes a moment that has passed, and adopting it silently puts
    /// the older values back — which is not merely a stale display: the next edit is built on that
    /// baseline and sends the reverted value to the daemon.
    ///
    /// A caller observes this before starting a read and hands it back when adopting, so a response that
    /// was overtaken can be recognised and dropped rather than believed.
    ///
    /// Two separate events move it, and it needs both. The daemon accepting a change moves what a read
    /// returns. Publishing moves what this session would compare that read against — and publishing
    /// happens BEFORE the call that changes the daemon, so a read starting in between would otherwise
    /// carry an epoch that looks current while returning state from before the change.
    ///
    /// Deliberately not the same counter as <see cref="_revision"/>. This one answers "could a read I
    /// started still be trusted"; that one answers "which published settings is this". An override moves
    /// this and not that, and an apply moves this twice while publishing once.
    /// </summary>
    private int _observationEpoch;

    /// <summary>
    /// What a read of the daemon could have observed at this moment.
    ///
    /// Internal because it is only meaningful paired with a read this session made itself. It was public
    /// once, and the host had to observe it, read the daemon, and hand it back — three steps in the right
    /// order, with the failure silent if it got them wrong. <see cref="ReloadFromDaemonAsync"/> does all
    /// three, which is why this no longer needs to leave the library.
    /// </summary>
    internal int ObservationEpoch => Volatile.Read(ref _observationEpoch);

    /// <summary>
    /// Which revision of the settings this session publishes is current.
    ///
    /// Counts published revisions only, so two results carrying the same value describe the same
    /// settings. That is what makes it usable as a stamp: an operation that changes the daemon without
    /// changing what this session publishes — a per-app override — produces no revision of its own and
    /// must not borrow this one.
    /// </summary>
    private int _revision;

    /// <summary>The revision <see cref="CurrentSettings"/> is at right now.</summary>
    private int Revision => Volatile.Read(ref _revision);

    /// <summary>The single place the published settings change, so no assignment can forget the version.</summary>
    /// <returns>The revision the published state is now at, for stamping whatever produced it.</returns>
    private int Publish(Settings? settings)
    {
        _settings = settings;
        // A new baseline is also something a read in flight can no longer be trusted against.
        Interlocked.Increment(ref _observationEpoch);
        return Interlocked.Increment(ref _revision);
    }

    /// <summary>
    /// A result a caller may keep: a copy of its own, stamped, or null when the copy cannot be made.
    ///
    /// Null rather than the revision itself, always. What is handed back must not be the object this
    /// session holds, sent, or may still retry — a caller that adopted that and went on editing would be
    /// editing all three.
    /// </summary>
    private PreparedSettings? Detach(Settings revision, SettingsStamp stamp) =>
        Snapshot(revision) is { } copy ? new PreparedSettings(copy, stamp) : null;

    /// <summary>
    /// The stamp for a published revision.
    ///
    /// The session is offset by one so a stamp from a freshly-created session is never equal to
    /// <see cref="SettingsStamp.None"/>, which means no session at all.
    /// </summary>
    private SettingsStamp StampFor(int revision) =>
        new(Volatile.Read(ref _sessionGeneration) + 1, revision);

    /// <summary>
    /// The daemon has just accepted something, so what a read of it can observe has changed.
    ///
    /// Moves the observation epoch and not the revision, and the distinction is the whole point.
    /// Publishing happens BEFORE the call, so a read starting between the two sees the new epoch and the
    /// old daemon state — a combination that looks current and is not. Versioning the local baseline
    /// does not version what a remote read returns; only the acceptance does.
    ///
    /// It must not move the revision either. An apply publishes once and is accepted once; on one shared
    /// counter the result's stamp would be a revision behind the state it had just created, so a caller
    /// checking freshness would reject its own result for having finished.
    ///
    /// Called on every path that succeeds in changing the daemon, not only the persisting one. Live-only
    /// and per-app applies, ending an override, and restoring the saved default all change what a read
    /// can observe, whether or not they change the baseline this session publishes.
    /// </summary>
    private void NoteDaemonAccepted() => Interlocked.Increment(ref _observationEpoch);

    /// <summary>
    /// Runs <paramref name="operation"/> with no other mutating operation in flight, and only while it
    /// still belongs to the daemon it was asked for.
    ///
    /// The generation is captured <b>before</b> waiting for the semaphore, which is the point: time spent
    /// queued is exactly when the daemon can change underneath a caller.
    /// </summary>
    /// <param name="operation">The work to run, given the origin it was admitted on.</param>
    /// <param name="superseded">What to report when a different daemon answered while this was queued.</param>
    /// <param name="closed">
    /// What to report when the session has been closed, if that is not the same as being superseded.
    /// They are different facts: superseded means a different daemon answered, and a caller told that
    /// about a closed session would go looking for a daemon change that never happened.
    /// </param>
    private async Task<T> SerializedAsync<T>(Func<Origin, Task<T>> operation, Func<T> superseded,
        Func<T>? closed = null)
    {
        // Before the queue, not inside it: a closed session refuses at once rather than after whatever
        // is stuck finishes.
        if (!TryBegin()) return (closed ?? superseded)();

        try
        {
            var admitted = Here();
            await _mutations.WaitAsync().ConfigureAwait(true);
            try
            {
                // And again for a caller that was already queued when closing began.
                if (_closed) return (closed ?? superseded)();
                if (!StillCurrent(admitted)) return superseded();

                return await operation(admitted).ConfigureAwait(true);
            }
            finally { _mutations.Release(); }
        }
        finally { End(); }
    }

    /// <summary>
    /// Everything this session is currently doing, and whether it is still accepting more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closing used to wait on the mutation gate, which answers a narrower question than it looks like:
    /// reads do not take it, and a retry waits for destination discovery before reaching it. So a session
    /// with a reload outstanding reported that everything had settled, and that read's continuation could
    /// run afterwards.
    /// </para>
    /// <para>
    /// A count covers all of it, because every operation passes through here whether it mutates or not.
    /// </para>
    /// </remarks>
    private readonly object _liveGate = new();

    private int _live;
    private bool _closed;
    private TaskCompletionSource? _quiet;

    /// <summary>Registers an operation, or refuses it because the session is closing.</summary>
    /// <remarks>
    /// Checked <b>before</b> anything queues or waits. It used to be checked under the mutation gate, so
    /// a caller arriving after a close that had given up queued behind whatever was stuck and waited for
    /// a daemon that was never going to answer. It was refused eventually, which is not the same thing.
    /// </remarks>
    private bool TryBegin()
    {
        lock (_liveGate)
        {
            if (_closed) return false;

            _live++;
            return true;
        }
    }

    /// <summary>
    /// Reports save progress to the host, unless this session was closed out from under the work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bounded close can give up on an operation that never answered. That operation is still running,
    /// and when its daemon finally replies it carries on into this session's state and, without this,
    /// into the host's callback -- arriving at something that finished tearing down seconds ago.
    /// </para>
    /// <para>
    /// Only after the close has <em>abandoned</em> it, not merely while one is waiting: work that settles
    /// inside the window settled normally and its host deserves to hear so.
    /// </para>
    /// </remarks>
    private void TellTheHost(SettingsSaveState state)
    {
        if (_abandoned) return;

        _onSaveState(state);
    }

    private volatile bool _abandoned;

    /// <summary>Marks an operation finished, and wakes a close that is waiting for the last one.</summary>
    private void End()
    {
        lock (_liveGate)
        {
            if (--_live == 0) _quiet?.TrySetResult();
        }
    }

    /// <summary>
    /// Stops admitting work, and waits for whatever was already admitted to finish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate is the whole mechanism: one operation holds it at a time, so acquiring it means nothing
    /// is running. Setting the flag first is what makes the wait finite — otherwise an operation admitted
    /// while waiting could take the gate next and the wait would chase it.
    /// </para>
    /// <para>
    /// Bounded, because the operation may be waiting on a daemon that has stopped answering, and an
    /// application exiting cannot be held open by one. False says it gave up: the work is still running,
    /// still holds what it holds, and the caller is closing anyway.
    /// </para>
    /// </remarks>
    /// <returns>True when everything admitted had finished; false when the wait ran out.</returns>
    internal async Task<bool> CloseAsync(TimeSpan settleWithin)
    {
        Task quiet;
        lock (_liveGate)
        {
            _closed = true;

            // Nothing running: settled, and every later caller gets the same answer for the same reason.
            if (_live == 0) return true;

            _quiet ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            quiet = _quiet.Task;
        }

        try
        {
            await quiet.WaitAsync(settleWithin).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            // Reported rather than swallowed, and reported the same way to a second caller: asking again
            // while the work is still running must not be told it has finished. The old version set one
            // flag and answered true to everyone after it, which conflated closing, closed-after-giving-up
            // and settled.
            // From here the work that outlived the window is on its own: it may still complete and settle
            // this session's state, and a host reading afterwards will see that. What it will not do is
            // call back into a host that has finished tearing down.
            _abandoned = true;

            _log.Warn("Closing the settings session with work still in flight: it did not finish in "
                      + "time. Nothing further will be admitted, what the daemon holds is unknown, and "
                      + "nothing further will be reported to the host.");
            return false;
        }
    }

    /// <summary>
    /// What an operation belongs to: a host-declared session, and the connection channel itself.
    /// </summary>
    /// <param name="Session">
    /// Moves when the host tells us the daemon changed — the deliberate reset, after an executable was
    /// compared and found different.
    /// </param>
    /// <param name="Channel">
    /// A hold on the connection itself, not a number describing it. Every send in the operation goes
    /// through this, so a channel replaced mid-operation cannot receive work authored for its
    /// predecessor.
    ///
    /// The distinction matters and I got it wrong first: comparing a number before sending is a check,
    /// and a check plus a send is two steps with a gap between them. An operation applies policy, takes
    /// snapshots and calls back into the host after being admitted — plenty of time for the channel to be
    /// replaced after the check passes. Holding it removes the gap rather than narrowing it.
    /// </param>
    private readonly record struct Origin(int Session, IDaemonSettingsBinding Channel);

    /// <summary>
    /// Why a send came back false: the channel is gone because it was replaced, or there is simply no
    /// transport.
    ///
    /// They look identical at the call -- both are "not sent" -- and they are different facts. A send
    /// bound to a replaced channel fails because the work is obsolete, and reporting that as
    /// "not connected" would tell a user with a perfectly good connection that they have none.
    /// </summary>
    private SettingsApplyOutcome NotSent(Origin origin) =>
        StillCurrent(origin) ? SettingsApplyOutcome.Disconnected : SettingsApplyOutcome.Superseded;

    /// <summary>What an operation admitted at this instant belongs to.</summary>
    private Origin Here() => new(Volatile.Read(ref _sessionGeneration), _daemon.Bind());

    /// <summary>
    /// True while an operation from <paramref name="origin"/> may still publish what it did.
    ///
    /// Not what stops it sending to the wrong daemon — <see cref="Origin.Channel"/> does that, by being a
    /// hold rather than a comparison. This decides whether the result may become this session's state.
    /// </summary>
    private bool StillCurrent(Origin origin) =>
        origin.Channel.Incarnation == _daemon.Incarnation
        && (origin.Session == Volatile.Read(ref _sessionGeneration)
            || origin.Channel.Incarnation == Volatile.Read(ref _survivorChannel));

    /// <summary>
    /// The channel whose work the last reset let through, or 0.
    /// </summary>
    /// <remarks>
    /// A reset invalidates by bumping one generation, which is right for every operation admitted on the
    /// daemon that has gone and wrong for the one arriving. An apply can be admitted and sent on a new
    /// channel before anything has identified it, and that channel's own identification then invalidated
    /// it -- B's work superseded by B's arrival, with a successful edit recorded nowhere.
    ///
    /// Which channel accepted or is executing an operation is what decides this, not whether its answer
    /// happened to arrive before the identification callback did.
    /// </remarks>
    private int _survivorChannel;

    /// <param name="daemon">The daemon connection.</param>
    /// <param name="isOwnedDaemon">The #465 gate — only rewrite filters on a daemon OTA positively owns.
    /// Read late because ownership is determined on connect, after this is constructed. Deliberately
    /// phrased as "is ours" rather than "is not theirs": an unidentifiable daemon is neither, and the
    /// negative form let OTA rewrite it (#742).</param>
    /// <param name="onSaveState">Reports save progress; the save chip stays observable state on the session.</param>
    /// <param name="log">Where this type reports what it did and could not do — mostly partial failure,
    /// which is exactly what is invisible from outside.</param>
    /// <param name="policy">The host's own rules, applied to a private copy on the way out.</param>
    /// <param name="rediscoverDestination">
    /// How to ask the owning session where the daemon on a given channel keeps its settings, for a retry
    /// that finds this session still does not know. Null leaves an unknown destination unknown.
    /// </param>
    internal SettingsCoordinator(IDaemonSettingsChannel daemon,
        Func<bool> isOwnedDaemon, Action<SettingsSaveState> onSaveState,
        IOtdLog log, IOtdSettingsPolicy policy, Func<int, Task>? rediscoverDestination = null)
        : this(daemon, new SettingsFileStore(log), isOwnedDaemon, onSaveState, log, policy,
               rediscoverDestination)
    {
    }

    /// <summary>
    /// Takes the file store, for a host that must supply its own — a test needing a write to fail on
    /// demand, chiefly. Failing writes are central behaviour here: applied-but-not-saved, the bounded
    /// retry, the destination a pending write is bound to.
    ///
    /// The other constructor uses the library's own writer, which is internal and not obtainable from
    /// outside. Supplying an alternative is not the same as being handed ours.
    /// </summary>
    internal SettingsCoordinator(IDaemonSettingsChannel daemon, ISettingsFileStore store,
        Func<bool> isOwnedDaemon, Action<SettingsSaveState> onSaveState,
        IOtdLog log, IOtdSettingsPolicy policy, Func<int, Task>? rediscoverDestination = null)
    {
        _rediscoverDestination = rediscoverDestination;
        _daemon = daemon;
        _store = store;
        _log = log;
        _policy = policy;
        _isOwnedDaemon = isOwnedDaemon;
        _onSaveState = onSaveState;
    }

    /// <summary>
    /// The settings this session is editing — the user's own, never a transient per-app snapshot.
    ///
    /// A detached copy on every read. The alternative is an escape hatch: <c>_settings</c> is also the
    /// object sent to the daemon and, after a failed write, the pending retry, so a caller that read it,
    /// edited it and applied it would be editing all three — and an edit the daemon never accepted would
    /// get written by the retry. Callers already work that way, so this is the read that has to change
    /// rather than all of them.
    ///
    /// Each read copies, so hold the result rather than re-reading it in a loop.
    /// </summary>
    public Settings? CurrentSettings => _settings is { } s ? Snapshot(s) : null;

    /// <summary>
    /// The settings this session is editing, detached and stamped.
    ///
    /// The stamp says which published revision the copy came from, so a caller holding it can tell later
    /// whether the ground has moved. Reading the settings and the revision is not one atomic step: a
    /// change landing between them yields a stamp one revision newer than the copy, which errs towards
    /// "this is stale" — the safe direction, and the reason it is not worth locking for.
    ///
    /// These are the settings this session publishes, which while <see cref="HasEphemeralOverride"/> is
    /// set is deliberately not what the daemon is running.
    /// </summary>
    public PreparedSettings? GetCurrent() =>
        _settings is { } current ? Detach(current, StampFor(Revision)) : null;

    /// <summary>
    /// True while the daemon is running something other than <see cref="CurrentSettings"/> — a transient
    /// per-app snapshot. The session's reload consults this so a temporary override can't become the
    /// editor's baseline (#737).
    /// </summary>
    public bool HasEphemeralOverride { get; private set; }

    /// <summary>
    /// Re-reads the daemon's settings and adopts them as this session's baseline.
    ///
    /// The whole sequence, because the order in it is what makes it safe and none of it is the host's
    /// business. The epoch is observed BEFORE the read, since an apply can complete while the read is in
    /// flight and its answer would then describe a moment that has passed; adopting that does not merely
    /// show stale values, it makes the next edit build on them and send the reverted value back.
    ///
    /// Skips the read entirely while an override is live (#737). The daemon is holding a transient
    /// snapshot then, and reading it back would make that snapshot the editor's baseline — the thing it
    /// is asked to persist as the user's default and to restore to.
    ///
    /// Deliberately NOT serialized against mutating operations, so a refresh is never blocked behind a
    /// long apply. That is an implementation choice which permits overlap, not a claim that overlap is
    /// desirable: a poll that did read the state an apply had just produced would be perfectly correct.
    /// What makes the overlap safe is the epoch check below plus the host's confinement contract — the
    /// epoch invalidates a stale answer, and single-context execution is what keeps the compare and the
    /// adopt from being interleaved. The epoch is not a substitute for that contract, and if the
    /// execution model ever changes this sequence needs real synchronization.
    /// </summary>
    /// <returns>What happened. Every outcome is ordinary; none needs the host to act.</returns>
    public async Task<SettingsReloadOutcome> ReloadFromDaemonAsync()
    {
        // A read is work: it holds a channel, it adopts what it finds, and its continuation touches this
        // session's state. So closing waits for it, and a closed session refuses a fresh one -- reported
        // as disconnected, which is what a closed session is, and publishing nothing: this read never
        // happened, so it has nothing to say about the baseline.
        if (!TryBegin()) return new SettingsReloadOutcome(SettingsReloadStatus.Disconnected);

        try
        {
            return await ReloadCoreAsync().ConfigureAwait(true);
        }
        finally { End(); }
    }

    private async Task<SettingsReloadOutcome> ReloadCoreAsync()
    {
        if (HasEphemeralOverride) return SettingsReloadOutcome.SkippedOverride;

        // Read through a hold on the channel, for the same reason every send goes through one: a read
        // that spans a reconnect would otherwise be answered by whichever channel is current when it
        // lands, and adopting that makes the new daemon's settings the baseline without anything having
        // identified it. The epoch does not catch this on its own -- a silent reconnect moves no epoch.
        var channel = _daemon.Bind();
        var observed = ObservationEpoch;
        var loaded = await channel.GetSettingsAsync();
        if (channel.Incarnation != _daemon.Incarnation) return SettingsReloadOutcome.Overtaken;
        if (!AdoptLoadedSettings(loaded, observed)) return SettingsReloadOutcome.Overtaken;

        // Null is adopted, not rejected: a daemon that answered with nothing is a daemon this session has
        // no settings for, and leaving the previous daemon's settings in place would be worse than an
        // empty editor. Reported separately so "the baseline is empty" is never mistaken for a read that
        // returned the user's settings.
        //
        // GetCurrent can itself come back null when the copy cannot be made. The baseline is adopted
        // either way -- the status says so -- but the payload is then absent, which is why it is
        // documented as a copy of the new baseline when one could be made rather than as a guarantee.
        return loaded == null
            ? SettingsReloadOutcome.Disconnected
            : new SettingsReloadOutcome(SettingsReloadStatus.Adopted, GetCurrent());
    }

    /// <summary>
    /// Adopts what a read returned, if that read has not been overtaken.
    ///
    /// Private: pairing a read with the epoch observed before it is the entire protection, and a caller
    /// that could do one without the other would have the defect this exists to prevent.
    /// </summary>
    /// <param name="settings">What the daemon returned.</param>
    /// <param name="observedVersion">
    /// <see cref="ObservationEpoch"/> as it was before the read started. If it has moved since, something
    /// was applied while the read was in flight and the response is older than what this session already
    /// holds.
    /// </param>
    /// <returns>False when the read was overtaken and nothing was adopted.</returns>
    private bool AdoptLoadedSettings(Settings? settings, int observedVersion)
    {
        if (observedVersion != ObservationEpoch)
        {
            _log.Info("Discarded a settings read that was overtaken by a change made while it was in " +
                      "flight; the newer settings stand.");
            return false;
        }
        Publish(settings);
        return true;
    }

    /// <summary>
    /// Records the no-op guard's baseline after a load. While an override is live the daemon does NOT
    /// hold <see cref="CurrentSettings"/>, so there is no honest value — null disables the guard rather
    /// than letting it skip an apply on the strength of a comparison against settings the daemon isn't
    /// running (#737).
    /// </summary>
    public void RecordLoadedBaseline()
    {
        _lastLoadedSettingsJson = HasEphemeralOverride ? null : SerializeForCompare(_settings);
    }

    /// <summary>
    /// Forgets everything that belonged to the daemon we were talking to, because a different one is
    /// answering now (#787).
    ///
    /// Almost all of this class's state is a fact about one daemon: the change it accepted but did not
    /// persist, the file that change was for, what its settings file last held, and whether it is running
    /// a transient per-app snapshot. None of that describes the new daemon, and each one misleads a
    /// different part of the app if carried across:
    ///
    /// <list type="bullet">
    /// <item>a pending write would target the wrong file (the retry also refuses this on its own, so the
    /// two guards are independent);</item>
    /// <item>a live override would suppress the load path's <see cref="AdoptLoadedSettings"/>, so the new
    /// daemon's settings would never be read and the editor would keep offering to persist the old
    /// daemon's (#737 gates on exactly that);</item>
    /// <item>a stale persisted baseline would let the no-op guard skip an apply the new daemon has never
    /// seen.</item>
    /// </list>
    ///
    /// <see cref="CurrentSettings"/> is deliberately left alone: the caller reloads immediately after
    /// this, and clearing it would blank the editor for that moment. Clearing the override is what makes
    /// that reload adopt the new daemon's settings.
    /// </summary>
    /// <remarks>
    /// <b>Calls nothing out of the library.</b> Telling the host its save chip is stale is a separate
    /// step, <see cref="AnnounceDiscardedChange"/>, because a host callback can reenter: it can bring up
    /// another daemon, whose own transition then commits while this one is still half-applied. Every
    /// state change here has to be finished before anything outside can observe it.
    /// </remarks>
    /// <returns>True when an unsaved change was thrown away, so the caller can say so. Everything else
    /// this drops is bookkeeping the user never knew about; a pending write is an edit they made.</returns>
    /// <param name="channelWhoseWorkSurvives">
    /// The channel whose accepted-but-unsaved work belongs to the daemon <em>arriving</em> rather than the
    /// one leaving, or 0 when nothing may be kept.
    /// </param>
    internal bool ResetForNewDaemon(int channelWhoseWorkSurvives)
    {
        // A pending write accepted by the arriving daemon is not the departing daemon's to lose, which
        // became possible the moment an apply could be admitted before identification had run.
        // Discarding it threw away an edit that was live on the daemon the user is now talking to.
        //
        // Deciding that on the channel alone was not enough. A channel is one daemon process, so identity
        // changing while the channel does not is something the transport cannot produce -- but the
        // identification seam can, and there the same-channel test would have kept a write belonging to
        // whichever daemon was misidentified. Hence the caller deciding, since only it knows which
        // channels it has already identified.
        // Work on the arriving channel keeps its authority across the reset that welcomed it, whether it
        // has finished (a pending write) or is still running (a send outstanding on that channel).
        Volatile.Write(ref _survivorChannel, channelWhoseWorkSurvives);

        var keepPending = channelWhoseWorkSurvives != 0
                          && _pendingDestination is { } made
                          && made.Channel == channelWhoseWorkSurvives;
        var hadUnsaved = HasUnsavedChange && !keepPending;

        // Everything queued or in flight belongs to the daemon that has gone. Bumping first means a
        // caller already past the semaphore check still fails StillCurrent before it writes.
        Interlocked.Increment(ref _sessionGeneration);
        // An outstanding read belongs to the daemon that has gone; bumping this makes its response
        // unadoptable rather than merely wrong.
        Interlocked.Increment(ref _observationEpoch);

        if (!keepPending) DiscardPendingPersist();
        _lastPersistedSettingsJson = null;
        _lastLoadedSettingsJson = null;
        HasEphemeralOverride = false;

        return hadUnsaved;
    }

    /// <summary>
    /// Tells the host its save chip no longer describes anything.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="ResetForNewDaemon"/>, and the split is the point. The chip was describing
    /// the old daemon's unsaved change; it is not the new one's problem, and leaving it would claim a
    /// change is live on a daemon that never received it. But this is a call into host code, so it goes
    /// after the state it describes is settled rather than in the middle of settling it.
    /// </remarks>
    internal void AnnounceDiscardedChange() => TellTheHost(SettingsSaveState.None);

    /// <summary>
    /// Copies the caller's settings and stamps the copy, at the moment the request is admitted.
    ///
    /// Before queueing, deliberately. Time spent waiting for the gate is time the caller can go on
    /// editing the object it handed over, and an operation that read it later would send whatever it had
    /// become rather than what was asked for. Isolating here is also what lets the caller keep editing
    /// immediately: after this returns, its object and this operation have nothing to do with each other.
    ///
    /// Returns null when the copy could not be made. That is a refusal, not a fallback: continuing with
    /// the caller's own object is precisely the guarantee being withdrawn, and a clone failing means a
    /// serialization failure is coming for the disk write anyway.
    /// </summary>
    private Settings? Admit(Settings settings, out Exception? error) => Snapshot(settings, out error);

    /// <summary>Applies to the daemon and persists to disk. Reports what actually happened rather than
    /// collapsing apply and persist into one result (#734). Does NOT reload — the caller does.</summary>
    public Task<SettingsApplyOutcome> ApplyAndSaveAsync(Settings settings)
    {
        if (Admit(settings, out var error) is not { } working)
        {
            _log.Warn("Couldn't take a private copy of the settings being applied; nothing was sent or " +
                      "saved. The settings are probably not serializable, which would fail the disk " +
                      "write next.", error);
            // The chip has to move off whatever it was saying. Left alone it would go on showing "Saved"
            // from the previous operation while this one silently did nothing.
            TellTheHost(SettingsSaveState.ApplyFailed);
            return Task.FromResult(SettingsApplyOutcome.Failed(error));
        }
        return SerializedAsync(origin => ApplyAndSaveCoreAsync(working, origin),
            () => SettingsApplyOutcome.Superseded,
            closed: () => SettingsApplyOutcome.Disconnected);
    }

    private async Task<SettingsApplyOutcome> ApplyAndSaveCoreAsync(Settings settings, Origin origin)
    {
        // (b) Circuit-breaker: if applies are firing faster than any legitimate use, a binding loop is
        // running — skip to break it (no reload → the loop can't re-trigger) instead of hanging the app.
        if (!_applyLoopBreaker.Allow(Environment.TickCount64))
        {
            System.Diagnostics.Debug.WriteLine(
                "SettingsCoordinator: apply-loop breaker tripped — skipping ApplyAndSave to avoid a hang (a UI binding is looping).");
            return SettingsApplyOutcome.Skipped;
        }

        // `settings` is this operation's private working copy, so policy edits it freely — but the
        // result is copied again below before anything is sent. The host's policy may keep a reference
        // to what it was handed; what it keeps must not be what goes to the daemon or the disk.
        _policy.Apply(settings, PolicyContext(persisting: true));

        var revision = Snapshot(settings);
        if (revision == null)
        {
            TellTheHost(SettingsSaveState.ApplyFailed);
            _log.Warn("Couldn't isolate the settings after applying policy; nothing was sent or saved.");
            return SettingsApplyOutcome.Failed(null);
        }

        // The format guard runs on the revision that is actually going out, after policy rather than
        // before it: policy edits profiles, so guarding first would leave anything policy touched
        // unchecked.
        GuardFormat(revision);

        // No-op guard: settings byte-identical to what the daemon last returned are a pure write of
        // unchanged data — skip them. Avoids redundant daemon writes and reloads, and neutralizes the
        // common "same value written back repeatedly" binding loop without a save flicker.
        //
        // It must ALSO match what was last persisted (#734). The baseline is the last daemon-LOADED
        // settings, so after a save failure the reload records the unsaved change as the baseline;
        // requiring both means an unsaved change always gets another chance at disk.
        //
        // Compared AFTER policy and the format guard, not before. The question is whether this operation
        // would change anything, and the only honest subject of that question is the revision that would
        // actually be sent. Comparing the caller's request instead asks it of something that never goes
        // anywhere: once the library stopped editing the caller's object, a request identical to the last
        // one still differed from the stored state by exactly the repairs policy had made to it, so the
        // guard stopped firing and every reapply became a real write.
        if (SerializeForCompare(revision) is { } json
            && json == _lastLoadedSettingsJson
            && json == _lastPersistedSettingsJson)
            return SettingsApplyOutcome.NoChange;

        var stamp = StampFor(Publish(revision));
        // A real apply puts the daemon on these settings, so any per-app override is over (#737).
        HasEphemeralOverride = false;
        // A copy of its own, not the revision. `revision` is simultaneously this session's state, the
        // object sent to the daemon, and -- if the write fails -- the pending retry. Handing that same
        // instance back as a result means a caller which adopts and then edits it is editing all three,
        // so an edit the daemon never accepted would be written by the retry. That is the ownership
        // problem this whole change exists to remove, reintroduced at the last step.
        //
        // Null rather than an alias when the copy fails: a result that cannot be isolated is not a
        // result a caller may adopt, and saying nothing is better than saying something untrue.
        var prepared = Detach(revision, stamp);

        // Resolved BEFORE the RPC, not after (#803), and resolved against the channel this work is bound
        // to rather than against whatever the host last noticed. Reading it late meant an apply that
        // outlived a switch wrote into the new daemon's file; reading it from the host meant an apply
        // admitted before anyone had identified the new daemon wrote into the OLD daemon's file, which is
        // the half #828 was still owed.
        var path = DestinationFor(origin);

        TellTheHost(SettingsSaveState.Saving);
        bool applied;
        try
        {
            // False means there is no transport: the change was NOT sent, so it isn't live and we must
            // not say it is (#734). Previously this returned quietly and we reported success.
            applied = await origin.Channel.SetSettingsAsync(revision);
        }
        catch (Exception ex)
        {
            // Reachable daemon, failed call. Not live, not saved — a different state from "live but
            // unpersisted", and the UI text must not claim otherwise.
            TellTheHost(SettingsSaveState.ApplyFailed);
            _log.Warn("Couldn't apply settings to the daemon.", ex);
            // Still throws: callers depend on it, and changing that is not this change's business.
            throw;
        }

        if (applied) NoteDaemonAccepted();

        if (!applied)
        {
            // Superseded rather than disconnected when the channel this was bound to has been replaced:
            // the send failed because the work is obsolete, not because the user has no daemon.
            if (!StillCurrent(origin))
            {
                _log.Warn("A settings apply was bound to a connection that has since been replaced; "
                            + "it was not sent to its replacement.");
                return SettingsApplyOutcome.Superseded;
            }

            TellTheHost(SettingsSaveState.Disconnected);
            _log.Warn("Couldn't apply settings: not connected to the daemon.");
            return SettingsApplyOutcome.Disconnected with { Prepared = prepared };
        }

        // The daemon changed while this was in flight, so what just succeeded landed on a daemon that is
        // no longer ours to speak for (#803). Write nothing and touch no state: the reset already cleared
        // this session, and "applied" describes a machine the user has moved on from.
        if (!StillCurrent(origin))
        {
            _log.Warn("A settings apply completed after the daemon changed; discarding its result "
                        + "rather than writing it to the new daemon's file.");
            return SettingsApplyOutcome.Superseded;
        }

        // Persist to disk (same as OTD's own UX Save). Apply and persist are separate outcomes: a failed
        // write means the change is live but won't survive a daemon restart, which we must not hide.
        // An empty settings path is NOT a successful save — there is nowhere to write (#734).
        bool saved = !string.IsNullOrEmpty(path) && _store.TrySave(revision, path);

        // Tracked separately from the daemon-loaded baseline so a persistence-only retry is possible.
        _lastPersistedSettingsJson = saved ? SerializeForCompare(revision) : null;
        // The same revision the daemon accepted, so a retry writes that and not whatever the caller's
        // object has become since (#765, #774).
        _pendingPersistSettings = saved ? null : revision;
        _pendingDestination = saved
            ? null
            : new PendingDestination(origin.Channel.Incarnation,
                                     string.IsNullOrEmpty(path) ? null : path);
        // A new change gets its own budget, and may be retried immediately — a spent budget must not
        // silently disable recovery for the rest of the session (#743).
        _automaticRetries = 0;

        if (!saved)
            _log.Warn(string.IsNullOrEmpty(path)
                ? $"Settings applied but not saved: {WhyNowhereToWrite(origin)}."
                : $"Settings applied but not saved: couldn't write {path}.");

        TellTheHost(saved ? SettingsSaveState.Saved : SettingsSaveState.Failed);
        var result = saved ? SettingsApplyOutcome.Saved : SettingsApplyOutcome.Unsaved;
        return result with { Prepared = prepared };
    }

    /// <summary>
    /// A change the daemon accepted is still missing from disk — live now, gone on the next daemon
    /// restart. <see cref="RetryPendingPersistAsync"/> is what clears it.
    /// </summary>
    public bool HasUnsavedChange => _pendingPersistSettings != null;

    /// <summary>
    /// Retry a pending disk write, if there is one and the retry budget isn't spent (#743). Called from
    /// the session's reload — which runs on window focus and every 30 seconds — so a write refused
    /// because the file was momentarily locked fixes itself with no user action and the chip goes back
    /// to "Saved". Returns <see cref="SettingsApplyStatus.NoChange"/> when there's nothing to do, so it
    /// is free to call on every load.
    /// </summary>
    public Task<SettingsApplyOutcome> RetryPendingPersistAsync() => SerializedAsync(origin =>
    {
        if (!HasUnsavedChange || _automaticRetries >= MaxAutomaticRetries)
            return Task.FromResult(SettingsApplyOutcome.NoChange);

        // Not having been told where to write yet is not a failed write, and must not spend the budget
        // that exists for failed writes. Otherwise a connection slow to answer could exhaust the
        // allowance before a single attempt had been made.
        if (string.IsNullOrEmpty(DestinationFor(origin)))
            return Task.FromResult(SettingsApplyOutcome.Unsaved);

        _automaticRetries++;
        return RetryPersistCoreAsync(origin);
    }, () => SettingsApplyOutcome.NoChange);

    /// <summary>
    /// Retries the disk write for settings the daemon already accepted but that failed to persist (#734).
    /// No daemon write and no reload — the change is already live; only the file is behind.
    /// </summary>
    public async Task<SettingsApplyOutcome> RetryPersistAsync()
    {
        // No ConfigureAwait(false), deliberately, and it was wrong here for a round. Everything after
        // this await enters the coordinator, reads its fields, writes a file and calls the host's
        // save-state callback -- and the mutation gate does not serialize any of that against the
        // identification and reset running on the host's context. Continuing on a pool thread is the
        // confinement failure this library exists to prevent, arrived at by a keystroke.
        //
        // Which means this resumes on the caller's synchronization context -- which a host calling the
        // asynchronous operations is required to have, and which IOtdExecutionContext says so. The
        // lookup also completes from inside work on the context, so an inline continuation usually lands
        // there anyway; that is a happy accident and not the contract, and writing it down as though it
        // were would be documenting a guarantee nothing enforces.
        // Registered around the discovery too, which happens before the gate: a close that waited only
        // for gated work could finish while this was still asking the daemon where to write.
        if (!TryBegin()) return SettingsApplyOutcome.Disconnected;

        try
        {
            await LookForTheDestinationIfNeededAsync();
        }
        finally { End(); }

        return await SerializedAsync(RetryPersistCoreAsync, () => SettingsApplyOutcome.NoChange,
                                     closed: () => SettingsApplyOutcome.Disconnected);
    }

    /// <summary>
    /// Asks the session where to persist, when that is the thing standing in the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Outside the mutation gate, deliberately.</b> This is a network call, and #828 is explicit that
    /// a short state transition must not wait behind one. Doing it inside the gate -- where it started --
    /// meant a retry could stall every other settings operation for as long as the daemon took to answer
    /// a question about metadata. Nothing here mutates, so there is nothing for the gate to protect.
    /// </para>
    /// <para>
    /// Reads the channel directly rather than an operation's origin, since it runs before one exists. A
    /// reconnect between this and the retry it precedes is handled where it always was: the retry's own
    /// origin decides what its destination is, and an answer about a channel that has gone is ignored.
    /// </para>
    /// </remarks>
    private Task LookForTheDestinationIfNeededAsync()
    {
        if (_rediscoverDestination is not { } lookAgain) return Task.CompletedTask;

        var channel = _daemon.Incarnation;
        var knowledge = _destination is { } known && known.Channel == channel
            ? known.Knowledge
            : DestinationKnowledge.Pending;

        // A daemon that has answered, or answered that it has no file, is not asked again.
        return knowledge is DestinationKnowledge.Pending or DestinationKnowledge.Unavailable
            ? lookAgain(channel)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Writes the pending revision, and deliberately does NOT run policy over it first.
    /// </summary>
    /// <remarks>
    /// The pending revision is what the daemon accepted. Policing it again on the way to disk would
    /// write something the daemon never saw — recreating the disagreement between disk and daemon that a
    /// retry exists to resolve. It matters whenever the host's policy is not a pure function of its
    /// input, which is a property this library cannot check and should not assume.
    ///
    /// Stated because it is invisible: the correct behaviour here is an absence, and an absence is what
    /// somebody tidies away. `RetryPolicyTests` pins it with a policy that renames on every run, so a
    /// second application changes the bytes; a policy with stable output would let the mistake through
    /// unnoticed, which is what every retry test before that one did.
    /// </remarks>
    private async Task<SettingsApplyOutcome> RetryPersistCoreAsync(Origin origin)
    {
        if (_pendingPersistSettings is not { } pending)
            return SettingsApplyOutcome.NoChange;


        // Empty covers both "the daemon reports no settings file" and "nobody has asked this connection
        // yet". Neither is a reason to write somewhere else, and both leave the change exactly where it
        // was: live, unsaved, and retryable.
        var path = DestinationFor(origin);
        if (string.IsNullOrEmpty(path))
        {
            _log.Warn($"Couldn't write the pending settings change: {WhyNowhereToWrite(origin)}.");
            return SettingsApplyOutcome.Unsaved;
        }

        // Two ways a pending change can fail to belong here, and they are not the same question.
        if (_pendingDestination is { } made)
        {
            // Its destination moved, which means a different daemon answered since it was made (#787).
            // Writing here would put one daemon's settings into another's file — a file OTA was never
            // asked to touch, belonging to an install the user may share with OTD's own UX. Drop the
            // change instead: losing an edit the disk already refused is bad, silently overwriting
            // someone else's configuration is worse.
            if (made.Path is { } knownThen && !PathEquality.Same(knownThen, path))
            {
                _log.Warn($"Discarding an unsaved settings change made for {knownThen}: the connected " +
                            $"daemon now uses {path}, and the change does not belong to it.");
                DiscardPendingPersist();
                TellTheHost(SettingsSaveState.None);
                return SettingsApplyOutcome.NoChange;
            }

            // Or nothing knew where it went when it was accepted. Then only the channel that accepted it
            // can say: completing it with whatever path is current would adopt an unrelated daemon's
            // destination, which is the cross-daemon write this whole arrangement exists to prevent.
            if (made.Path is null && made.Channel != origin.Channel.Incarnation)
            {
                _log.Warn("Discarding an unsaved settings change: it was accepted by a connection that " +
                            "never reported a settings file, and a different one is connected now.");
                DiscardPendingPersist();
                TellTheHost(SettingsSaveState.None);
                return SettingsApplyOutcome.NoChange;
            }
        }

        TellTheHost(SettingsSaveState.Saving);
        bool saved = _store.TrySave(pending, path);
        if (saved)
        {
            _lastPersistedSettingsJson = SerializeForCompare(pending);
            _pendingPersistSettings = null;
        }
        TellTheHost(saved ? SettingsSaveState.Saved : SettingsSaveState.Failed);
        return saved ? SettingsApplyOutcome.Saved : SettingsApplyOutcome.Unsaved;
    }

    /// <summary>Applies live without persisting — a temporary override (profile switching, #320). The
    /// saved <c>settings.json</c> default is untouched. Does NOT reload; the caller does.</summary>
    public Task<SettingsApplyOutcome> ApplyLiveOnlyAsync(Settings settings)
    {
        // Isolated at admission like every other mutating path. This one used to send the caller's own
        // instance, so an edit made while the RPC was in flight reached the daemon -- the hazard #774
        // fixed for apply-and-save and left open here.
        if (Admit(settings, out var error) is not { } working)
        {
            _log.Warn("Couldn't take a private copy of the live-only settings; nothing was sent.", error);
            return Task.FromResult(SettingsApplyOutcome.Failed(error));
        }
        return SerializedAsync(origin => ApplyLiveOnlyCoreAsync(working, origin),
            () => SettingsApplyOutcome.Superseded,
            closed: () => SettingsApplyOutcome.Disconnected);
    }

    private async Task<SettingsApplyOutcome> ApplyLiveOnlyCoreAsync(Settings settings, Origin origin)
    {
        _policy.Apply(settings, PolicyContext(persisting: false));

        var revision = Snapshot(settings);
        if (revision == null)
        {
            _log.Warn("Couldn't isolate the live-only settings after applying policy; nothing was sent.");
            return SettingsApplyOutcome.Failed(null);
        }

        GuardFormat(revision);

        // Report whether it landed (#766). False means no transport — the change was never sent, so a
        // caller must not announce a switch that didn't happen. State moves only on success.
        if (!await origin.Channel.SetSettingsAsync(revision))
        {
            _log.Warn("Couldn't apply the live-only settings: not connected to the daemon.");
            return NotSent(origin);
        }
        NoteDaemonAccepted();

        // Whatever just succeeded, it succeeded against a daemon that is no longer ours (#803). Leave
        // this session's state alone: the reset has already reset it for the daemon that replaced it.
        if (!StillCurrent(origin))
        {
            _log.Warn("A live-only apply completed after the daemon changed; not adopting it as the baseline.");
            return SettingsApplyOutcome.Superseded;
        }

        var stamp = StampFor(Publish(revision));
        HasEphemeralOverride = false;   // the daemon is on _settings again (#737)
        return SettingsApplyOutcome.Live with { Prepared = Detach(revision, stamp) };
    }

    /// <summary>
    /// Applies to the daemon ONLY — no disk save, no reload, and (unlike <see cref="ApplyLiveOnlyAsync"/>)
    /// no change to <see cref="CurrentSettings"/>. For automatic per-app switching (#167): the editor keeps
    /// showing and persisting the user's default while the daemon runs a transient snapshot.
    /// </summary>
    public Task<SettingsApplyOutcome> ApplyEphemeralAsync(Settings settings)
    {
        if (Admit(settings, out var error) is not { } working)
        {
            _log.Warn("Couldn't take a private copy of the per-app snapshot; nothing was sent.", error);
            return Task.FromResult(SettingsApplyOutcome.Failed(error));
        }
        return SerializedAsync(origin => ApplyEphemeralCoreAsync(working, origin),
            () => SettingsApplyOutcome.Superseded,
            closed: () => SettingsApplyOutcome.Disconnected);
    }

    private async Task<SettingsApplyOutcome> ApplyEphemeralCoreAsync(Settings settings, Origin origin)
    {
        _policy.Apply(settings, PolicyContext(persisting: false));

        var revision = Snapshot(settings);
        if (revision == null)
        {
            _log.Warn("Couldn't isolate the per-app snapshot after applying policy; nothing was sent.");
            return SettingsApplyOutcome.Failed(null);
        }

        GuardFormat(revision);

        // An override that never reached the daemon is not an override (#766). Setting the flag anyway
        // would suppress the reload's settings read on the strength of one that does not exist.
        if (!await origin.Channel.SetSettingsAsync(revision))
        {
            _log.Warn("Couldn't apply the per-app snapshot: not connected to the daemon.");
            return NotSent(origin);
        }
        NoteDaemonAccepted();

        // Flag it so the reload stops overwriting the baseline with what the daemon now holds (#737).
        // Leaving _settings untouched here was never enough on its own: the 30-second poll read the
        // daemon back into it, so a transient snapshot silently became the editor's baseline and the
        // source a "restore default" would restore from.
        // Whatever just succeeded, it succeeded against a daemon that is no longer ours (#803). Leave
        // this session's state alone: the reset has already reset it for the daemon that replaced it.
        if (!StillCurrent(origin))
        {
            _log.Warn("A per-app snapshot completed after the daemon changed; not recording it as an override.");
            return SettingsApplyOutcome.Superseded;
        }

        HasEphemeralOverride = true;
        // No Prepared, deliberately. The point of a per-app snapshot is that what the user edits and what
        // gets saved stay theirs, so this publishes nothing — and with nothing published there is no
        // revision this snapshot can honestly claim to be. Stamping it with the current one would say it
        // IS the published settings, and a caller comparing stamps would adopt a transient override as
        // the thing to save. Withholding the result is what makes that impossible, rather than a comment
        // asking callers not to.
        return SettingsApplyOutcome.Live;
    }

    /// <summary>
    /// Puts the daemon back on <see cref="CurrentSettings"/>, ending any ephemeral override.
    ///
    /// Sends unconditionally — it does not check <see cref="HasEphemeralOverride"/> first. That flag
    /// records what this session was told, and a session that has just reconnected knows less about the
    /// daemon than the flag implies. So the only case that sends nothing is having nothing to send:
    /// no settings loaded, reported as <see cref="SettingsApplyStatus.NoChange"/>. Otherwise the baseline
    /// goes out and a success is <see cref="SettingsApplyStatus.AppliedLive"/>, whether or not an
    /// override was recorded.
    /// </summary>
    public Task<SettingsApplyOutcome> ClearEphemeralOverrideAsync() =>
        SerializedAsync(ClearEphemeralOverrideCoreAsync, () => SettingsApplyOutcome.Superseded,
                        closed: () => SettingsApplyOutcome.Disconnected);

    private async Task<SettingsApplyOutcome> ClearEphemeralOverrideCoreAsync(Origin origin)
    {
        if (_settings is not { } baseline)
        {
            // Nothing to return to, so nothing is overriding anything.
            HasEphemeralOverride = false;
            return SettingsApplyOutcome.NoChange;
        }

        // The override is only over once the daemon is back on the baseline (#766). Clearing the flag on
        // a failed write would leave the tablet on the snapshot while the reload resumed treating the
        // daemon as authoritative — adopting that snapshot as the editor's default, which is exactly
        // what #737 fixed.
        if (!await origin.Channel.SetSettingsAsync(baseline))
        {
            _log.Warn("Couldn't end the per-app override: not connected to the daemon. " +
                        "The override is still in effect.");
            return NotSent(origin);
        }
        NoteDaemonAccepted();

        // Whatever just succeeded, it succeeded against a daemon that is no longer ours (#803). Leave
        // this session's state alone: the reset has already reset it for the daemon that replaced it.
        if (!StillCurrent(origin))
        {
            _log.Warn("The per-app override ended after the daemon changed; the new daemon never had one.");
            return SettingsApplyOutcome.Superseded;
        }

        HasEphemeralOverride = false;
        // The baseline was already published; putting the daemon back on it creates no new revision, so
        // the stamp is the one it already had. Unlike the per-app apply above, the result IS the
        // published settings, so handing it back says something true.
        return SettingsApplyOutcome.Live with { Prepared = Detach(baseline, StampFor(Revision)) };
    }

    /// <summary>
    /// Re-reads the saved default from disk (untouched by a live-only override) and applies it. Every way
    /// this can fall short has its own outcome (#734): it used to finish silently when the default
    /// couldn't be loaded, and the caller cleared the override indicator and announced a restoration that
    /// never happened — while the override was still running. Does NOT reload; the caller does.
    /// </summary>
    public Task<SettingsRestoreOutcome> RestoreDefaultAsync() =>
        SerializedAsync(RestoreDefaultCoreAsync, () => SettingsRestoreOutcome.Superseded,
                        closed: () => SettingsRestoreOutcome.Disconnected);

    private async Task<SettingsRestoreOutcome> RestoreDefaultCoreAsync(Origin origin)
    {
        var path = DestinationFor(origin);
        if (string.IsNullOrEmpty(path) || !_store.TryLoad(path, out var def) || def == null)
        {
            _log.Warn("Couldn't restore the saved default: no readable settings file. " +
                        "Any active override is still in effect.");
            return SettingsRestoreOutcome.SourceUnavailable;
        }

        // Guarded like anything else we send. Restore reads the saved default off disk, so it can carry
        // content the daemon never had -- from a file written by an older build, or edited by hand.
        //
        // The repair lands on `def`, which is the store's own freshly-deserialized object and not shared
        // with the caller. It is deliberately NOT written back: repairing the file is a different act
        // from not sending it something that crashes, and this operation was asked to do the second.
        GuardFormat(def);

        bool applied;
        try
        {
            applied = await origin.Channel.SetSettingsAsync(def);
        }
        catch (Exception ex)
        {
            _log.Warn("Couldn't restore the saved default: the daemon rejected it. " +
                        "Any active override is still in effect.", ex);
            return SettingsRestoreOutcome.Failed(ex);
        }

        if (applied) NoteDaemonAccepted();

        if (!applied)
        {
            _log.Warn("Couldn't restore the saved default: not connected to the daemon. " +
                        "Any active override is still in effect.");
            return SettingsRestoreOutcome.Disconnected;
        }

        // The default we just applied came from the old daemon's file, and the reset has already cleared
        // this session's state for the new one (#803). Adopting it here would make one daemon's saved
        // default the other's baseline.
        if (!StillCurrent(origin))
        {
            _log.Warn("A restore completed after the daemon changed; discarding its result.");
            return SettingsRestoreOutcome.Superseded;
        }

        Publish(def);
        HasEphemeralOverride = false;   // restored to the saved default; no override remains (#737)

        // Drop any pending save (#764). A pending save is an edit the daemon took but the disk refused,
        // and restoring the default is the user discarding exactly that. Left in place, the next reload's
        // retry writes it back over the default that was just restored — disk and daemon then disagree,
        // and the next restart resolves it in favour of the edit the user got rid of.
        DiscardPendingPersist();
        _lastPersistedSettingsJson = SerializeForCompare(def);

        // Nothing is outstanding any more, so stop saying otherwise (#776). A failed save followed by a
        // successful restore used to leave the chip reading "Couldn't save — your change is live but
        // won't survive a restart" about a change the user had just deliberately discarded.
        //
        // None rather than Saved: restoring reads the default off disk and applies it, so no save
        // happened, and reporting one would be a smaller version of the same lie. The failed paths above
        // all return early and leave their own state standing.
        TellTheHost(SettingsSaveState.None);
        return SettingsRestoreOutcome.Restored;
    }

    /// <summary>
    /// Repairs applied to every settings object before it reaches the daemon or disk. These run on the
    /// way OUT rather than on load because the shared <c>settings.json</c> is also OTD's own UX's file —
    /// a malformed profile we write would crash their UI, not just ours.
    /// </summary>
    /// <summary>
    /// What the host policy is told about the operation it is running inside.
    ///
    /// No session identity yet: the coordinator carries no per-operation stamp until the operations move
    /// behind the facade, and an invented one would be worse than an absent one.
    /// </summary>
    /// <param name="persisting">
    /// Whether this operation asks for a disk write. Intent, not outcome — a requested write can fail.
    /// </param>
    private SettingsPolicyContext PolicyContext(bool persisting) =>
        new(SettingsStamp.None, _isOwnedDaemon(), persisting);

    /// <summary>
    /// Repairs, on the revision that is about to go out, the one profile shape known to crash a reader
    /// of the shared settings file.
    ///
    /// OpenTabletDriver's own interface does <c>p.AbsoluteModeSettings.Tablet.Width</c> when it saves and
    /// throws if any of that is null. Some profile creation outside the daemon process leaves it null, so
    /// filling it here keeps the file usable by every program that reads it, not just this one.
    ///
    /// This runs on the persisting path only. Live-only and ephemeral applies do not get it, and that
    /// asymmetry is preserved rather than corrected here.
    ///
    /// It is NOT the principled line it appears to be. The guard protects a reader of the settings file,
    /// so "we do not write, therefore it cannot matter" looks like it follows — but OpenTabletDriver's
    /// interface pulls the daemon's live state into itself (<c>MainForm.SyncSettings</c>, wired to
    /// <c>Resynchronize</c>) and then dereferences that same field when the user saves. Settings applied
    /// live-only really can reach the crash by that route. Widening the guard is a behaviour change owed
    /// its own review, not something to slip in here.
    /// </summary>
    /// <summary>
    /// Repairs the one settings shape known to crash a reader of the shared file, on the revision about
    /// to leave this library.
    /// </summary>
    /// <remarks>
    /// Runs on <b>every</b> path that sends, not only the one that writes (#836). It used to guard the
    /// persisting path alone, reasoning that a null area only matters to whoever reads the file. That is
    /// not the only route: OpenTabletDriver's UX pulls settings from the daemon on every resync
    /// (<c>MainForm.SyncSettings</c>) and then null-dereferences the area in its own Save. A live-only
    /// apply never touches our file and can still crash it.
    ///
    /// Idempotent and cheap, so the rule is one sentence rather than a table of which operations repair:
    /// nothing leaves here carrying a null Absolute-mode area.
    ///
    /// Ending an override is the one send that does not call this, and does not need to: it returns the
    /// daemon to settings the daemon itself gave us, so it can introduce nothing the daemon did not
    /// already have.
    /// </remarks>
    private void GuardFormat(Settings settings)
    {
        int repaired = ProfileSanitizer.EnsureValidAbsoluteAreas(settings);
        if (repaired > 0)
            _log.Warn($"Repaired {repaired} profile(s) with missing Absolute-mode areas before sending " +
                      "them to the daemon (would otherwise crash the OpenTabletDriver UX).");
    }

    /// <summary>Forget the pending save and its retry budget — nothing is outstanding.</summary>
    private void DiscardPendingPersist()
    {
        _pendingPersistSettings = null;
        _pendingDestination = null;
        _automaticRetries = 0;
    }

    /// <summary>
    /// An independent copy, so later mutation of the caller's object cannot reach what we hold. Round-trips
    /// through the same serializer the equality guard uses, so a settings object that can be compared can
    /// also be snapshotted. Returns null if it can't be cloned, which simply means there is nothing to
    /// retry — better than retrying something that has since changed underneath us.
    /// </summary>
    // Not static: it reports its own failure, and the log is injected (#807). Keeping it static would
    // mean either a static logger or a silent null, and a snapshot that fails silently is how a
    // concurrent edit reaches the daemon unnoticed.
    private Settings? Snapshot(Settings settings) => Snapshot(settings, out _);

    /// <summary>
    /// A detached copy, or null with <paramref name="error"/> set to why not.
    ///
    /// The cause is handed back rather than only logged: a caller that refuses an operation because it
    /// could not isolate the request should be able to say what went wrong, and "failed for no stated
    /// reason" is the least useful thing an error can be.
    /// </summary>
    private Settings? Snapshot(Settings settings, out Exception? error)
    {
        try
        {
            error = null;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(json);
        }
        catch (Exception ex)
        {
            error = ex;
            _log.Warn("Couldn't take a private copy of the settings.", ex);
            return null;
        }
    }

    /// <summary>Deterministic string form of the settings for cheap equality comparison (the no-op apply
    /// guard). Best-effort — returns null on any serialization failure, which just disables the guard for
    /// that call (the circuit-breaker still backs it up).</summary>
    private static string? SerializeForCompare(Settings? settings)
    {
        if (settings == null) return null;
        try { return Newtonsoft.Json.JsonConvert.SerializeObject(settings); }
        catch { return null; }
    }
}
