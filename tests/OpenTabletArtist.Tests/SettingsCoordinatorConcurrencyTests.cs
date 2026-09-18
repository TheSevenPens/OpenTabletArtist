using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

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
    private sealed class FakeStore : ISettingsFileStore
    {
        private Settings? _onDisk;

        public bool SaveSucceeds { get; set; } = true;
        public List<string> Writes { get; } = new();

        /// <summary>What a restart would load. Null when nothing has ever been written.</summary>
        public Settings? OnDisk => _onDisk;

        public void Seed(Settings settings) => _onDisk = Clone(settings);

        public void Save(Settings settings, string path) => TrySave(settings, path);

        public bool TrySave(Settings settings, string path)
        {
            if (!SaveSucceeds) return false;
            // Clone on write: the caller may keep mutating its object, and a real file would not change
            // underneath us when it does.
            _onDisk = Clone(settings);
            Writes.Add(Json(settings));
            return true;
        }

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = _onDisk == null ? null : Clone(_onDisk);
            return settings != null;
        }
    }

    private static Settings Clone(Settings s) =>
        JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(s))!;

    private static string Json(Settings? s) => JsonConvert.SerializeObject(s);

    private static Settings SettingsFor(string tablet, bool locked) => new()
    {
        LockUsableAreaDisplay = locked,
        Profiles = new ProfileCollection { new Profile { Tablet = tablet } },
    };

    private static string Tablet(Settings? s) => s?.Profiles[0].Tablet ?? "";

    private static (SettingsCoordinator coordinator, FakeDaemonTransport daemon, FakeStore store,
        List<SettingsSaveState> states) Make()
    {
        var daemon = new FakeDaemonTransport();
        var store = new FakeStore();
        var states = new List<SettingsSaveState>();
        var coordinator = new SettingsCoordinator(
            daemon, store,
            settingsPath: () => "settings.json",
            isOwnedDaemon: () => true,
            onSaveState: states.Add);
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
}
