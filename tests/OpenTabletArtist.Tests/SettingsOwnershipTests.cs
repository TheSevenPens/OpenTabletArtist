using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

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

    /// <summary>Records what it was handed, and keeps it — the thing a policy is allowed to do.</summary>
    private sealed class HoardingPolicy : IOtdSettingsPolicy
    {
        public List<Settings> SeenWorkingCopies { get; } = new();
        public Settings? Last { get; private set; }

        public void Apply(Settings workingCopy, SettingsPolicyContext context)
        {
            SeenWorkingCopies.Add(workingCopy);
            Last = workingCopy;
            OtaSettingsPolicy.Instance.Apply(workingCopy, context);
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
            settingsPath: () => "A/settings.json",
            isOwnedDaemon: () => true,
            onSaveState: _ => { },
            log: NullOtdLog.Instance,
            policy: policy);
        return (coordinator, daemon, policy);
    }

    public static TheoryData<string> EveryMutatingPath => new() { "save", "live", "ephemeral" };

    private static Task Apply(SettingsCoordinator c, string path, Settings s) => path switch
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
        Assert.Same(daemon.Applied[0], prepared.Settings);
        Assert.False(prepared.Stamp.IsNone);
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
}
