using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;
using OtdInterop;

namespace OpenTabletArtist.Tests;

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
            settingsPath: () => path.Value,
            isOwnedDaemon: () => true,
            onSaveState: states.Add,
            log: NullOtdLog.Instance,
            policy: OtaSettingsPolicy.Instance);
        return (coordinator, daemon, store, states);
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
        var (coordinator, _, _, _) = Make();
        Assert.True((await coordinator.ApplyEphemeralAsync(SettingsFor("A's per-app snapshot", locked: true))).IsLive);
        Assert.True(coordinator.HasEphemeralOverride);

        coordinator.ResetForNewDaemon();

        Assert.False(coordinator.HasEphemeralOverride);
    }

    [Fact]
    public async Task ResetForNewDaemon_DropsEverythingBoundToTheOldOne()
    {
        var (coordinator, _, store, states) = Make();
        store.SaveSucceeds = false;
        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.True(coordinator.HasUnsavedChange);
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        coordinator.ResetForNewDaemon();

        Assert.False(coordinator.HasUnsavedChange);
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
        var (coordinator, _, store, _) = Make();
        store.SaveSucceeds = false;
        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.True(coordinator.HasUnsavedChange);

        Assert.True(coordinator.ResetForNewDaemon());
    }

    [Fact]
    public async Task ResetForNewDaemon_SaysNothingWhenThereWasNothingToLose()
    {
        // A switch with no pending edit is routine. Announcing it would train the user to dismiss the
        // notice that matters.
        var (coordinator, _, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Saved fine", locked: true));
        Assert.False(coordinator.HasUnsavedChange);

        Assert.False(coordinator.ResetForNewDaemon());
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

        coordinator.ResetForNewDaemon();
        path.Value = OtherPath;
        daemon.Applied.Clear();

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("Same", locked: true));

        Assert.NotEqual(SettingsApplyStatus.NoChange, outcome.Status);
        Assert.Single(daemon.Applied);   // it actually reached the new daemon
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
        coordinator.ResetForNewDaemon();
        path.Value = OtherPath;

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

        coordinator.ResetForNewDaemon();
        path.Value = OtherPath;
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

        coordinator.ResetForNewDaemon();
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
        coordinator.ResetForNewDaemon();
        path.Value = OtherPath;
        hold.SetResult(true);
        await apply;

        daemon.Applied.Clear();
        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("B's edit", locked: true));

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.Equal("B's edit", Tablet(store.OnDiskAt(OtherPath)));
        Assert.Single(daemon.Applied);
    }
}
