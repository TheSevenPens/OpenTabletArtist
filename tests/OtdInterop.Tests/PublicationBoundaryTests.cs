using System;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// What this session publishes, and when — the boundary #832 moved.
/// </summary>
///
/// <remarks>
/// <para>
/// An apply used to publish its revision <em>before</em> the RPC, so between the send and the answer this
/// session's authoritative baseline was something no daemon had agreed to. If the operation was then
/// superseded it stayed that way until a reload, and a caller reading it in between built its next edit
/// on top of it.
/// </para>
/// <para>
/// The rule now: <b>a revision exists once the daemon has accepted it, and not before.</b> The separate
/// question — whether a read in flight can still be trusted — is the observation epoch's, and it is
/// answered at acceptance, which is why the two are different counters.
/// </para>
/// </remarks>
public class PublicationBoundaryTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// While an apply is in flight, the baseline is still the previous one.
    /// </summary>
    /// <remarks>
    /// The authority boundary itself. Everything else here is about what happens at the edges of it.
    /// </remarks>
    [Fact]
    public async Task WhileAnApplyIsInFlight_TheBaselineIsStillThePreviousOne()
    {
        var (settings, daemon, _) = Make();

        var before = settings.GetCurrent()!;

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        var apply = settings.ApplyAndSaveAsync(Tablet("Candidate"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var during = settings.GetCurrent()!;
        Assert.Equal("Baseline", during.Settings.Profiles[0].Tablet);
        Assert.Equal(before.Stamp, during.Stamp);

        held.SetResult(true);
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, (await apply).Status);

        var after = settings.GetCurrent()!;
        Assert.Equal("Candidate", after.Settings.Profiles[0].Tablet);
        Assert.NotEqual(before.Stamp, after.Stamp);
    }

    /// <summary>
    /// A daemon that refuses the write publishes nothing, and hands back nothing to adopt.
    /// </summary>
    /// <remarks>
    /// The disconnected result used to carry the attempted settings, stamped with the revision it had
    /// already published. With publication moved there is no revision to stamp them with, and stamping
    /// them with the current one would be worse than saying nothing: that stamp identifies different,
    /// already-published content.
    /// </remarks>
    [Fact]
    public async Task AnApplyTheDaemonRefuses_PublishesNothingAndOffersNothing()
    {
        var (settings, daemon, _) = Make();

        var before = settings.GetCurrent()!;
        daemon.SetSettingsHandler = _ => Task.FromResult(false);

        var outcome = await settings.ApplyAndSaveAsync(Tablet("Refused"));

        Assert.Equal(SettingsApplyStatus.Disconnected, outcome.Status);
        Assert.Null(outcome.Prepared);

        var after = settings.GetCurrent()!;
        Assert.Equal("Baseline", after.Settings.Profiles[0].Tablet);
        Assert.Equal(before.Stamp, after.Stamp);
    }

    /// <summary>
    /// An apply whose call throws publishes nothing either.
    /// </summary>
    [Fact]
    public async Task AnApplyThatThrows_PublishesNothing()
    {
        var (settings, daemon, _) = Make();

        var before = settings.GetCurrent()!;
        daemon.SetSettingsHandler = _ => throw new InvalidOperationException("the pipe went away");

        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.ApplyAndSaveAsync(Tablet("Thrown")));

        var after = settings.GetCurrent()!;
        Assert.Equal("Baseline", after.Settings.Profiles[0].Tablet);
        Assert.Equal(before.Stamp, after.Stamp);
    }

    /// <summary>
    /// An accepted write still publishes when the disk save fails.
    /// </summary>
    /// <remarks>
    /// Publication is ordered before the write for this reason: the daemon is running these settings, so
    /// they are the baseline whatever happened to the file. A persistence failure is its own outcome and
    /// its own retry, and must not revert what was accepted.
    /// </remarks>
    [Fact]
    public async Task AnAcceptedWriteWhoseSaveFails_StillPublishes()
    {
        var (settings, _, store) = Make();

        store.SaveSucceeds = false;

        var outcome = await settings.ApplyAndSaveAsync(Tablet("Live but unsaved"));

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, outcome.Status);
        Assert.NotNull(outcome.Prepared);
        Assert.Equal("Live but unsaved", settings.GetCurrent()!.Settings.Profiles[0].Tablet);
    }

    /// <summary>
    /// A read begun before an acceptance cannot replace what that acceptance published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invariant publication was moved under, and the interleaving an operation-start epoch bump
    /// would not have covered on its own: the read begins <em>after</em> the apply has been sent and
    /// lands after it has been accepted.
    /// </para>
    /// <para>
    /// The old content is captured and handed back explicitly. A fake that read the daemon's current
    /// settings when released would return the newly accepted ones instead, and the test would pass
    /// whatever the code did.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AReadBegunBeforeAnAcceptance_CannotReplaceWhatItPublished()
    {
        var (settings, daemon, _) = Make();

        var applyHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return applyHeld.Task;
        };

        var apply = settings.ApplyAndSaveAsync(Tablet("Accepted"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        // What the daemon holds right now, decided now rather than when the read is released.
        var oldContent = Tablet("What the daemon had before");
        var readHeld = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetSettingsHandler = () =>
        {
            reading.TrySetResult();
            return readHeld.Task;
        };

        var reload = settings.ReloadFromDaemonAsync();
        await reading.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        applyHeld.SetResult(true);
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, (await apply).Status);

        var published = settings.GetCurrent()!;

        readHeld.SetResult(oldContent);
        Assert.Equal(SettingsReloadStatus.Overtaken,
            (await reload.WaitAsync(Bound, TestContext.Current.CancellationToken)).Status);

        var after = settings.GetCurrent()!;
        Assert.Equal("Accepted", after.Settings.Profiles[0].Tablet);
        Assert.Equal(published.Stamp, after.Stamp);
    }

    /// <summary>
    /// The comparison baseline is the accepted apply's too, not the overtaken read's.
    /// </summary>
    /// <remarks>
    /// Separate from the published settings on purpose: they are different fields with different jobs,
    /// and the no-op guard reads this one. A read adopted over it would make the next identical apply
    /// look like a change, or a real change look like a no-op.
    /// </remarks>
    [Fact]
    public async Task AnOvertakenRead_DoesNotBecomeTheComparisonBaselineEither()
    {
        var (settings, daemon, _) = Make();

        var applyHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return applyHeld.Task;
        };

        var apply = settings.ApplyAndSaveAsync(Tablet("Accepted"));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var oldContent = Tablet("What the daemon had before");
        var readHeld = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetSettingsHandler = () =>
        {
            reading.TrySetResult();
            return readHeld.Task;
        };

        var reload = settings.ReloadFromDaemonAsync();
        await reading.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        applyHeld.SetResult(true);
        await apply;

        readHeld.SetResult(oldContent);
        await reload.WaitAsync(Bound, TestContext.Current.CancellationToken);

        // Re-applying exactly what the daemon accepted is a no-op, which it could only be if the
        // comparison baseline is still the accepted revision.
        Assert.Equal(SettingsApplyStatus.NoChange,
            (await settings.ApplyAndSaveAsync(Tablet("Accepted"))).Status);
    }

    /// <summary>
    /// A failed apply leaves a running override running.
    /// </summary>
    /// <remarks>
    /// The override used to be cleared next to the publication, before the send — so an apply the daemon
    /// refused ended an override the daemon was still running. A reload could then read that transient
    /// snapshot and adopt it as the editor's default, which is the defect #737 exists to prevent, reached
    /// by a different route.
    /// </remarks>
    [Fact]
    public async Task AFailedApplyDuringAnOverride_LeavesTheOverrideRunning()
    {
        var (settings, daemon, _) = Make();

        Assert.Equal(SettingsApplyStatus.AppliedLive,
            (await settings.ApplyEphemeralAsync(Tablet("Snapshot"))).Status);
        Assert.True(settings.HasEphemeralOverride);

        daemon.SetSettingsHandler = _ => Task.FromResult(false);
        Assert.Equal(SettingsApplyStatus.Disconnected,
            (await settings.ApplyAndSaveAsync(Tablet("Refused"))).Status);

        Assert.True(settings.HasEphemeralOverride);

        // And the reload still declines to read the daemon, which is what the flag is for.
        Assert.Equal(SettingsReloadStatus.SkippedOverride,
            (await settings.ReloadFromDaemonAsync()).Status);
    }

    /// <summary>
    /// An acceptance that publishes nothing still invalidates a read in flight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The acceptance bump on its own, with the publication bump out of the way. Every other test here
    /// would pass with the acceptance bump deleted, because an apply that publishes also bumps — so they
    /// establish the epoch moves, not <em>where</em> it moves. An ephemeral apply accepts and publishes
    /// nothing, which is the only shape that tells those apart.
    /// </para>
    /// <para>
    /// This was pointed out in review before the mutation confirmed it. It is the reason the two counters
    /// are separate: one answers "which published settings is this", the other "could a read I started
    /// still be trusted", and the daemon taking a transient snapshot moves only the second.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAcceptanceThatPublishesNothing_StillInvalidatesAReadInFlight()
    {
        var (settings, daemon, _) = Make();

        var oldContent = Tablet("What the daemon had before");
        var readHeld = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetSettingsHandler = () =>
        {
            reading.TrySetResult();
            return readHeld.Task;
        };

        var reload = settings.ReloadFromDaemonAsync();
        await reading.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        // Accepted by the daemon, and deliberately publishing no revision of its own.
        Assert.Equal(SettingsApplyStatus.AppliedLive,
            (await settings.ApplyEphemeralAsync(Tablet("Snapshot"))).Status);

        readHeld.SetResult(oldContent);

        Assert.Equal(SettingsReloadStatus.Overtaken,
            (await reload.WaitAsync(Bound, TestContext.Current.CancellationToken)).Status);
    }

    // --- harness --------------------------------------------------------------------------------

    private static (IOtdSettingsSession, FakeDaemonTransport, RecordingStore) Make()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = Tablet("Baseline") };
        var store = new RecordingStore();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            NoPolicy.Instance, new FakeProcessLocator());
        var settings = session.OpenSettings(() => true, _ => { });

        daemon.Reconnect();
        settings.ReloadFromDaemonAsync().GetAwaiter().GetResult();
        return (settings, daemon, store);
    }

    private sealed class RecordingStore : ISettingsFileStore
    {
        public bool SaveSucceeds { get; set; } = true;

        public void Save(Settings settings, string path) { }

        public bool TrySave(Settings settings, string path) => SaveSucceeds;

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = null;
            return false;
        }
    }

    private static Settings Tablet(string name) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = name } } };
}
