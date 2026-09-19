using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Work does not survive the connection it was authored against (#828).
///
/// The daemon-switch protections built so far all depend on somebody noticing: the host identifies the
/// connected executable and tells the session, and the session then refuses work from before. That leaves
/// a window nothing covered. The real client assigns its RPC channel and only afterwards raises
/// <c>Connected</c> — so a send already reaches the new daemon before any host handler has run, and
/// before anything could have been told.
///
/// So an operation binds to the channel it was admitted on, and that binding needs nobody's cooperation:
/// the channel number is already correct when the first operation after a reconnect is admitted.
/// </summary>
public class DaemonReconnectTests
{
    /// <summary>Remembers where each write went, which is the question a daemon switch raises.</summary>
    private sealed class PathRecordingStore : ISettingsFileStore
    {
        public List<string> Wrote { get; } = new();
        public void Save(Settings s, string path) => Wrote.Add(path);
        public bool TrySave(Settings s, string path) { Wrote.Add(path); return true; }
        public bool TryLoad(string path, out Settings? s) { s = null; return false; }
    }

    /// <summary>
    /// An apply that spans a reconnect writes nothing.
    ///
    /// This is the case #828 asks for: hold the RPC, replace the channel underneath it, release. Before
    /// the binding, the completion carried on and wrote the revision to the settings path captured before
    /// the send — the OLD daemon's file. One daemon's settings into another's, which is #787 and #803
    /// reached by a route neither covered.
    /// </summary>
    [Fact]
    public async Task AnApplyThatSpansAReconnect_WritesNothing()
    {
        var (_, settings, daemon, store) = Make();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => held.Task;

        var apply = settings.ApplyAndSaveAsync(Tablet("Edited"));
        daemon.ReconnectSilently();          // the channel is replaced; nothing announces it
        held.SetResult(true);

        Assert.Equal(SettingsApplyStatus.Superseded, (await apply).Status);
        Assert.Empty(store.Wrote);
    }

    /// <summary>
    /// What a superseded apply DOES leave behind: the revision it published before sending.
    ///
    /// Pinned rather than asserted away, because it is a real limitation and I would otherwise have
    /// written a test claiming the opposite. An apply publishes its revision <em>before</em> the RPC, on
    /// purpose — a read starting between publish and acceptance would otherwise carry a version that
    /// looks current while describing state from before the change. When the operation is then
    /// superseded, that published revision describes settings the new daemon never accepted.
    ///
    /// <b>Not bounded as well as I first claimed.</b> I wrote that the following reload repairs it, and
    /// Codex pointed out that is not sufficient: a caller reading the baseline between the two builds its
    /// next edit on a revision no daemon has, which is the contamination #814 already reproduced. I also
    /// had the premise wrong — #818 required read invalidation, not publishing before acceptance, and the
    /// epoch/revision split exists so those can be decided separately.
    ///
    /// So this is a characterization of a known gap, not settled behaviour. #832 is the fix, and this
    /// test should invert when it lands.
    /// </summary>
    [Fact]
    public async Task ASupersededApply_LeavesItsPublishedRevisionForTheReloadToCorrect()
    {
        var (_, settings, daemon, store) = Make();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => held.Task;
        var apply = settings.ApplyAndSaveAsync(Tablet("Edited"));
        daemon.ReconnectSilently();
        held.SetResult(true);
        await apply;

        Assert.Equal("Edited", settings.GetCurrent()!.Settings.Profiles[0].Tablet);
        Assert.Empty(store.Wrote);

        // And the reload is what corrects it.
        daemon.Settings = Tablet("What the new daemon holds");
        await settings.ReloadFromDaemonAsync();
        Assert.Equal("What the new daemon holds", settings.GetCurrent()!.Settings.Profiles[0].Tablet);
    }

    /// <summary>
    /// Work queued behind a held operation is refused too, not merely the one in flight.
    ///
    /// The requirement is explicit about this: cover operations already queued, not only work started by
    /// whatever notices the reconnect. A queued apply reaches the front <em>after</em> the channel moved,
    /// so it would otherwise be the first thing to write the old daemon's settings to the new one.
    /// </summary>
    [Fact]
    public async Task WorkQueuedBeforeAReconnect_IsRefusedWhenItReachesTheFront()
    {
        var (_, settings, daemon, store) = Make();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => held.Task;

        var first = settings.ApplyAndSaveAsync(Tablet("First"));
        var queued = settings.ApplyAndSaveAsync(Tablet("Queued"));   // waiting on the gate
        daemon.ReconnectSilently();
        held.SetResult(true);

        Assert.Equal(SettingsApplyStatus.Superseded, (await first).Status);
        Assert.Equal(SettingsApplyStatus.Superseded, (await queued).Status);
        Assert.Empty(store.Wrote);
    }

    /// <summary>
    /// A reconnect to the SAME executable still invalidates work that spans it, and still keeps the
    /// settings.
    ///
    /// The two questions are deliberately separate. Whether an operation may act is about the channel:
    /// the one it was sent on is gone, and it cannot know what the new one did with it. Whether the
    /// user's unsaved edit survives is about the executable, and #823 settled that a daemon at the same
    /// path is the same daemon. Collapsing them would make every ordinary reconnect discard an edit.
    /// </summary>
    [Fact]
    public async Task AReconnectToTheSameDaemon_InvalidatesWorkButKeepsTheSettings()
    {
        var (session, settings, daemon, _) = Make();
        await settings.ApplyAndSaveAsync(Tablet("Saved"));

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => held.Task;
        var apply = settings.ApplyAndSaveAsync(Tablet("Spans the gap"));
        daemon.ReconnectSilently();
        held.SetResult(true);

        Assert.Equal(SettingsApplyStatus.Superseded, (await apply).Status);

        // Same executable, so nothing about the user's settings is discarded and no notice is raised.
        var change = session.NoteConnectedDaemon();
        Assert.False(change.Changed);
        Assert.False(change.DiscardedUnsavedChange);
    }

    /// <summary>After the channel settles, ordinary work proceeds. The gate is not a one-way door.</summary>
    [Fact]
    public async Task AfterAReconnect_NewWorkIsAdmittedNormally()
    {
        var (_, settings, daemon, store) = Make();
        daemon.ReconnectSilently();

        var outcome = await settings.ApplyAndSaveAsync(Tablet("After"));

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.Single(store.Wrote);
    }

    /// <summary>Replaces the channel from inside the operation, between admission and the send.</summary>
    private sealed class SwitchDuringPreparation(FakeDaemonTransport daemon) : IOtdSettingsPolicy
    {
        public void Apply(Settings settings, SettingsPolicyContext context) => daemon.ReconnectSilently();
    }

    /// <summary>
    /// Obsolete work never enters the replacement transport — the send is BOUND, not merely checked.
    ///
    /// From Codex's review of the first attempt, which is the reason this test exists in this shape. That
    /// attempt compared a channel number at the start of the operation and again afterwards, and called
    /// that binding. It is not: an operation applies policy, takes snapshots, runs format checks and calls
    /// back into the host between those two points, and the channel can be replaced anywhere in there. The
    /// check passed, the send went to the replacement, and the later check reported Superseded — after the
    /// settings had already reached the wrong daemon.
    ///
    /// The policy hook is only a way to land the replacement in that interval deterministically; it
    /// touches no session state. What is asserted is that nothing was sent at all, which a check before
    /// the send cannot deliver and a hold on the channel can.
    /// </summary>
    [Fact]
    public async Task ObsoleteWorkNeverEntersTheReplacementTransport()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var store = new PathRecordingStore();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            new SwitchDuringPreparation(daemon), locator);
        var settings = session.OpenSettings(() => "A/settings.json", () => true, _ => { });
        await settings.ReloadFromDaemonAsync();
        session.NoteConnectedDaemon();
        daemon.Applied.Clear();

        var outcome = await settings.ApplyAndSaveAsync(Tablet("A's edit"));

        Assert.Equal(SettingsApplyStatus.Superseded, outcome.Status);
        Assert.Empty(store.Wrote);
        Assert.Empty(daemon.Applied);          // the part a pre-send check cannot give you
    }

    /// <summary>
    /// A send that fails because its channel was replaced says so, rather than "not connected".
    ///
    /// Both are "it was not sent", and they are different facts. Telling a user with a working daemon
    /// that they have no connection is wrong in a way they would act on — by going to look at a daemon
    /// that is fine.
    /// </summary>
    [Fact]
    public async Task ASendBoundToAReplacedChannel_IsSupersededNotDisconnected()
    {
        var (_, settings, daemon, _) = Make();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => held.Task;
        var live = settings.ApplyLiveOnlyAsync(Tablet("Live"));
        daemon.ReconnectSilently();
        held.SetResult(true);

        Assert.Equal(SettingsApplyStatus.Superseded, (await live).Status);
    }

    private static (OtdSession, IOtdSettingsSession, FakeDaemonTransport, PathRecordingStore) Make()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var store = new PathRecordingStore();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, locator);
        var settings = session.OpenSettings(() => "A/settings.json", () => true, _ => { });
        settings.ReloadFromDaemonAsync().GetAwaiter().GetResult();
        session.NoteConnectedDaemon();
        return (session, settings, daemon, store);
    }

    private static Settings Tablet(string n) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = n } } };
}
