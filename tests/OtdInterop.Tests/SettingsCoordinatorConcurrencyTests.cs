using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;
using OtdInterop;

namespace OtdInterop.Tests;

/// <summary>
/// What happens when settings operations overlap — #774, #775, #776, from the second follow-up review
/// of #731.
///
/// Every earlier coordinator test drove a daemon that answered immediately, so no test in the suite could
/// observe an interleaving even in principle. The defects those tests missed all live in the same place:
/// each operation reads shared state, awaits the daemon, and writes that state back, and nothing said
/// what happens when a second operation arrives in the middle.
///
/// These hold the daemon call open with a <see cref="TaskCompletionSource"/> rather than sleeping, so the
/// interleaving is exact and the test is not timing-dependent.
///
/// <c>SettingsCoordinator</c> is headless and thread-agnostic by design, so it is exercised directly here
/// rather than through <c>AppSession</c> and a dispatcher.
/// </summary>
public class SettingsCoordinatorConcurrencyTests
{
    /// <summary>
    /// Stateful on purpose: <c>TryLoad</c> returns whatever was last written, the way a real file does.
    /// A fake with a fixed load value would let "restore the saved default" restore something that is not
    /// on disk, which is precisely the agreement these tests exist to check.
    /// </summary>
    /// <summary>
    /// Stateful and keyed by path: <c>TryLoad</c> returns whatever was last written there, the way a real
    /// file does. Keyed rather than single-file because two daemons have two settings files, and "A's
    /// pending save never reached B's file" is not expressible against one (#787).
    /// </summary>
    private sealed class FakeStore : ISettingsFileStore
    {
        private readonly Dictionary<string, Settings> _files = new(StringComparer.OrdinalIgnoreCase);

        public bool SaveSucceeds { get; set; } = true;
        public List<string> Writes { get; } = new();

        /// <summary>What a restart would load from <paramref name="path"/>, or null if nothing is there.</summary>
        public Settings? OnDiskAt(string path) =>
            _files.TryGetValue(path, out var s) ? Clone(s) : null;

        /// <summary>The default path these tests use when only one daemon is involved.</summary>
        public Settings? OnDisk => OnDiskAt(DefaultPath);

        public void Seed(Settings settings, string path = DefaultPath) => _files[path] = Clone(settings);

        public void Save(Settings settings, string path) => TrySave(settings, path);

        public bool TrySave(Settings settings, string path)
        {
            if (!SaveSucceeds) return false;
            // Clone on write: the caller may keep mutating its object, and a real file would not change
            // underneath us when it does.
            _files[path] = Clone(settings);
            Writes.Add(Json(settings));   // content only; which file it went to is OnDiskAt's job
            return true;
        }

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = OnDiskAt(path);
            return settings != null;
        }
    }

    private const string DefaultPath = "A/settings.json";

    private static Settings Clone(Settings s) =>
        JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(s))!;

    /// <summary>
    /// A close woken by an abandonment answers false, on its own (#893).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It used to answer true. The wait is released either by the last operation finishing or by an
    /// <c>Abandon</c> that gave up on it, and this took the same path for both — leaving the session's
    /// <c>settled &amp;&amp; !_tornDown</c> to repair the answer afterwards. So a question this object can
    /// answer about its own work depended on an ordering somewhere else, and that ordering was wrong
    /// twice (#891, #893). Each time, abandoned work was reported as having finished.
    /// </para>
    /// <para>
    /// Tested here rather than through the session on purpose: through the session it passes on the outer
    /// guard even when this answer is wrong, which is how it stayed wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CloseAsync_AnswersFalse_WhenWhatItWaitedOnWasAbandoned()
    {
        var (coordinator, daemon, _, _) = Make();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        _ = coordinator.ApplyAndSaveAsync(SettingsFor("Held", locked: false));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = coordinator.CloseAsync(TimeSpan.FromMinutes(5));
        coordinator.Abandon();

        Assert.False(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.False(held.Task.IsCompleted);   // the work was never finished, only given up on
    }

    /// <summary>Work that actually finishes still answers true — the case the fix must not break.</summary>
    /// <remarks>
    /// True means the admitted work finished, not that it saved successfully; an operation can finish
    /// having failed to save, and that belongs to its save-state rather than to the shape of the close.
    /// </remarks>
    [Fact]
    public async Task CloseAsync_AnswersTrue_WhenTheWorkFinishes()
    {
        var (coordinator, daemon, _, _) = Make();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        var applying = coordinator.ApplyAndSaveAsync(SettingsFor("Finishes", locked: false));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = coordinator.CloseAsync(TimeSpan.FromMinutes(5));
        held.TrySetResult(true);
        await applying.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    /// <summary>And a close that arrives after the abandonment answers false from a standing start.</summary>
    [Fact]
    public async Task CloseAsync_AnswersFalse_WhenItArrivesAfterTheAbandonment()
    {
        var (coordinator, daemon, _, _) = Make();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        _ = coordinator.ApplyAndSaveAsync(SettingsFor("Held", locked: false));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        coordinator.Abandon();

        Assert.False(await coordinator.CloseAsync(TimeSpan.FromMinutes(5))
            .WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private static string Json(Settings? s) => JsonConvert.SerializeObject(s);

    private static Settings SettingsFor(string tablet, bool locked) => new()
    {
        LockUsableAreaDisplay = locked,
        Profiles = new ProfileCollection { new Profile { Tablet = tablet } },
    };

    private static string Tablet(Settings? s) => s?.Profiles[0].Tablet ?? "";

    /// <summary>Mutable so a test can move the destination the way a reconnect to another daemon does.</summary>
    private sealed class PathHolder { public string Value { get; set; } = DefaultPath; }

    private static (SettingsCoordinator coordinator, FakeDaemonTransport daemon, FakeStore store,
        List<SettingsSaveState> states) Make() => Make(new PathHolder());

    private static (SettingsCoordinator coordinator, FakeDaemonTransport daemon, FakeStore store,
        List<SettingsSaveState> states) Make(PathHolder path)
    {
        var daemon = new FakeDaemonTransport();
        var store = new FakeStore();
        var states = new List<SettingsSaveState>();
        var coordinator = new SettingsCoordinator(
            daemon, store,
            isOwnedDaemon: () => true,
            onSaveState: states.Add,
            log: NullOtdLog.Instance,
            policy: NoPolicy.Instance);

        // The connection starts identified, which is the ordinary state and what every test written
        // before #828 assumed. A test that wants the window BEFORE identification reconnects and does not
        // call Identify.
        Identify(coordinator, daemon, path);

        return (coordinator, daemon, store, states);
    }

    /// <summary>
    /// Tells the coordinator where the daemon on the current channel keeps its settings.
    /// </summary>
    /// <remarks>
    /// What <see cref="OtdSession"/> does for itself once it has asked the daemon's <c>AppInfo</c>. A
    /// coordinator built directly, as these tests build it, has no session to do that — so a test that
    /// reconnects and then expects a save to land has to say that the new connection was identified,
    /// because otherwise it was not.
    /// </remarks>
    private static void Identify(SettingsCoordinator coordinator, FakeDaemonTransport daemon,
        PathHolder path) => coordinator.LearnDestination(path.Value, daemon.Incarnation);

    /// <summary>
    /// A daemon change as it actually happens: a new channel, and then the reset for it.
    /// </summary>
    /// <remarks>
    /// These tests used to reset without moving the channel, which nothing real does — one pipe
    /// connection is one daemon process, so a different daemon always means a different channel. It
    /// stopped being a harmless simplification once the reset had to tell a pending write the NEW daemon
    /// accepted from one belonging to the daemon that has gone: with the channel left still, every
    /// pending write looked like the new daemon's.
    /// </remarks>
    private static void SwitchDaemon(SettingsCoordinator coordinator, FakeDaemonTransport daemon)
    {
        daemon.ReconnectSilently();
        coordinator.ResetForNewDaemon(daemon.Incarnation);
    }

    /// <summary>A daemon call that does not answer until the test says so.</summary>
    private static TaskCompletionSource<bool> HoldNextSetSettings(FakeDaemonTransport daemon,
        Action<Settings>? onSent = null)
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var used = false;
        daemon.SetSettingsHandler = sent =>
        {
            // Later calls answer immediately, and honour whatever the test has since set.
            if (used) return Task.FromResult(daemon.SetSettingsSucceeds);
            used = true;
            onSent?.Invoke(sent);
            return hold.Task;
        };
        return hold;
    }

    // --- #774: the revision an apply owns -------------------------------------------------

    [Fact]
    public async Task AnEditDuringAnInFlightApply_IsNotWhatGetsPersisted()
    {
        var (coordinator, daemon, store, _) = Make();
        // One object, edited in place — how the tablet editor actually works.
        var shared = SettingsFor("Tablet", locked: true);

        string? sent = null;
        var hold = HoldNextSetSettings(daemon, s => sent = Json(s));

        var apply = coordinator.ApplyAndSaveAsync(shared);
        shared.LockUsableAreaDisplay = false;   // a second edit lands while the RPC is pending
        hold.SetResult(true);                   // the daemon accepts the FIRST revision

        var outcome = await apply;

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.True(store.OnDisk!.LockUsableAreaDisplay);
        // The stronger claim, and the one that generalises: what went to the daemon and what went to disk
        // are the same revision, whatever happened to the caller's object in between.
        Assert.Equal(sent, Assert.Single(store.Writes));
    }

    [Fact]
    public async Task AnEditDuringAnInFlightApply_IsNotWhatTheRetryPersists()
    {
        var (coordinator, daemon, store, _) = Make();
        var shared = SettingsFor("Tablet", locked: true);
        store.SaveSucceeds = false;

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(shared);
        shared.LockUsableAreaDisplay = false;
        hold.SetResult(true);

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, (await apply).Status);
        Assert.True(coordinator.HasUnsavedChange);

        // The disk comes back, and the retry writes the revision the daemon accepted — not the edit that
        // arrived while it was being applied.
        store.SaveSucceeds = true;
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, (await coordinator.RetryPersistAsync()).Status);
        Assert.True(store.OnDisk!.LockUsableAreaDisplay);
    }

    [Fact]
    public async Task AnEditTheDaemonRefused_IsNeverPersisted()
    {
        var (coordinator, daemon, store, _) = Make();
        var shared = SettingsFor("Tablet", locked: true);

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(shared);

        // The second edit is rejected outright, so it must not reach disk by any route.
        shared.LockUsableAreaDisplay = false;
        daemon.SetSettingsSucceeds = false;
        // Started, not awaited: operations are serialized now, so awaiting it here would wait on the
        // apply that this test has not released yet.
        var second = coordinator.ApplyAndSaveAsync(shared);
        hold.SetResult(true);
        await apply;

        Assert.Equal(SettingsApplyStatus.Disconnected, (await second).Status);
        Assert.True(store.OnDisk!.LockUsableAreaDisplay);
    }

    // --- #775: apply and restore have an order --------------------------------------------

    [Fact]
    public async Task ARestoreDuringAnInFlightApply_LeavesTheDaemonAndTheFileAgreeing()
    {
        var (coordinator, daemon, store, _) = Make();
        store.Seed(SettingsFor("Saved", locked: false));

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(SettingsFor("Edited", locked: true));
        var restore = coordinator.RestoreDefaultAsync();   // asked for while the apply is still out
        hold.SetResult(true);

        await apply;
        Assert.True((await restore).IsRestored);

        // The property that has to hold however the two are ordered: what the tablet is running and what
        // a restart would load are the same thing.
        Assert.Equal(Tablet(store.OnDisk), Tablet(daemon.Settings));
        Assert.False(coordinator.HasUnsavedChange);
    }

    /// <summary>
    /// The discriminating case. With the apply's disk write failing, the apply leaves a pending save
    /// behind it — and #764's fix (discarding it on restore) ran before the apply had set it.
    /// </summary>
    [Fact]
    public async Task ARestoreDuringAnInFlightApply_DoesNotLeaveTheDiscardedEditPendingOnDisk()
    {
        var (coordinator, daemon, store, _) = Make();
        store.Seed(SettingsFor("Saved", locked: false));
        store.SaveSucceeds = false;

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(SettingsFor("Edited", locked: true));
        var restore = coordinator.RestoreDefaultAsync();
        hold.SetResult(true);

        await apply;
        Assert.True((await restore).IsRestored);

        // Nothing is outstanding: the edit was discarded by the restore, so no later retry may write it.
        Assert.False(coordinator.HasUnsavedChange);

        store.SaveSucceeds = true;
        await coordinator.RetryPendingPersistAsync();
        Assert.Equal("Saved", Tablet(store.OnDisk));
    }

    // --- #776: the save chip after a restore ----------------------------------------------

    [Fact]
    public async Task ASuccessfulRestore_ClearsTheFailedSaveState()
    {
        var (coordinator, _, store, states) = Make();
        store.Seed(SettingsFor("Saved", locked: false));
        store.SaveSucceeds = false;

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("Edited", locked: true))).Status);
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        Assert.True((await coordinator.RestoreDefaultAsync()).IsRestored);

        // The chip said "Couldn't save — your change is live but won't survive a restart" about a change
        // the user had just deliberately thrown away.
        Assert.False(coordinator.HasUnsavedChange);
        Assert.Equal(SettingsSaveState.None, states[^1]);
    }

    [Fact]
    public async Task AFailedRestore_LeavesTheFailureStanding()
    {
        var (coordinator, _, store, states) = Make();
        store.SaveSucceeds = false;   // nothing on disk to restore from, either

        await coordinator.ApplyAndSaveAsync(SettingsFor("Edited", locked: true));
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        Assert.Equal(SettingsRestoreStatus.SourceUnavailable,
            (await coordinator.RestoreDefaultAsync()).Status);

        // The change really is still live and still unsaved. Clearing the warning here would be the
        // opposite lie to the one #776 fixes.
        Assert.True(coordinator.HasUnsavedChange);
        Assert.Equal(SettingsSaveState.Failed, states[^1]);
    }

    // --- #787: a session belongs to one daemon ---------------------------------------------

    private const string OtherPath = "B/settings.json";

    /// <summary>
    /// The case that matters. A save fails against daemon A, the user switches to B — which has its own
    /// application data directory — and the retry must not redirect A's settings into B's file. The
    /// destination is resolved at retry time, so without binding, it follows whichever daemon is
    /// connected now.
    /// </summary>
    [Fact]
    public async Task APendingSaveIsNotWrittenToADifferentDaemonsFile()
    {
        var path = new PathHolder();
        var (coordinator, _, store, _) = Make(path);
        store.Seed(SettingsFor("B's own settings", locked: false), OtherPath);
        store.SaveSucceeds = false;

        // Applied against A, but the disk refused it.
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true))).Status);
        Assert.True(coordinator.HasUnsavedChange);

        // The user stops A and starts B. The disk is writable again — the earlier failure was A's.
        path.Value = OtherPath;
        store.SaveSucceeds = true;

        await coordinator.RetryPendingPersistAsync();

        Assert.Equal("B's own settings", Tablet(store.OnDiskAt(OtherPath)));
        Assert.False(coordinator.HasUnsavedChange);   // dropped, not carried to yet another daemon
    }

    [Fact]
    public async Task APendingSaveStillRetriesAgainstItsOwnFile()
    {
        // The guard must not break the thing the retry exists for: a momentarily locked file, same daemon.
        var path = new PathHolder();
        var (coordinator, _, store, _) = Make(path);
        store.SaveSucceeds = false;

        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.True(coordinator.HasUnsavedChange);

        store.SaveSucceeds = true;
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await coordinator.RetryPendingPersistAsync()).Status);
        Assert.Equal("A's edit", Tablet(store.OnDisk));
    }

    /// <summary>
    /// The second manifestation. An ephemeral override is a fact about one daemon; while it is set, the
    /// load path deliberately does not adopt what the daemon reports. Carried across a switch, it stops
    /// the new daemon's settings ever being read, so OTA edits and offers to persist the old one's.
    /// </summary>
    [Fact]
    public async Task AnOverrideDoesNotSurviveADaemonChange()
    {
        var (coordinator, daemon, _, _) = Make();
        Assert.True((await coordinator.ApplyEphemeralAsync(SettingsFor("A's per-app snapshot", locked: true))).IsLive);
        Assert.True(coordinator.HasEphemeralOverride);

        SwitchDaemon(coordinator, daemon);

        Assert.False(coordinator.HasEphemeralOverride);
    }

    [Fact]
    public async Task ResetForNewDaemon_DropsEverythingBoundToTheOldOne()
    {
        var (coordinator, daemon, store, states) = Make();
        store.SaveSucceeds = false;
        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.True(coordinator.HasUnsavedChange);
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        SwitchDaemon(coordinator, daemon);

        Assert.False(coordinator.HasUnsavedChange);

        // The reset itself tells the host NOTHING. Announcing is a call into host code, and host code can
        // reenter -- bring up another daemon, whose transition then commits while this one is half-done.
        // So the state change finishes first and the caller announces afterwards.
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        coordinator.AnnounceDiscardedChange();

        // The chip was describing A's unsaved change; it is not the new daemon's problem.
        Assert.Equal(SettingsSaveState.None, states[^1]);
    }

    /// <summary>
    /// The reset reports whether anything the user would notice was lost, so the caller can tell them
    /// (#787). Everything else it drops is bookkeeping they never saw; a pending write is an edit they
    /// made, and a save chip going quiet is not an explanation for losing it.
    /// </summary>
    [Fact]
    public async Task ResetForNewDaemon_SaysWhenAnEditWasThrownAway()
    {
        var (coordinator, daemon, store, _) = Make();
        store.SaveSucceeds = false;
        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.True(coordinator.HasUnsavedChange);

        daemon.ReconnectSilently();
        Assert.True(coordinator.ResetForNewDaemon(daemon.Incarnation));
    }

    [Fact]
    public async Task ResetForNewDaemon_SaysNothingWhenThereWasNothingToLose()
    {
        // A switch with no pending edit is routine. Announcing it would train the user to dismiss the
        // notice that matters.
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Saved fine", locked: true));
        Assert.False(coordinator.HasUnsavedChange);

        daemon.ReconnectSilently();
        Assert.False(coordinator.ResetForNewDaemon(daemon.Incarnation));
    }

    /// <summary>
    /// The no-op guard compares against the last thing written to disk. Left over from A, it could skip
    /// an apply that B has never seen — the settings match what A's file held, not what B is running.
    /// </summary>
    [Fact]
    public async Task AfterADaemonChange_AnIdenticalApplyIsNotSkipped()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);
        var edit = SettingsFor("Same", locked: true);
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, (await coordinator.ApplyAndSaveAsync(edit)).Status);

        SwitchDaemon(coordinator, daemon);
        path.Value = OtherPath;
        Identify(coordinator, daemon, path);      // B answered, and said where it lives
        daemon.Applied.Clear();

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("Same", locked: true));

        Assert.NotEqual(SettingsApplyStatus.NoChange, outcome.Status);
        Assert.Single(daemon.Applied);   // it actually reached the new daemon
    }
    // --- #807 Phase 5: the baseline is the library's to record -------------------------------

    /// <summary>
    /// While a transient override is running, re-applying what the daemon held before it is not skipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The no-op guard's subject is "would this change anything", and during an override the daemon is
    /// running something else entirely — so the settings it held before are not what it has now, and
    /// sending them is a real change. Skipping it would leave the daemon on the override with the editor
    /// believing it had been put back (#737).
    /// </para>
    /// <para>
    /// It follows from the baseline being what the daemon <em>accepted</em>: during an override that is
    /// the snapshot, so the user's own settings no longer match it and the guard cannot fire. The
    /// host-driven version needed a special case to reach the same answer, because it recorded what this
    /// session was publishing, which during an override is exactly what the daemon is not running.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DuringAnOverride_ReapplyingWhatTheDaemonHeldBefore_IsStillSent()
    {
        var (coordinator, daemon, _, _) = Make();

        var original = SettingsFor("A's own", locked: true);
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await coordinator.ApplyAndSaveAsync(original)).Status);

        // The daemon is now running something else on this session's behalf.
        Assert.True((await coordinator.ApplyEphemeralAsync(SettingsFor("A per-app snapshot", locked: true))).IsLive);
        Assert.True(coordinator.HasEphemeralOverride);

        daemon.Applied.Clear();
        var again = await coordinator.ApplyAndSaveAsync(SettingsFor("A's own", locked: true));

        // Not NoChange: the daemon does not hold these, whatever this session last published.
        Assert.NotEqual(SettingsApplyStatus.NoChange, again.Status);
        Assert.Single(daemon.Applied);
    }

    /// <summary>
    /// An accepted apply becomes the baseline, so repeating it is recognised as changing nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard needs a baseline, and until now the only thing that set one was the host calling
    /// <c>RecordLoadedBaseline</c> after its load. So the coordinator on its own could apply the same
    /// settings for ever and send every one of them; whether the guard worked depended on a host having
    /// done something unrelated first.
    /// </para>
    /// <para>
    /// Recorded where the daemon accepts instead, which is the only moment this session knows what the
    /// daemon has. Removing that recording makes the second apply a real send, which is what this
    /// detects.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAcceptedApply_BecomesTheBaselineForTheNextOne()
    {
        var (coordinator, daemon, _, _) = Make();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("Same", locked: true))).Status);
        Assert.Single(daemon.Applied);

        var again = await coordinator.ApplyAndSaveAsync(SettingsFor("Same", locked: true));

        Assert.Equal(SettingsApplyStatus.NoChange, again.Status);
        Assert.Single(daemon.Applied);          // and nothing further reached the daemon
    }

    /// <summary>
    /// Explicitly applying the settings the daemon already runs under an override still ends the
    /// override.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The no-op guard asks whether the transport write would change anything. That is not the same
    /// question as whether the <em>operation</em> would: an apply-and-save also publishes the revision and
    /// ends any per-app override, and those are necessary even when the daemon is already running the
    /// bytes. Returning early skipped them, so the override stayed live and this session went on
    /// publishing something else — which a later clear-override would then send back to the daemon.
    /// </para>
    /// <para>
    /// Enabled by recording the baseline from what the daemon accepted, which let the guard fire during
    /// an override for the first time. Codex's probe on #858; the risk was flagged and real.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ApplyingWhatAnOverrideAlreadyRuns_StillEndsTheOverride()
    {
        var (coordinator, daemon, _, _) = Make();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: true))).Status);

        // The editor moves to B live-only: published B, disk still A.
        Assert.True((await coordinator.ApplyLiveOnlyAsync(SettingsFor("B", locked: true))).IsLive);

        // And an override puts the daemon back on A.
        Assert.True((await coordinator.ApplyEphemeralAsync(SettingsFor("A", locked: true))).IsLive);
        Assert.True(coordinator.HasEphemeralOverride);

        // Now the user explicitly saves A. The daemon already has those bytes; the session does not.
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: true));

        Assert.False(coordinator.HasEphemeralOverride);
        Assert.Equal("A", Tablet(coordinator.GetCurrent()?.Settings));

        // And ending an override that is already over sends nothing stale back.
        daemon.Applied.Clear();
        await coordinator.ClearEphemeralOverrideAsync();
        Assert.DoesNotContain(daemon.Applied, sent => Tablet(sent) == "B");
    }

    /// <summary>
    /// A superseded daemon's successful reply does not become the current daemon's baseline.
    /// </summary>
    /// <remarks>
    /// Advancing the observation epoch for a reply that arrives late is conservative and right: an
    /// outstanding read is stale either way. Recording its <em>content</em> is not, because the content
    /// describes a daemon this session has moved off. Both used to happen in the same helper, before the
    /// check that rejects the work.
    /// </remarks>
    [Fact]
    public async Task ASupersededReply_DoesNotBecomeTheBaseline()
    {
        var (coordinator, daemon, _, _) = Make();

        // A live-only apply to the old daemon, held mid-flight. It owns the mutation gate while held, so
        // nothing else that mutates can run -- which is why the new daemon's state is established by a
        // reload, which is a read and is not gated. My first attempt used an apply here and deadlocked.
        var hold = HoldNextSetSettings(daemon);
        var stale = coordinator.ApplyLiveOnlyAsync(SettingsFor("Old daemon's", locked: true));

        SwitchDaemon(coordinator, daemon);
        daemon.Settings = SettingsFor("New daemon's", locked: true);
        Assert.Equal(SettingsReloadStatus.Adopted, (await coordinator.ReloadFromDaemonAsync()).Status);

        hold.SetResult(true);                       // the old daemon answers, successfully, far too late
        Assert.Equal(SettingsApplyStatus.Superseded, (await stale).Status);

        // Read directly, because the guard needs the persisted baseline to agree as well and a reset
        // clears that -- so no sequence reachable from outside can turn this contamination into a
        // skipped apply on its own. It is still the wrong value to be holding, and holding it is what
        // this asserts.
        Assert.Equal(Json(SettingsFor("New daemon's", locked: true)),
            coordinator.LastObservedDaemonSettingsJson);
    }

    // --- #828 readiness: a connection nobody has identified yet ------------------------------

    /// <summary>
    /// Work admitted after a switch, but before anything has identified the new daemon, must not write
    /// the new daemon's settings into the old daemon's file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The half of #828 that channel binding does not cover, and the reason binding alone was never
    /// enough. The bound channel makes the <em>send</em> correct: it goes to B, which is the daemon that
    /// is actually connected. The destination on disk is a different question, and the answer still comes
    /// from the host — which learns B's settings file from B's <c>AppInfo</c>, during a data load that
    /// has not happened yet. So the apply is live on B and persisted into A's file.
    /// </para>
    /// <para>
    /// A's file belongs to an install OTA was never asked to touch, and which the user may also be
    /// driving with OpenTabletDriver's own UX. This is the same hazard #787 fixed for a <em>pending</em>
    /// write, where the destination had moved under a retry; what was left is the destination never
    /// having been right in the first place.
    /// </para>
    /// <para>
    /// Written before the fix, per #828's own instruction that the ordering tests drive the design.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnApplyAdmittedBeforeTheNewDaemonIsIdentified_DoesNotWriteIntoTheOldDaemonsFile()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);

        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.Equal("A's edit", Tablet(store.OnDiskAt(DefaultPath)));

        // B answers. Nothing has identified it yet, so the host still believes the settings file is A's:
        // that only moves once a data load has read B's AppInfo.
        daemon.Reconnect();

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("B's edit", locked: true));

        // Whatever else happens, A's file must still hold A's edit.
        Assert.Equal("A's edit", Tablet(store.OnDiskAt(DefaultPath)));
        Assert.NotEqual(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
    }

    // --- #803: a daemon change invalidates work that is queued or in flight ------------------
    //
    // #787 stopped the coordinator from CARRYING state between daemons. It did not stop an operation
    // that straddles the change, and serializing every mutating path (#777) could not: the reset is
    // deliberately NOT behind the semaphore, because it must not queue behind work belonging to a daemon
    // that has gone. So there are two windows left, and each of these pins one of them.

    /// <summary>
    /// The window between "the RPC was sent" and "the result is written".
    ///
    /// The destination used to be resolved after the await, from whichever daemon was connected by then.
    /// An apply sent to A, completing after the user switched to B, therefore wrote A's settings into
    /// <b>B's</b> settings.json — the exact failure #789 closed for the pending-save retry and left open
    /// on the first write.
    /// </summary>
    [Fact]
    public async Task AnApplyThatOutlivesTheDaemon_DoesNotWriteToTheNewDaemonsFile()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);
        store.Seed(SettingsFor("B's own settings", locked: false), OtherPath);

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));

        // The user stops A and starts B while the apply is still waiting on A.
        SwitchDaemon(coordinator, daemon);
        path.Value = OtherPath;
        Identify(coordinator, daemon, path);      // B answered, and said where it lives

        hold.SetResult(true);   // A answers, too late to matter
        var outcome = await apply;

        // Asserted before the status, because this is the damage: B's file, holding A's edit.
        Assert.Equal("B's own settings", Tablet(store.OnDiskAt(OtherPath)));
        Assert.Equal(SettingsApplyStatus.Superseded, outcome.Status);
        Assert.False(coordinator.HasUnsavedChange);   // nor is it left pending against B
    }

    /// <summary>
    /// The other window: an operation that is still <em>queued</em> when the daemon changes.
    ///
    /// The generation has to be captured before the wait, not after acquiring the semaphore — time spent
    /// queued is exactly when the daemon can change underneath a caller. Sampling it inside would see the
    /// new value and send A's edit to B.
    /// </summary>
    [Fact]
    public async Task AnApplyQueuedWhenTheDaemonChanges_IsNeverSent()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);

        var hold = HoldNextSetSettings(daemon);
        var first = coordinator.ApplyAndSaveAsync(SettingsFor("A's first edit", locked: true));

        // Queued behind the first, still meant for A. Started, not awaited — awaiting here would
        // deadlock the test against the semaphore the first apply is holding.
        var queued = coordinator.ApplyAndSaveAsync(SettingsFor("A's second edit", locked: false));

        SwitchDaemon(coordinator, daemon);
        path.Value = OtherPath;
        Identify(coordinator, daemon, path);      // B answered, and said where it lives
        hold.SetResult(true);

        await first;
        Assert.Equal(SettingsApplyStatus.Superseded, (await queued).Status);

        // One call reached the daemon — the one that was already in flight. The queued edit was dropped
        // rather than delivered to a daemon it was never meant for.
        Assert.Single(daemon.Applied);
        Assert.Null(store.OnDiskAt(OtherPath));
    }

    /// <summary>
    /// The same window for a per-app override. Recording one against B would suppress B's settings read
    /// on the strength of an override B never received — #737's failure, reintroduced by a daemon switch.
    /// </summary>
    [Fact]
    public async Task AnEphemeralOverrideThatOutlivesTheDaemon_IsNotRecorded()
    {
        var (coordinator, daemon, _, _) = Make();

        var hold = HoldNextSetSettings(daemon);
        var ephemeral = coordinator.ApplyEphemeralAsync(SettingsFor("Per-app snapshot", locked: true));

        SwitchDaemon(coordinator, daemon);
        hold.SetResult(true);

        // Superseded, not merely "false": the session it was for had ended.
        Assert.Equal(SettingsApplyStatus.Superseded, (await ephemeral).Status);
        Assert.False(coordinator.HasEphemeralOverride);
    }

    /// <summary>
    /// The control. Every assertion above is "nothing happened", which a coordinator that had stopped
    /// working entirely would also satisfy. After the switch, B's own edits must still apply and save.
    /// </summary>
    [Fact]
    public async Task ButAfterTheSwitch_TheNewDaemonsEditsStillLand()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        SwitchDaemon(coordinator, daemon);
        path.Value = OtherPath;
        Identify(coordinator, daemon, path);      // B answered, and said where it lives
        hold.SetResult(true);
        await apply;

        daemon.Applied.Clear();
        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("B's edit", locked: true));

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.Equal("B's edit", Tablet(store.OnDiskAt(OtherPath)));
        Assert.Single(daemon.Applied);
    }
}
