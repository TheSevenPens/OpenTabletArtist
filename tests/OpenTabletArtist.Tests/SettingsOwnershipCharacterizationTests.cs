using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// <b>Characterization tests. These assert what the code does today, not what it should do.</b>
///
/// Do not "fix" a failure here by changing the assertion to match new behaviour. A failure means the
/// ownership contract changed, which is the entire thing these exist to make visible.
///
/// <para>
/// Written for #807 (the OtdInterop extraction), Phase 0. That plan requires a contract stating whether a
/// settings object handed to the coordinator is owned by the caller or by the coordinator. Today there is
/// no such contract, and the actual behaviour is <b>not uniform across the three mutating paths</b>:
/// </para>
///
/// <list type="bullet">
/// <item>Policy (<c>ProfileFilterMaintenance</c>, <c>ProfileSanitizer</c>) runs on the <em>caller's</em>
/// object, in place, before any copy is taken. So applying settings silently edits the object the app is
/// still holding — and the tablet editor holds exactly such objects.</item>
/// <item><c>ApplyAndSaveAsync</c> then snapshots, so the daemon receives a copy (#774).</item>
/// <item><c>ApplyLiveOnlyAsync</c> and <c>ApplyEphemeralAsync</c> do <b>not</b> snapshot, so the daemon
/// receives the caller's own instance, which the app can keep mutating mid-flight.</item>
/// </list>
///
/// <para>
/// Whichever way #807 writes the contract, it changes at least one of these. Snapshot-before-sanitize
/// stops the app's copy being repaired; snapshotting the live-only paths changes what reaches the daemon
/// when the caller mutates during an apply. Both are behaviour changes that would otherwise ride along
/// inside a "mechanical move" — so they are pinned here first, and any change to them has to be a
/// deliberate, separately reviewable decision.
/// </para>
/// </summary>
public class SettingsOwnershipCharacterizationTests
{
    /// <summary>A third-party filter OTA does not own. On a daemon OTA owns, policy disables it.</summary>
    private const string ThirdPartyFilter = "OpenTabletDriver.Filters.Noise.NoiseReduction";

    private sealed class NoopStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }
        public bool TrySave(Settings settings, string path) => true;
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    /// <summary>Settings carrying something policy will visibly change: an enabled third-party filter,
    /// and a profile with no Absolute-mode areas for <c>ProfileSanitizer</c> to repair.</summary>
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

    /// <summary>Owned daemon, because the unapproved-filter policy only runs on one (#465/#742).</summary>
    private static (SettingsCoordinator coordinator, FakeDaemonTransport daemon) Make()
    {
        var daemon = new FakeDaemonTransport();
        var coordinator = new SettingsCoordinator(
            daemon, new NoopStore(),
            settingsPath: () => "A/settings.json",
            isOwnedDaemon: () => true,
            onSaveState: _ => { });
        return (coordinator, daemon);
    }

    // --- Does policy edit the object the app is still holding? --------------------------------

    [Fact]
    public async Task ApplyAndSave_EditsTheCallersOwnObject()
    {
        var (coordinator, _) = Make();
        var mine = WithPolicyBait();

        await coordinator.ApplyAndSaveAsync(mine);

        // Today: yes. The app's object comes back with the filter disabled and the areas repaired,
        // without the app asking for either.
        Assert.False(ThirdPartyFilterEnabled(mine));
        Assert.NotNull(mine.Profiles[0].AbsoluteModeSettings);
    }

    [Fact]
    public async Task ApplyLiveOnly_EditsTheCallersOwnObject()
    {
        var (coordinator, _) = Make();
        var mine = WithPolicyBait();

        await coordinator.ApplyLiveOnlyAsync(mine);

        Assert.False(ThirdPartyFilterEnabled(mine));
    }

    [Fact]
    public async Task ApplyEphemeral_EditsTheCallersOwnObject()
    {
        var (coordinator, _) = Make();
        var mine = WithPolicyBait();

        await coordinator.ApplyEphemeralAsync(mine);

        Assert.False(ThirdPartyFilterEnabled(mine));
    }

    // --- Does the daemon receive the caller's instance, or a copy? ----------------------------

    /// <summary>
    /// The one path that isolates the caller. #774 introduced the snapshot so an edit landing during the
    /// RPC cannot change what is sent and persisted.
    /// </summary>
    [Fact]
    public async Task ApplyAndSave_SendsACopy_NotTheCallersInstance()
    {
        var (coordinator, daemon) = Make();
        var mine = WithPolicyBait();

        await coordinator.ApplyAndSaveAsync(mine);

        Assert.Single(daemon.Applied);
        Assert.NotSame(mine, daemon.Applied[0]);
    }

    /// <summary>
    /// And the two that do not. The caller's live object goes to the transport, so anything the app does
    /// to it while the RPC is in flight is sent. These are the per-app switching paths (#167/#320), where
    /// the caller is usually handing over a freshly-built snapshot — which is why it has not bitten.
    /// </summary>
    [Fact]
    public async Task ApplyLiveOnly_SendsTheCallersOwnInstance()
    {
        var (coordinator, daemon) = Make();
        var mine = WithPolicyBait();

        await coordinator.ApplyLiveOnlyAsync(mine);

        Assert.Single(daemon.Applied);
        Assert.Same(mine, daemon.Applied[0]);
    }

    [Fact]
    public async Task ApplyEphemeral_SendsTheCallersOwnInstance()
    {
        var (coordinator, daemon) = Make();
        var mine = WithPolicyBait();

        await coordinator.ApplyEphemeralAsync(mine);

        Assert.Single(daemon.Applied);
        Assert.Same(mine, daemon.Applied[0]);
    }

    // --- The consequence, stated as its own test ---------------------------------------------

    /// <summary>
    /// Why the difference matters, rather than being trivia about object identity.
    ///
    /// On the live-only path the app can mutate the settings after the call has begun and before the
    /// daemon reads them. <c>ApplyAndSaveAsync</c> is immune because of its snapshot; this one is not.
    /// Pinned so that if #807 makes the paths uniform, the change shows up here as an intended edit.
    /// </summary>
    [Fact]
    public async Task OnTheLiveOnlyPath_AnEditDuringTheApply_ReachesTheDaemon()
    {
        var (coordinator, daemon) = Make();
        var mine = WithPolicyBait();

        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = sent =>
        {
            daemon.Applied.Add(sent);
            return released.Task;
        };

        var apply = coordinator.ApplyLiveOnlyAsync(mine);
        mine.LockUsableAreaDisplay = true;   // the app edits its object mid-flight
        released.SetResult(true);
        await apply;

        // The daemon holds the caller's instance, so it sees the later edit too.
        Assert.True(daemon.Applied[0].LockUsableAreaDisplay);
    }
}
