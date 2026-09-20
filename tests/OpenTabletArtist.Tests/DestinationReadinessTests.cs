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
/// What happens to an edit made before this session knows where the connected daemon keeps its settings
/// (#828 readiness).
/// </summary>
///
/// <remarks>
/// <para>
/// Driven through <see cref="OtdSession"/> rather than against a coordinator built by hand, because the
/// interesting failures are in the orchestration: identification, the reset it performs, and the
/// destination lookup all run together, and a coordinator on its own has none of them.
/// </para>
/// <para>
/// The promise being tested is the one the readiness change makes: an edit admitted before the
/// destination is known is <em>live, unsaved and retryable</em> — not lost, and not written to the
/// previous daemon's file. Two probes from Codex's review of #848 showed it was only the last of those.
/// </para>
/// </remarks>
public class DestinationReadinessTests
{
    /// <summary>
    /// An edit made before the destination is known saves once it arrives, to that daemon's own file.
    /// </summary>
    /// <remarks>
    /// The pending write recorded an empty path, meaning "nowhere known yet". The retry then compared
    /// that empty string against the now-known path, found them different, and took the difference for a
    /// daemon switch — so it discarded the edit under a rule (#787) written for a destination that had
    /// genuinely moved. "Not known yet" and "known, and changed" are different facts and were the same
    /// value.
    /// </remarks>
    [Fact]
    public async Task AnEditMadeBeforeTheDestinationIsKnown_SavesOnceItArrives()
    {
        var h = Make();

        h.Daemon.Reconnect();                       // a channel; identification and the lookup are queued
        await h.Settings.ReloadFromDaemonAsync();

        var applied = await h.Settings.ApplyAndSaveAsync(Tablet("Early edit"));

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, applied.Status);
        Assert.Empty(h.Store.Wrote);

        h.Context.Drain();                          // identified, and the daemon says where it lives

        var retry = await h.Settings.RetryPersistAsync();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, retry.Status);
        Assert.Equal(FakeDaemonTransport.DefaultSettingsFile, Assert.Single(h.Store.Wrote).Path);
    }

    /// <summary>
    /// An edit the <b>new</b> daemon already accepted is not discarded by the identification that follows.
    /// </summary>
    /// <remarks>
    /// The reset a daemon change performs assumes everything this session holds belonged to the daemon
    /// that has gone. Once an apply can be admitted before identification, that assumption is wrong for
    /// exactly one thing: a pending write the new daemon has itself accepted. Discarding it threw away an
    /// edit that was live on the daemon the user is now talking to — which is the opposite of what the
    /// readiness change promises.
    /// </remarks>
    [Fact]
    public async Task AnEditAcceptedByTheNewDaemonBeforeIdentification_SurvivesIt()
    {
        var h = Make();

        h.Daemon.Reconnect();                       // A
        h.Context.Drain();
        await h.Settings.ReloadFromDaemonAsync();

        h.Locator.Path = "B/OpenTabletDriver.Daemon.exe";
        h.Daemon.Reconnect();                       // B answers; nothing has identified it yet

        var applied = await h.Settings.ApplyAndSaveAsync(Tablet("B's edit"));
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, applied.Status);

        h.Context.Drain();                          // B is identified, and the reset runs

        // The edit belongs to B, and B is who we are talking to. It must still be there.
        var retry = await h.Settings.RetryPersistAsync();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, retry.Status);
        Assert.Equal("B's edit", Tablet(Assert.Single(h.Store.Wrote).Settings));
    }

    /// <summary>
    /// And an edit the <b>old</b> daemon accepted is still discarded, which is the rule that matters more.
    /// </summary>
    /// <remarks>
    /// The other side of the same change. Preserving a pending write across an identification must not
    /// become preserving <em>every</em> pending write: an edit A accepted has no business being written
    /// into B's file, and that is what #787 closed.
    /// </remarks>
    [Fact]
    public async Task AnEditAcceptedByTheOldDaemon_IsStillDiscardedByIdentification()
    {
        var h = Make();

        h.Daemon.Reconnect();                       // A
        h.Context.Drain();
        await h.Settings.ReloadFromDaemonAsync();

        h.Store.SaveSucceeds = false;
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await h.Settings.ApplyAndSaveAsync(Tablet("A's edit"))).Status);
        h.Store.SaveSucceeds = true;

        h.Locator.Path = "B/OpenTabletDriver.Daemon.exe";
        h.Daemon.Reconnect();
        h.Context.Drain();                          // B is identified; A's edit is not B's business

        Assert.Equal(SettingsApplyStatus.NoChange, (await h.Settings.RetryPersistAsync()).Status);
        Assert.Empty(h.Store.Wrote);
    }

    /// <summary>
    /// A lookup started on B whose reply arrives after C has connected does not authorise C.
    /// </summary>
    /// <remarks>
    /// The metadata call is asynchronous, so its answer can outlive the connection it was asked about.
    /// Adopting it would point this session at B's settings file while C is connected, which is the
    /// cross-daemon write in its subtlest form: nothing about the write looks wrong, and the destination
    /// is a real path belonging to a real daemon.
    /// </remarks>
    [Fact]
    public async Task AMetadataReplyThatOutlivesItsChannel_DoesNotAuthoriseTheNextOne()
    {
        var h = Make();

        // Deliberately NOT RunContinuationsAsynchronously. The lookup's continuation posts to the test's
        // own context, and that context is a plain list; completing this on the thread pool would append
        // to it from another thread while the test drains, so the test would pass or fail on timing. It
        // did pass, against a mutation that adopted the stale reply -- for that reason and no other.
        var bAnswers = new TaskCompletionSource<AppInfo?>();
        h.Daemon.GetAppInfoHandler = () => bAnswers.Task;

        h.Daemon.Reconnect();                       // B, whose lookup is held
        h.Context.Drain();

        h.Daemon.GetAppInfoHandler = null;          // C answers normally
        h.Daemon.Reconnect();                       // C
        h.Context.Drain();

        bAnswers.SetResult(FakeDaemonTransport.Reporting("B/settings.json"));
        h.Context.Drain();

        await h.Settings.ReloadFromDaemonAsync();
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await h.Settings.ApplyAndSaveAsync(Tablet("C's edit"))).Status);

        // C's file, not B's. B's answer was about a connection that had already gone.
        Assert.Equal(FakeDaemonTransport.DefaultSettingsFile, Assert.Single(h.Store.Wrote).Path);
    }

    /// <summary>
    /// A failed metadata lookup recovers on an explicit retry, without needing a reconnect.
    /// </summary>
    /// <remarks>
    /// Asking once per transition left a connection whose single lookup failed unable to persist for its
    /// whole life, with one warning at connect as the only trace. The save chip's Retry only ever retried
    /// the disk, so the user's one recovery action did not touch the thing that was actually broken.
    /// </remarks>
    [Fact]
    public async Task AFailedMetadataLookup_RecoversOnAnExplicitRetry()
    {
        var h = Make();

        var fail = true;
        h.Daemon.GetAppInfoHandler = () => fail
            ? Task.FromException<AppInfo?>(new InvalidOperationException("no answer"))
            : Task.FromResult<AppInfo?>(FakeDaemonTransport.Reporting("A/settings.json"));

        h.Daemon.Reconnect();
        h.Context.Drain();
        await h.Settings.ReloadFromDaemonAsync();

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await h.Settings.ApplyAndSaveAsync(Tablet("Edit"))).Status);
        Assert.Empty(h.Store.Wrote);

        fail = false;                               // the daemon can answer now

        // Two deterministic steps, not "retry until something works". The first attempt asks the daemon
        // again and still cannot write, because the answer comes back through the host's context; the
        // second writes. Asserting "one of these worked" would pass just as well with no rediscovery at
        // all, as long as something else eventually learned the path.
        var asked = h.Daemon.GetAppInfoCalls;

        // Started, then drained, then awaited. The lookup finishes only once its answer has been
        // recorded on the host's context, so a caller that blocks before the context runs would wait for
        // work it is itself preventing -- which a real dispatcher never does, because the host awaiting
        // returns to the pump.
        var retry = h.Settings.RetryPersistAsync();
        h.Context.Drain();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, (await retry).Status);
        Assert.Equal(asked + 1, h.Daemon.GetAppInfoCalls);       // it asked again, once
        Assert.Equal("A/settings.json", Assert.Single(h.Store.Wrote).Path);

        // And once it knows, it stops asking: a destination already answered is not re-fetched on every
        // later retry.
        Assert.Equal(asked + 1, h.Daemon.GetAppInfoCalls);
    }

    /// <summary>
    /// Two channels reporting the <b>same</b> path are still two channels, and a pending edit from the
    /// first does not survive onto the second.
    /// </summary>
    /// <remarks>
    /// Not hypothetical: the two OpenTabletDriver installs this repository is tested against both report
    /// <c>%LOCALAPPDATA%\OpenTabletDriver\settings.json</c>. The path guard that #787 added is blind
    /// here, so what protects the user is the identity change resetting the session — and this pins that
    /// the destination being identical does not quietly re-authorise the old edit.
    /// </remarks>
    [Fact]
    public async Task TwoDaemonsSharingASettingsFile_StillDoNotShareAPendingEdit()
    {
        var h = Make();

        h.Daemon.Reconnect();                       // A
        h.Context.Drain();
        await h.Settings.ReloadFromDaemonAsync();

        h.Store.SaveSucceeds = false;
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await h.Settings.ApplyAndSaveAsync(Tablet("A's edit"))).Status);
        h.Store.SaveSucceeds = true;

        // B: a different install, the same settings file.
        h.Locator.Path = "B/OpenTabletDriver.Daemon.exe";
        h.Daemon.Reconnect();
        h.Context.Drain();

        Assert.Equal(SettingsApplyStatus.NoChange, (await h.Settings.RetryPersistAsync()).Status);
        Assert.Empty(h.Store.Wrote);
    }

    /// <summary>
    /// A lookup that has already failed does not become the permanent answer.
    /// </summary>
    /// <remarks>
    /// The coalescing recorded the in-flight lookup <em>after</em> starting it, so a lookup that
    /// completed synchronously — which a failure does — had already cleared the slot before it was
    /// filled. Every later retry then returned that finished task and never asked the daemon again, so a
    /// connection whose first two lookups failed could never recover however many times the user pressed
    /// Retry.
    /// </remarks>
    [Fact]
    public async Task ALookupThatAlreadyFailed_IsNotCachedAsTheAnswer()
    {
        var h = Make();

        var fail = true;
        h.Daemon.GetAppInfoHandler = () => fail
            ? Task.FromException<AppInfo?>(new InvalidOperationException("no answer"))
            : Task.FromResult<AppInfo?>(FakeDaemonTransport.Reporting("A/settings.json"));

        h.Daemon.Reconnect();
        h.Context.Drain();
        await h.Settings.ReloadFromDaemonAsync();

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await h.Settings.ApplyAndSaveAsync(Tablet("Edit"))).Status);

        var afterFirst = h.Daemon.GetAppInfoCalls;

        var second = h.Settings.RetryPersistAsync();   // asks, and fails again
        h.Context.Drain();
        await second;
        Assert.Equal(afterFirst + 1, h.Daemon.GetAppInfoCalls);

        fail = false;

        var third = h.Settings.RetryPersistAsync();    // must ask again, not reuse the failed lookup
        h.Context.Drain();
        await third;

        Assert.Equal(afterFirst + 2, h.Daemon.GetAppInfoCalls);
    }

    /// <summary>
    /// An operation the arriving daemon is still executing keeps its authority across the identification
    /// that welcomes it.
    /// </summary>
    /// <remarks>
    /// The other ordering of the case fixed last round. There the edit was already accepted when
    /// identification ran; here the send is still outstanding on the very channel being identified. The
    /// reset bumps a single global generation, so the operation came back Superseded — B's own work
    /// invalidated by B's own arrival, and the user's successful edit not recorded as anything.
    /// </remarks>
    [Fact]
    public async Task AnApplyStillRunningOnTheArrivingChannel_IsNotSupersededByItsIdentification()
    {
        var h = Make();

        h.Daemon.Reconnect();                       // A
        h.Context.Drain();
        await h.Settings.ReloadFromDaemonAsync();

        h.Locator.Path = "B/OpenTabletDriver.Daemon.exe";
        h.Daemon.Reconnect();                       // B answers; its identification is queued

        var held = new TaskCompletionSource<bool>();
        h.Daemon.SetSettingsHandler = _ => held.Task;

        var apply = h.Settings.ApplyAndSaveAsync(Tablet("B's edit"));   // bound to B, still sending

        h.Context.Drain();                          // B is identified, and the reset runs
        held.SetResult(true);                       // and only now does B answer

        var outcome = await apply;

        // Not Superseded: the edit is B's, it reached B, and B is who we are talking to. Not saved
        // either, because the destination is resolved before the send (#803) and nothing had identified
        // B by then -- so it is live, unsaved and B's to recover, which is the whole promise.
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, outcome.Status);

        var retry = await h.Settings.RetryPersistAsync();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, retry.Status);
        Assert.Equal("B's edit", Tablet(Assert.Single(h.Store.Wrote).Settings));
    }

    /// <summary>
    /// Coordinator work after an <b>incomplete</b> metadata lookup still runs on the host's context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Moving discovery outside the mutation gate was right; leaving the host's context to do it was not,
    /// and I did both in one edit. Everything after that await enters the coordinator, reads its fields,
    /// writes a file and calls the host's save-state callback — and the gate does not serialize any of
    /// that against the identification and reset running on the context. A ConfigureAwait(false) put it
    /// all on a pool thread.
    /// </para>
    /// <para>
    /// The lookup is completed from another thread here, because that is the only arrangement that shows
    /// it: a synchronously-answering daemon conceals the boundary entirely, which is why every existing
    /// metadata test passed while this was broken.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AfterAnAsynchronousLookup_TheRetryStaysOnTheHostContext()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var store = new PathRecordingStore();
        using var context = new OneThreadContext();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, new FakeProcessLocator(), context);
        var settings = session.OpenSettings(() => true, state => context.Observe(state));

        // A lookup that will not answer until this test says so, from a thread that is not the context's.
        var answer = new TaskCompletionSource<AppInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetAppInfoHandler = () => answer.Task;

        daemon.Reconnect();

        // Everything the host does runs ON the host's context, which is what a host actually does.
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsApplyOutcome retried = default;

        var hostWork = context.RunAsync(async () =>
        {
            await settings.ReloadFromDaemonAsync();
            Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
                (await settings.ApplyAndSaveAsync(Tablet("Edit"))).Status);

            var retry = settings.RetryPersistAsync();
            retryStarted.SetResult();
            retried = await retry;
        });

        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        answer.SetResult(FakeDaemonTransport.Reporting("A/settings.json"));
        await hostWork.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, retried.Status);

        // The save-state callback is the host's own code, and it ran where the host said its code runs.
        Assert.NotEmpty(context.SaveStates);
        Assert.Empty(context.Offences);
    }

    /// <summary>
    /// A caller asking about a new connection does not wait behind an obsolete connection's lookup.
    /// </summary>
    /// <remarks>
    /// Coalescing through one unqualified task would hand C's caller B's outstanding call, so a daemon
    /// that had gone could keep a live connection from ever learning where to write. Held here rather
    /// than merely slow, because "did not wait" is only observable if waiting would have been indefinite.
    /// </remarks>
    [Fact]
    public async Task ALookupHeldOnAnOldChannel_DoesNotDelayTheNewOne()
    {
        var h = Make();

        var bNeverAnswers = new TaskCompletionSource<AppInfo?>();
        h.Daemon.GetAppInfoHandler = () => bNeverAnswers.Task;

        h.Daemon.Reconnect();                       // B, whose lookup never returns
        h.Context.Drain();

        h.Daemon.GetAppInfoHandler = null;          // C answers normally
        h.Daemon.Reconnect();                       // C
        h.Context.Drain();

        await h.Settings.ReloadFromDaemonAsync();
        var applied = await h.Settings.ApplyAndSaveAsync(Tablet("C's edit"));

        // C's own lookup answered, so this saved — while B's is still outstanding and always will be.
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, applied.Status);
        Assert.False(bNeverAnswers.Task.IsCompleted);
    }

    // --- harness --------------------------------------------------------------------------------

    private sealed record Harness(OtdSession Session, IOtdSettingsSession Settings,
        FakeDaemonTransport Daemon, FakeProcessLocator Locator, ControllableContext Context,
        PathRecordingStore Store);

    private static Harness Make()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var store = new PathRecordingStore();
        var context = new ControllableContext();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, locator, context);
        var settings = session.OpenSettings(() => true, _ => { });
        return new Harness(session, settings, daemon, locator, context, store);
    }

    /// <summary>A store that remembers what went where, since where is the whole subject here.</summary>
    private sealed class PathRecordingStore : ISettingsFileStore
    {
        public List<(string Path, Settings Settings)> Wrote { get; } = new();

        public bool SaveSucceeds { get; set; } = true;

        public void Save(Settings settings, string path) => TrySave(settings, path);

        public bool TrySave(Settings settings, string path)
        {
            if (!SaveSucceeds) return false;

            Wrote.Add((path, settings));
            return true;
        }

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = null;
            return false;
        }
    }

    /// <summary>
    /// A real single-threaded context, which records anything the library runs somewhere else.
    /// </summary>
    /// <remarks>
    /// The queued context every other test here uses reports <c>IsCurrent</c> true unconditionally and
    /// runs work on whatever thread drains it, so it cannot tell "on the context" from "not". That is
    /// convenient and it is exactly what hid a continuation escaping to the thread pool.
    /// </remarks>
    private sealed class OneThreadContext : IOtdExecutionContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = new();
        private readonly System.Threading.Thread _thread;

        public OneThreadContext()
        {
            _thread = new System.Threading.Thread(() =>
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(
                    new QueueContext(_queue));
                foreach (var work in _queue.GetConsumingEnumerable()) work();
            })
            { IsBackground = true, Name = "host-context" };
            _thread.Start();
        }

        /// <summary>Host callbacks that arrived, and the ones that arrived in the wrong place.</summary>
        public List<SettingsSaveState> SaveStates { get; } = new();

        public List<string> Offences { get; } = new();

        public bool IsCurrent => System.Threading.Thread.CurrentThread == _thread;

        /// <summary>Records a host callback, and whether it was delivered where it was promised.</summary>
        public void Observe(SettingsSaveState state)
        {
            lock (SaveStates)
            {
                SaveStates.Add(state);
                if (!IsCurrent) Offences.Add($"{state} on thread {Environment.CurrentManagedThreadId}");
            }
        }

        public Task PostAsync(Action work)
        {
            if (IsCurrent)
            {
                try { work(); return Task.CompletedTask; }
                catch (Exception ex) { return Task.FromException(ex); }
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try { work(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
            });
            return done.Task;
        }

        /// <summary>Runs <paramref name="body"/> on this context, with its awaits resuming here too.</summary>
        /// <remarks>
        /// A host calls the library from its own context; a test that calls from xunit's thread is
        /// measuring the wrong boundary, and will see the library "leave" a context it was never on. The
        /// thread installs a synchronization context, so awaits inside the body come back to it.
        /// </remarks>
        public Task RunAsync(Func<Task> body)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(async void () =>
            {
                try { await body().ConfigureAwait(true); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
            });
            return done.Task;
        }

        public void Dispose() => _queue.CompleteAdding();

        private sealed class QueueContext(System.Collections.Concurrent.BlockingCollection<Action> queue)
            : System.Threading.SynchronizationContext
        {
            public override void Post(System.Threading.SendOrPostCallback d, object? state)
            {
                if (!queue.IsAddingCompleted) queue.Add(() => d(state));
            }
        }
    }

    private static Settings Tablet(string name) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = name } } };

    private static string Tablet(Settings? s) => s?.Profiles[0].Tablet ?? "";
}
