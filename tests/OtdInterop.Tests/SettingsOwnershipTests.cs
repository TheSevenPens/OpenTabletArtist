using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// Who owns a settings object handed to the coordinator, now that the answer is "the caller, always".
///
/// <para>
/// These replace <c>SettingsOwnershipCharacterizationTests</c>, which pinned the opposite behaviour so
/// that changing it would have to be deliberate. Four of those expectations are retired here, and each
/// was retired because it described something the library should not have been doing:
/// </para>
///
/// <list type="bullet">
/// <item><c>ApplyAndSave_EditsTheCallersOwnObject</c>, <c>ApplyLiveOnly_…</c>,
/// <c>ApplyEphemeral_…</c> — policy ran on the caller's object, in place, so applying settings silently
/// edited what the app was still holding.</item>
/// <item><c>ApplyLiveOnly_SendsTheCallersOwnInstance</c> and <c>ApplyEphemeral_…</c> — those paths handed
/// the caller's live object to the transport.</item>
/// <item><c>OnTheLiveOnlyPath_AnEditDuringTheApply_ReachesTheDaemon</c> — the consequence of the
/// previous one, and the same hazard that was fixed for apply-and-save alone.</item>
/// </list>
///
/// <para>
/// <c>ApplyAndSave_SendsACopy_NotTheCallersInstance</c> is not retired. It was already true and it is
/// still true; it just applies to all three paths now.
/// </para>
/// </summary>
public class SettingsOwnershipTests
{
    /// <summary>A third-party filter the app does not own. Policy disables it on a daemon we own.</summary>
    private const string ThirdPartyFilter = "OpenTabletDriver.Filters.Noise.NoiseReduction";

    private sealed class NoopStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }
        public bool TrySave(Settings settings, string path) => true;
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    /// <summary>
    /// Records what it was handed, keeps it, and disables the bait filter — the things a policy is
    /// allowed to do.
    /// </summary>
    /// <remarks>
    /// The mutation used to come from calling the application's own policy, which meant these tests went
    /// red if OTA changed its filter rules — a coupling nobody chose, and one this suite exists to not
    /// have. What the library promises is that a policy runs against a private copy and that the outcome
    /// carries what was actually sent. Any policy that changes something can show that; which change it
    /// makes is the host's business.
    /// </remarks>
    private sealed class HoardingPolicy : IOtdSettingsPolicy
    {
        public List<Settings> SeenWorkingCopies { get; } = new();
        public Settings? Last { get; private set; }

        public void Apply(Settings workingCopy, SettingsPolicyContext context)
        {
            SeenWorkingCopies.Add(workingCopy);
            Last = workingCopy;

            foreach (var filter in workingCopy.Profiles[0].Filters)
                if (filter.Path == ThirdPartyFilter)
                    filter.Enable = false;
        }
    }

    private static Settings WithPolicyBait()
    {
        var profile = new Profile { Tablet = "T", AbsoluteModeSettings = null };
        profile.Filters.Add(new PluginSettingStore(ThirdPartyFilter) { Path = ThirdPartyFilter, Enable = true });
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static bool ThirdPartyFilterEnabled(Settings s)
    {
        foreach (var f in s.Profiles[0].Filters)
            if (f.Path == ThirdPartyFilter)
                return f.Enable;
        return false;
    }

    private static (SettingsCoordinator coordinator, FakeDaemonTransport daemon, HoardingPolicy policy) Make()
    {
        var daemon = new FakeDaemonTransport();
        var policy = new HoardingPolicy();
        var coordinator = new SettingsCoordinator(
            daemon, new NoopStore(),
            isOwnedDaemon: () => true,
            onSaveState: _ => { },
            log: NullOtdLog.Instance,
            policy: policy);
        coordinator.LearnDestination("A/settings.json", daemon.Incarnation);
        return (coordinator, daemon, policy);
    }

    public static TheoryData<string> EveryMutatingPath => new() { "save", "live", "ephemeral" };

    /// <summary>All three report an outcome now, so the helper can hand one back.</summary>
    private static Task<SettingsApplyOutcome> Apply(SettingsCoordinator c, string path, Settings s) => path switch
    {
        "save" => c.ApplyAndSaveAsync(s),
        "live" => c.ApplyLiveOnlyAsync(s),
        _ => c.ApplyEphemeralAsync(s),
    };

    // --- The caller's object is theirs ---------------------------------------------------------

    /// <summary>
    /// The guarantee the contract leads with. The tablet editor hands over the very object its bindings
    /// read from, so an apply that edited it would change what the user is looking at, without asking.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMutatingPath))]
    public async Task NoPath_EditsTheCallersObject(string path)
    {
        var (coordinator, _, _) = Make();
        var mine = WithPolicyBait();

        await Apply(coordinator, path, mine);

        Assert.True(ThirdPartyFilterEnabled(mine));          // policy did not reach it
        Assert.Null(mine.Profiles[0].AbsoluteModeSettings);  // nor did the format guard
    }

    [Theory]
    [MemberData(nameof(EveryMutatingPath))]
    public async Task NoPath_SendsTheCallersInstance(string path)
    {
        var (coordinator, daemon, _) = Make();
        var mine = WithPolicyBait();

        await Apply(coordinator, path, mine);

        Assert.Single(daemon.Applied);
        Assert.NotSame(mine, daemon.Applied[0]);
    }

    /// <summary>
    /// The reason isolation happens at admission rather than just before the call. An edit made while an
    /// apply is in flight belongs to the next operation, not this one — otherwise what the daemon
    /// receives is neither what was asked for nor what is on screen.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMutatingPath))]
    public async Task AnEditDuringAnApply_DoesNotReachTheDaemon(string path)
    {
        var (coordinator, daemon, _) = Make();
        var mine = WithPolicyBait();

        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = sent =>
        {
            daemon.Applied.Add(sent);
            return released.Task;
        };

        var apply = Apply(coordinator, path, mine);
        mine.LockUsableAreaDisplay = true;   // the caller edits its object mid-flight
        released.SetResult(true);
        await apply;

        Assert.False(daemon.Applied[0].LockUsableAreaDisplay);
    }

    // --- The policy gets a working copy, and what it keeps is inert -----------------------------

    [Fact]
    public async Task ThePolicy_NeverReceivesTheCallersObject()
    {
        var (coordinator, _, policy) = Make();
        var mine = WithPolicyBait();

        await coordinator.ApplyAndSaveAsync(mine);

        Assert.Single(policy.SeenWorkingCopies);
        Assert.NotSame(mine, policy.SeenWorkingCopies[0]);
    }

    /// <summary>
    /// A policy may hold on to what it was handed — the contract says so. What it must not hold is the
    /// object that then goes to the daemon, or it could change it afterwards.
    /// </summary>
    [Fact]
    public async Task WhatThePolicyKeeps_IsNotWhatIsSent()
    {
        var (coordinator, daemon, policy) = Make();

        await coordinator.ApplyAndSaveAsync(WithPolicyBait());

        Assert.Single(daemon.Applied);
        Assert.NotSame(policy.Last, daemon.Applied[0]);
    }

    // --- What came back ------------------------------------------------------------------------

    /// <summary>
    /// The caller cannot see what its request became unless it is told. Policy legitimately changes a
    /// request, and an editor that went on displaying its own draft would be showing settings that were
    /// never sent — and would look, to external-change detection, like somebody else had edited.
    /// </summary>
    [Fact]
    public async Task TheOutcome_CarriesWhatWasActuallySent()
    {
        var (coordinator, daemon, _) = Make();

        var outcome = await coordinator.ApplyAndSaveAsync(WithPolicyBait());

        var prepared = Assert.IsType<PreparedSettings>(outcome.Prepared);
        Assert.False(ThirdPartyFilterEnabled(prepared.Settings));         // policy applied
        Assert.NotNull(prepared.Settings.Profiles[0].AbsoluteModeSettings); // guard applied
        Assert.False(prepared.Stamp.IsNone);

        // A copy of what was sent, not the thing itself. The two must agree on content and disagree on
        // identity -- see the test below for what sharing it costs.
        Assert.NotSame(daemon.Applied[0], prepared.Settings);
        Assert.Equal(Json(daemon.Applied[0]), Json(prepared.Settings));
    }

    private static string Json(Settings s) => Newtonsoft.Json.JsonConvert.SerializeObject(s);

    /// <summary>
    /// Why the result needs a copy of its own, and not just as a matter of principle.
    ///
    /// The revision is this session's state, the object sent to the daemon, and -- when the write fails
    /// -- the pending retry, all at once. A caller that adopted that instance and went on editing it, as
    /// the editor does, would be editing the pending retry: the next retry would then write an edit the
    /// daemon never accepted, which is the disagreement between disk and daemon the retry exists to
    /// resolve, caused by the retry.
    /// </summary>
    [Fact]
    public async Task EditingTheReturnedRevision_CannotChangeWhatAPendingRetryWrites()
    {
        var daemon = new FakeDaemonTransport();
        var store = new RefusingStore();
        var coordinator = new SettingsCoordinator(
            daemon, store,
            isOwnedDaemon: () => true,
            onSaveState: _ => { },
            log: NullOtdLog.Instance,
            policy: new HoardingPolicy());
        coordinator.LearnDestination("A/settings.json", daemon.Incarnation);

        var outcome = await coordinator.ApplyAndSaveAsync(WithPolicyBait());
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, outcome.Status);   // a retry is now pending

        // The caller adopts the revision and keeps editing, exactly as the editor does.
        outcome.Prepared!.Settings.LockUsableAreaDisplay = true;

        store.SaveSucceeds = true;
        await coordinator.RetryPersistAsync();

        Assert.NotNull(store.LastWritten);
        Assert.False(store.LastWritten!.LockUsableAreaDisplay);
    }

    /// <summary>Fails the first write so a retry is pending, then records what the retry writes.</summary>
    private sealed class RefusingStore : ISettingsFileStore
    {
        public bool SaveSucceeds { get; set; }
        public Settings? LastWritten { get; private set; }

        public void Save(Settings settings, string path) => TrySave(settings, path);

        public bool TrySave(Settings settings, string path)
        {
            if (!SaveSucceeds) return false;
            LastWritten = settings;
            return true;
        }

        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    /// <summary>
    /// The published state is a copy too. Callers across the app read it, edit what they read, and apply
    /// the result; while it was the internal object, that pattern edited this session's state and any
    /// pending retry along with it.
    /// </summary>
    [Fact]
    public async Task EditingCurrentSettings_DoesNotChangeTheSessionsOwnState()
    {
        var (coordinator, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(WithPolicyBait());

        var read = coordinator.CurrentSettings!;
        read.LockUsableAreaDisplay = true;

        Assert.False(coordinator.CurrentSettings!.LockUsableAreaDisplay);
    }

    /// <summary>
    /// An apply the daemon refused changes nothing anywhere — including the caller's draft.
    ///
    /// This one is a fix, not a preservation. Policy used to run on the caller's object before the call
    /// was made, so a refused apply still left the draft repaired: the editor showed a filter as disabled
    /// by a change the daemon never received.
    /// </summary>
    [Fact]
    public async Task ARefusedApply_LeavesTheCallersDraftAlone()
    {
        var (coordinator, daemon, _) = Make();
        daemon.SetSettingsSucceeds = false;
        var mine = WithPolicyBait();

        var outcome = await coordinator.ApplyAndSaveAsync(mine);

        Assert.Equal(SettingsApplyStatus.Disconnected, outcome.Status);
        Assert.True(ThirdPartyFilterEnabled(mine));
        Assert.Null(mine.Profiles[0].AbsoluteModeSettings);
    }

    // --- The control ---------------------------------------------------------------------------

    /// <summary>
    /// Every assertion above is a form of "nothing happened to my object", which an apply that did
    /// nothing at all would satisfy just as well.
    /// </summary>
    [Fact]
    public async Task ButTheSettingsStillReachTheDaemon()
    {
        var (coordinator, daemon, _) = Make();

        var outcome = await coordinator.ApplyAndSaveAsync(WithPolicyBait());

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.Single(daemon.Applied);
        Assert.Equal("T", daemon.Applied[0].Profiles[0].Tablet);
    }

    // --- Each refusal says which refusal it was -------------------------------------------------

    /// <summary>
    /// The live-only and per-app paths used to return a bare <c>bool</c>. Three different things made it
    /// false — the copy failed, there was no transport, the session had ended — and the caller could not
    /// tell them apart, so a per-app switch that silently did nothing was hard to attribute.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMutatingPath))]
    public async Task WithNoTransport_EveryPathSaysDisconnected(string path)
    {
        var (coordinator, daemon, _) = Make();
        daemon.SetSettingsSucceeds = false;

        var outcome = await Apply(coordinator, path, WithPolicyBait());

        Assert.Equal(SettingsApplyStatus.Disconnected, outcome.Status);
        Assert.False(outcome.IsLive);
    }

    /// <summary>
    /// Applied, and not saving was the intent — distinct from a save that was wanted and failed.
    /// Conflating them would make a deliberate override look like something to retry.
    /// </summary>
    [Fact]
    public async Task ALiveOnlyApply_IsLiveButNotPersisted()
    {
        var (coordinator, _, _) = Make();

        var outcome = await coordinator.ApplyLiveOnlyAsync(WithPolicyBait());

        Assert.Equal(SettingsApplyStatus.AppliedLive, outcome.Status);
        Assert.True(outcome.IsLive);
        Assert.False(outcome.IsPersisted);
        Assert.False(outcome.NeedsPersistRetry);   // nothing to retry: nothing was meant to be written
    }

    /// <summary>
    /// With nothing loaded there is nowhere to put the daemon back to, so nothing is sent. This is the
    /// ONLY case that sends nothing -- see the test below, which covers the one people assume.
    /// </summary>
    [Fact]
    public async Task ClearingAnOverrideWithNothingLoaded_SendsNothing()
    {
        var (coordinator, daemon, _) = Make();

        var outcome = await coordinator.ClearEphemeralOverrideAsync();

        Assert.Equal(SettingsApplyStatus.NoChange, outcome.Status);
        Assert.Empty(daemon.Applied);
    }

    /// <summary>
    /// Clearing when no override was recorded still sends the baseline. The flag records what this
    /// session was TOLD, and a session that has just reconnected knows less about the daemon than the
    /// flag implies -- so "put it somewhere known" beats "trust the flag and skip".
    ///
    /// Pinned because it was previously described the other way round, as a short circuit that returns
    /// NoChange and skips the reload. It does not, and a caller written to that description would stop
    /// reloading after a switch that did reach the daemon.
    /// </summary>
    [Fact]
    public async Task ClearingAnOverrideThatWasNeverSet_StillSendsTheBaseline()
    {
        var (coordinator, daemon, _) = Make();
        await coordinator.ApplyAndSaveAsync(WithPolicyBait());
        Assert.False(coordinator.HasEphemeralOverride);
        daemon.Applied.Clear();

        var outcome = await coordinator.ClearEphemeralOverrideAsync();

        Assert.Equal(SettingsApplyStatus.AppliedLive, outcome.Status);
        Assert.True(outcome.ChangedTheDaemon);          // so the caller reloads
        Assert.Single(daemon.Applied);
    }

    /// <summary>
    /// A result that has just succeeded describes the state that now exists, so the session's own stamp
    /// must agree with it. It did not: the stamp was taken when the revision was published, and the
    /// daemon's acceptance then moved the same counter -- so every apply handed back a result the
    /// freshness test would reject, for no reason but having finished.
    /// </summary>
    [Fact]
    public async Task AnAppliedResult_IsNotStaleTheMomentItReturns()
    {
        var (coordinator, _, _) = Make();

        var outcome = await coordinator.ApplyAndSaveAsync(WithPolicyBait());

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        var prepared = Assert.IsType<PreparedSettings>(outcome.Prepared);
        Assert.Equal(coordinator.GetCurrent()!.Stamp, prepared.Stamp);
        Assert.False(prepared.Stamp.SupersededBy(coordinator.GetCurrent()!.Stamp));
    }

    /// <summary>
    /// A per-app override changes the daemon and publishes nothing, so there is no revision it could be
    /// stamped as. Handing one back anyway would stamp a transient snapshot with the baseline's revision
    /// -- indistinguishable, to a caller comparing stamps, from the settings it is supposed to save.
    /// Withholding it is what makes that impossible rather than merely discouraged.
    /// </summary>
    [Fact]
    public async Task APerAppOverride_HandsBackNothingToAdopt()
    {
        var (coordinator, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(WithPolicyBait());
        var baseline = coordinator.GetCurrent()!;

        var outcome = await coordinator.ApplyEphemeralAsync(SettingsFor("Per-app"));

        Assert.Equal(SettingsApplyStatus.AppliedLive, outcome.Status);
        Assert.Null(outcome.Prepared);
        // The published settings did not move, so neither did their revision.
        Assert.Equal(baseline.Stamp, coordinator.GetCurrent()!.Stamp);
        Assert.Equal(Json(baseline.Settings), Json(coordinator.GetCurrent()!.Settings));
    }

    /// <summary>
    /// A stamp from a session that has ended is worthless, not merely old. Reading "different session"
    /// as "not superseded" would let the stalest possible result through -- one belonging to a daemon
    /// the user has already switched away from.
    /// </summary>
    [Fact]
    public void AStampFromADaemonThatHasGone_IsSuperseded()
    {
        // Deliberately a HIGHER revision in the old session than in the new one: revisions from
        // different sessions are not comparable, and the only honest answer is "no longer current".
        Assert.True(new SettingsStamp(1, 9).SupersededBy(new SettingsStamp(2, 1)));

        // And equality within a session is not supersession -- a result stamped with the revision that
        // is still current describes the state that exists.
        Assert.False(new SettingsStamp(1, 9).SupersededBy(new SettingsStamp(1, 9)));
        Assert.True(new SettingsStamp(1, 9).SupersededBy(new SettingsStamp(1, 10)));
    }

    /// <summary>
    /// The current settings come back stamped, so a caller holding them can tell later whether the
    /// ground has moved. A stamp that never changed would be decoration.
    /// </summary>
    [Fact]
    public async Task GetCurrent_IsStampedAndMovesWhenTheStateDoes()
    {
        var (coordinator, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(WithPolicyBait());

        var first = coordinator.GetCurrent();
        Assert.NotNull(first);
        Assert.False(first!.Stamp.IsNone);

        await coordinator.ApplyAndSaveAsync(SettingsFor("Changed"));
        var second = coordinator.GetCurrent();

        Assert.NotNull(second);
        Assert.True(first.Stamp.SupersededBy(second!.Stamp));
        Assert.NotSame(first.Settings, second.Settings);
    }

    // --- Re-reading the daemon (#807 Phase 4) ----------------------------------------------------
    //
    // This used to be three steps in the host: observe the session's state, read the daemon, hand the
    // observation back when adopting. Every one of them failed silently if done in the wrong order, and
    // the session could not check that they had been. It is one call now, so these test the ordering
    // where it lives rather than through a view model.

    /// <summary>
    /// While an override is running the daemon is NOT holding the baseline, so it is not asked at all.
    /// Reading it here is the defect #737 fixed: the transient snapshot becomes the editor's baseline,
    /// what a save writes as the user's default, and what a restore restores to.
    /// </summary>
    [Fact]
    public async Task ReloadingWhileAnOverrideIsRunning_DoesNotReadTheDaemon()
    {
        var (coordinator, daemon, _) = Make();
        await coordinator.ApplyAndSaveAsync(WithPolicyBait());
        await coordinator.ApplyEphemeralAsync(SettingsFor("Per-app"));
        var baseline = Json(coordinator.CurrentSettings!);
        var readsBefore = daemon.GetSettingsCalls;

        var outcome = await coordinator.ReloadFromDaemonAsync();

        Assert.Equal(SettingsReloadStatus.SkippedOverride, outcome.Status);
        Assert.Equal(readsBefore, daemon.GetSettingsCalls);      // not asked, not merely ignored
        Assert.Equal(baseline, Json(coordinator.CurrentSettings!));
    }

    /// <summary>The ordinary case: what the daemon returned becomes the baseline, stamped.</summary>
    [Fact]
    public async Task Reloading_AdoptsWhatTheDaemonReturned()
    {
        var (coordinator, daemon, _) = Make();
        daemon.Settings = SettingsFor("From the daemon");

        var outcome = await coordinator.ReloadFromDaemonAsync();

        Assert.Equal(SettingsReloadStatus.Adopted, outcome.Status);
        Assert.True(outcome.ChangedTheBaseline);
        Assert.Equal("From the daemon", coordinator.CurrentSettings!.Profiles[0].Tablet);
        var adopted = Assert.IsType<PreparedSettings>(outcome.Adopted);
        Assert.Equal(coordinator.GetCurrent()!.Stamp, adopted.Stamp);
    }

    /// <summary>
    /// A daemon that answers with nothing leaves an empty baseline, deliberately -- keeping the previous
    /// daemon's settings would be worse than an empty editor. Reported as its own outcome so "the
    /// baseline is empty" is never mistaken for a read that returned the user's settings.
    /// </summary>
    [Fact]
    public async Task ReloadingWithNothingConnected_EmptiesTheBaseline()
    {
        var (coordinator, daemon, _) = Make();
        await coordinator.ApplyAndSaveAsync(WithPolicyBait());
        Assert.NotNull(coordinator.CurrentSettings);
        daemon.Settings = null;

        var outcome = await coordinator.ReloadFromDaemonAsync();

        Assert.Equal(SettingsReloadStatus.Disconnected, outcome.Status);
        Assert.Null(coordinator.CurrentSettings);
        Assert.Null(outcome.Adopted);
    }

    /// <summary>
    /// A read held open while an apply completes describes a moment that has passed, and is discarded.
    ///
    /// The epoch has to be observed BEFORE the read, which is the whole reason this is one call: a host
    /// that observed it after starting the read would see the value the apply had already moved, and
    /// adopt the stale answer. Not a display glitch -- the next edit builds on that baseline, so the
    /// reverted value goes back to the daemon.
    /// </summary>
    [Fact]
    public async Task AReadOvertakenByAnApply_IsDiscarded()
    {
        var (coordinator, daemon, _) = Make();
        var held = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetSettingsHandler = () => held.Task;

        var reload = coordinator.ReloadFromDaemonAsync();          // in flight, answer withheld
        await coordinator.ApplyAndSaveAsync(SettingsFor("Newer"));  // completes while it waits
        held.SetResult(SettingsFor("Older"));                       // the stale answer arrives

        var outcome = await reload;

        Assert.Equal(SettingsReloadStatus.Overtaken, outcome.Status);
        Assert.False(outcome.ChangedTheBaseline);
        Assert.Equal("Newer", coordinator.CurrentSettings!.Profiles[0].Tablet);
    }

    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };
}
