using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OtdInterop.Tests;

public class SettingsSessionTests
{
    internal static Settings Document(float width = 100) => new()
    {
        Profiles = new ProfileCollection
        {
            new Profile
            {
                Tablet = "T",
                AbsoluteModeSettings = new AbsoluteModeSettings
                {
                    Tablet = new AreaSettings { Width = width, Height = 50, X = 50, Y = 25 },
                    Display = new AreaSettings { Width = 1920, Height = 1080, X = 960, Y = 540 }
                }
            }
        }
    };

    /// <summary>
    /// Closing while a write is out does not wait for it, and nothing is saved behind it (#919).
    /// </summary>
    /// <remarks>
    /// The existing close test holds the <em>preflight read</em>, which is the easy half: nothing has
    /// been sent, so refusing is free. This holds the write itself, with a Save queued behind it — the
    /// shape where a close could plausibly wait forever, or let the queued Save run against a session
    /// that is going away.
    /// </remarks>
    [Fact]
    public async Task ClosingDuringAWriteIsBoundedAndTheQueuedSaveNeverRuns()
    {
        var (session, daemon, store) = await Open(TimeSpan.FromMilliseconds(50));
        var inFlight = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => inFlight.Task;

        var apply = session.ApplyAsync(Document(120));
        var queued = session.SaveAsync();

        Assert.True(await session.CloseAsync(TimeSpan.FromSeconds(2)), "close should not wait on the daemon");
        Assert.False((await apply).IsLive);
        Assert.Equal(SettingsSaveStatus.Disconnected, (await queued).Status);
        Assert.Equal(0, store.Attempts);

        // The write lands afterwards, as it is entitled to. It must change nothing here.
        inFlight.SetResult(true);
        Assert.Equal(0, store.Attempts);
        Assert.Equal(SettingsReloadStatus.Disconnected, (await session.ReloadAsync()).Status);
    }

    /// <summary>
    /// A close whose budget expires against genuinely stuck work says so rather than claiming success.
    /// </summary>
    /// <remarks>
    /// <c>QuitSequence</c> bounds its own stop step, but it relies on this answer being truthful about
    /// whether local work actually settled. A close that returned true regardless would make that
    /// bounding meaningless.
    /// </remarks>
    [Fact]
    public async Task ACloseBudgetThatExpires_ReportsThatWorkDidNotSettle()
    {
        var (session, _, store) = await Open();
        using var blocked = new ManualResetEventSlim(false);
        store.BlockInsideSave = blocked;

        // Started off the test's own synchronization context. The block happens inside a synchronous file
        // write, and xUnit runs continuations on a context with limited concurrency — so blocking there
        // directly stops this test's awaits from ever resuming, which is a deadlock in the test rather
        // than a finding about the code.
        var save = Task.Run(() => session.SaveAsync());
        await WaitFor(() => store.Attempts > 0, "the save to reach the file");

        Assert.False(await session.CloseAsync(TimeSpan.FromMilliseconds(200)),
            "a close that could not settle its own work must not report success");

        blocked.Set();
        await save;
    }

    private static async Task WaitFor(Func<bool> until, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!until())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {what}.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// A write finishing after its connection was replaced cannot reach the replacement (#919).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The uncertain-write test covers a late completion against the session that issued it. This is the
    /// other direction: the reconnect has happened, a new session owns the connection, and the old
    /// session must not be able to act on the completion.
    /// </para>
    /// <para>
    /// What the replacement sees is the more interesting half, and it is not "nothing". The late write
    /// <em>does</em> reach the daemon — <c>SetSettings</c> takes no cancellation token, so a fresh pipe
    /// is not a barrier against work already accepted. The daemon really is holding different settings
    /// afterwards, and the replacement treats that as what it is: an outside change, which pauses it
    /// rather than being quietly overwritten. That is the protection doing its job, not an accident.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AWriteCompletingAfterAReconnect_CannotWriteToTheReplacementSession()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        daemon.Reconnect();
        var store = new MemorySettingsFileStore { Saved = Document() };

        var old = new SettingsCoordinator(daemon, store, "settings.json", TimeSpan.FromMilliseconds(50));
        Assert.Equal(SettingsReloadStatus.Adopted, (await old.ReloadAsync()).Status);

        var inFlight = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => inFlight.Task;
        var orphaned = old.ApplyAsync(Document(180));
        Assert.False((await orphaned).IsLive, "the write never returned, so it cannot be live");

        // The connection is replaced and a fresh session takes over, as OtdSession does on reconnect.
        daemon.SetSettingsHandler = null;
        daemon.Reconnect();
        var replacement = new SettingsCoordinator(daemon, store, "settings.json");
        Assert.Equal(SettingsReloadStatus.Adopted, (await replacement.ReloadAsync()).Status);

        // The old write finally completes. It belongs to a connection that is gone — but it still lands
        // in the daemon, because nothing on this side can stop work the daemon already accepted.
        inFlight.SetResult(true);
        // The daemon records the attempt before awaiting, and only stores the settings once the write
        // completes — so waiting on Applied would wait for something that already happened.
        await WaitFor(() => SettingsCodec.Same(daemon.Settings!, Document(180)),
                      "the orphaned write to land in the daemon");

        // The old session can do nothing with that, which is the guarantee it owes.
        var before = store.Attempts;
        Assert.Equal(SettingsReloadStatus.Disconnected, (await old.ReloadAsync()).Status);
        Assert.Equal(SettingsSaveStatus.Disconnected, (await old.SaveAsync()).Status);
        Assert.Equal(before, store.Attempts);

        // And the replacement pauses on it rather than writing over it: from where it stands, this is an
        // outside change like any other, and it is right about that.
        Assert.Equal(SettingsApplyStatus.ChangedElsewhere,
            (await replacement.ApplyAsync(Document(140))).Status);
        Assert.True(replacement.IsPaused);

        // Reload is the way out, as everywhere else.
        Assert.Equal(SettingsReloadStatus.Adopted, (await replacement.ReloadAsync()).Status);
        Assert.True((await replacement.ApplyAsync(Document(140))).IsLive);

        replacement.Dispose();
        old.Dispose();
    }

    private static async Task<(SettingsCoordinator Session, FakeDaemonTransport Daemon, MemorySettingsFileStore Store)>
        Open(TimeSpan? timeout = null)
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        daemon.Reconnect();
        var store = new MemorySettingsFileStore { Saved = Document() };
        var session = new SettingsCoordinator(daemon, store, "settings.json", timeout);
        Assert.Equal(SettingsReloadStatus.Adopted, (await session.ReloadAsync()).Status);
        return (session, daemon, store);
    }

    [Fact]
    public async Task SaveRepairsInvalidAreasAndConfirmsThemBeforeWriting()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        daemon.Settings!.Profiles[0].AbsoluteModeSettings = null!;
        await session.ReloadAsync();
        Assert.True((await session.SaveAsync()).IsSaved);
        Assert.NotNull(daemon.Settings.Profiles[0].AbsoluteModeSettings.Display);
        Assert.NotNull(store.Saved!.Profiles[0].AbsoluteModeSettings.Tablet);
    }

    [Fact]
    public async Task ExplicitRestoreAdoptsTheCurrentFileAsItsSavedBaseline()
    {
        var (session, _, store) = await Open();
        using var lifetime = session;
        store.Saved = Document(160);
        Assert.True((await session.RestoreSavedAsync()).IsRestored);
        Assert.False(session.HasUnsavedChanges);
        Assert.Equal(160, session.GetCurrent()!.Settings.Profiles[0].AbsoluteModeSettings.Tablet.Width);
        Assert.Equal(0, store.Attempts);
    }

    [Fact]
    public async Task RestoreWithoutAReadableSourceDoesNotApplyOrPause()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        store.Saved = null;
        Assert.Equal(SettingsRestoreStatus.SourceUnavailable, (await session.RestoreSavedAsync()).Status);
        Assert.Empty(daemon.Applied);
        Assert.False(session.IsPaused);
    }

    [Fact]
    public async Task ApplyIsLiveOnlyAndExplicitSavePersistsConfirmedValues()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        Assert.False(session.HasUnsavedChanges);
        var outcome = await session.ApplyAsync(Document(120));
        Assert.True(outcome.IsLive);
        Assert.Equal(120, daemon.Settings!.Profiles[0].AbsoluteModeSettings.Tablet.Width);
        Assert.Equal(0, store.Attempts);
        Assert.True(session.HasUnsavedChanges);
        Assert.True((await session.SaveAsync()).IsSaved);
        Assert.Equal(1, store.Attempts);
        Assert.Equal(120, store.Saved!.Profiles[0].AbsoluteModeSettings.Tablet.Width);
        Assert.False(session.HasUnsavedChanges);
    }

    [Fact]
    public async Task DocumentSnapshotsAreDetachedAndAdmissionCopiesQueuedInput()
    {
        var (session, daemon, _) = await Open();
        using var lifetime = session;
        session.GetCurrent()!.Settings.Profiles[0].Tablet = "mutated";
        Assert.Equal("T", session.GetCurrent()!.Settings.Profiles[0].Tablet);
        var release = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => release.Task;
        var first = session.ApplyAsync(Document(120));
        var input = Document(140);
        var second = session.ApplyAsync(input);
        input.Profiles[0].Tablet = "mutated";
        release.SetResult(true);
        Assert.True((await first).IsLive);
        Assert.True((await second).IsLive);
        Assert.Equal("T", daemon.Settings!.Profiles[0].Tablet);
        Assert.Equal(140, daemon.Settings.Profiles[0].AbsoluteModeSettings.Tablet.Width);
    }

    [Fact]
    public async Task SaveWaitsForAllAdmittedApplies()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        var release = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => release.Task;
        var apply = session.ApplyAsync(Document(120));
        var save = session.SaveAsync();
        Assert.False(save.IsCompleted);
        Assert.Equal(0, store.Attempts);
        release.SetResult(true);
        await apply;
        Assert.True((await save).IsSaved);
        Assert.Equal(120, store.Saved!.Profiles[0].AbsoluteModeSettings.Tablet.Width);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OutsideChangesPauseUntilExplicitReload(bool inDaemon)
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        if (inDaemon) daemon.Settings = Document(160);
        else store.Saved = Document(160);
        Assert.Equal(SettingsSaveStatus.Paused, (await session.SaveAsync()).Status);
        Assert.True(session.IsPaused);
        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, (await session.ApplyAsync(Document(120))).Status);
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Attempts);
        Assert.Equal(SettingsReloadStatus.Adopted, (await session.ReloadAsync()).Status);
        Assert.False(session.IsPaused);
        Assert.True((await session.ApplyAsync(Document(120))).IsLive);
    }

    [Fact]
    public async Task RefreshObservesWithoutDiscardingTheAppDocument()
    {
        var (session, daemon, _) = await Open();
        using var lifetime = session;
        daemon.Settings = Document(160);
        Assert.Equal(SettingsReloadStatus.Paused, (await session.RefreshAsync()).Status);
        Assert.Equal(100, session.GetCurrent()!.Settings.Profiles[0].AbsoluteModeSettings.Tablet.Width);
        Assert.Equal(160, (await session.ReloadAsync()).Adopted!.Settings.Profiles[0].AbsoluteModeSettings.Tablet.Width);
    }

    [Fact]
    public async Task FailedSaveRemainsDirtyAndOnlyExplicitSaveRetries()
    {
        var (session, _, store) = await Open();
        using var lifetime = session;
        await session.ApplyAsync(Document(120));
        store.SaveSucceeds = false;
        Assert.Equal(SettingsSaveStatus.Failed, (await session.SaveAsync()).Status);
        await session.RefreshAsync();
        await session.ApplyAsync(Document(140));
        Assert.Equal(1, store.Attempts);
        Assert.True(session.HasUnsavedChanges);
        store.SaveSucceeds = true;
        Assert.True((await session.SaveAsync()).IsSaved);
        Assert.Equal(2, store.Attempts);
    }

    [Fact]
    public async Task SuccessfulRpcWithRejectedValuesIsNotSuccess()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        daemon.Readback = _ => Document();
        Assert.Equal(SettingsApplyStatus.ApplyFailed, (await session.ApplyAsync(Document(120))).Status);
        Assert.True(session.IsPaused);
        Assert.Equal(SettingsSaveStatus.Paused, (await session.SaveAsync()).Status);
        Assert.Equal(0, store.Attempts);
    }

    [Fact]
    public async Task FailedPreflightNeverSendsOrSaves()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        daemon.GetSettingsHandler = () => Task.FromException<Settings?>(new IOException("read failed"));
        Assert.Equal(SettingsApplyStatus.CouldNotCheck, (await session.ApplyAsync(Document(120))).Status);
        Assert.Empty(daemon.Applied);
        Assert.True(session.IsPaused);
        Assert.Equal(0, store.Attempts);
    }

    [Fact]
    public async Task UncertainWriteClosesSessionSoLateCompletionCannotRaceSaveOrReload()
    {
        var (session, daemon, store) = await Open(TimeSpan.FromMilliseconds(50));
        using var lifetime = session;
        var release = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => release.Task;
        Assert.Equal(SettingsApplyStatus.ApplyFailed, (await session.ApplyAsync(Document(120))).Status);
        release.SetResult(true);
        Assert.Equal(SettingsReloadStatus.Disconnected, (await session.ReloadAsync()).Status);
        Assert.Equal(SettingsSaveStatus.Disconnected, (await session.SaveAsync()).Status);
        Assert.Equal(SettingsApplyStatus.Disconnected, (await session.ApplyAsync(Document(140))).Status);
        Assert.Equal(0, store.Attempts);
    }

    /// <summary>
    /// The driver editing its own settings pauses us the same as another application would (#919).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not hypothetical. On a device arriving, and after sleep, OTD runs <c>DetectTablets()</c> and then
    /// <c>SetSettings(Settings)</c>; reaching a profile for a tablet it has not seen generates one and
    /// adds it (<c>ProfileCollection.GetProfile</c>), and <c>MatchSpecifications</c> adjusts binding
    /// collections. The serialized settings genuinely differ afterwards, so the next observation pauses
    /// — and the artist did nothing but plug in a tablet.
    /// </para>
    /// <para>
    /// Pinned as the behaviour rather than fixed. Accepting differences seen near a detection would
    /// accept an unrelated edit that happened to arrive at the same moment, since nothing in the
    /// notification says which difference came from where. The cost is an extra Reload after attaching a
    /// new tablet; what this test guards is that the cost is deliberate and that the way out works.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADriverThatAddsAProfileForANewTablet_PausesLikeAnyOtherOutsideChange()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;

        // What OTD does to itself when a tablet it has not seen is attached.
        var withNewTablet = Document();
        withNewTablet.Profiles.Add(new Profile { Tablet = "A tablet OTD had not seen" });
        daemon.Settings = withNewTablet;

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, (await session.ApplyAsync(Document(120))).Status);
        Assert.True(session.IsPaused);
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Attempts);

        // And Reload is the way out, taking the driver's new profile with it.
        Assert.Equal(SettingsReloadStatus.Adopted, (await session.ReloadAsync()).Status);
        Assert.False(session.IsPaused);
        Assert.Equal(2, session.GetCurrent()!.Settings.Profiles.Count);

        // Edited from what the reload gave us, which is what the app does — it holds one document and
        // submits the whole of it. Editing from a snapshot taken before the detection would drop the
        // driver's new profile on the next write, and asserting that here would be pinning a mistake
        // the app does not make.
        var edited = session.GetCurrent()!.Settings;
        edited.Profiles[0].AbsoluteModeSettings.Tablet.Width = 120;

        Assert.True((await session.ApplyAsync(edited)).IsLive);
        Assert.Equal(2, session.GetCurrent()!.Settings.Profiles.Count);
    }

    /// <summary>
    /// A write that returned and a read back that failed is recoverable: Reload is a way out (#919).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves used to share one catch and one answer — close the session, tell the artist to restart
    /// their driver. Only one of them earns that. A write that has not returned may still be running
    /// inside the daemon, and nothing on this side can stop it: <c>SetSettings</c> takes no cancellation
    /// token and OTD's RPC host serves every connection against the same daemon object, so a fresh pipe
    /// is not a barrier. Restarting the daemon is the remedy because it is the only one that works.
    /// </para>
    /// <para>
    /// A verification read that failed is a different situation wearing the same exception. The write
    /// finished; only our knowledge of the result is missing, and another read supplies it. Closing there
    /// spent the artist's daemon restart on a question a Reload could answer.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AWriteThatLandedButCouldNotBeVerified_IsPausedRatherThanClosed()
    {
        var (session, daemon, store) = await Open(TimeSpan.FromMilliseconds(50));
        using var lifetime = session;

        // The write returns; the read back after it does not.
        var reads = 0;
        daemon.GetSettingsHandler = () => ++reads == 2
            ? new TaskCompletionSource<Settings?>().Task    // the verification read, never answered
            : Task.FromResult<Settings?>(Document());

        var applied = await session.ApplyAsync(Document(120));
        Assert.Equal(SettingsApplyStatus.CouldNotCheck, applied.Status);
        Assert.True(session.IsPaused, "an unverified apply should pause");

        // Not closed: a read that works establishes the baseline again.
        daemon.GetSettingsHandler = null;
        daemon.Settings = Document(120);
        var reloaded = await session.ReloadAsync();

        Assert.Equal(SettingsReloadStatus.Adopted, reloaded.Status);
        Assert.False(session.IsPaused, "Reload should have resolved it");
        Assert.Equal(0, store.Attempts);

        // And editing works again.
        Assert.True((await session.ApplyAsync(Document(140))).IsLive);
    }

    [Fact]
    public async Task DisconnectInvalidatesQueuedWorkAndNeverWritesToReplacement()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        var read = new TaskCompletionSource<Settings?>();
        daemon.GetSettingsHandler = () => read.Task;
        var apply = session.ApplyAsync(Document(120));
        var queued = session.SaveAsync();
        daemon.Reconnect();
        read.SetResult(Document());
        Assert.False((await apply).IsLive);
        Assert.Equal(SettingsSaveStatus.Disconnected, (await queued).Status);
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Attempts);
    }

    [Fact]
    public async Task ClosingCancelsWaitingReadsAndQueuedOperationsWithoutPersisting()
    {
        var (session, daemon, store) = await Open();
        var read = new TaskCompletionSource<Settings?>();
        daemon.GetSettingsHandler = () => read.Task;
        var apply = session.ApplyAsync(Document(120));
        var queued = session.SaveAsync();
        Assert.True(await session.CloseAsync(TimeSpan.FromSeconds(2)));
        Assert.False((await apply).IsLive);
        Assert.Equal(SettingsSaveStatus.Disconnected, (await queued).Status);
        Assert.Equal(0, store.Attempts);
        read.SetResult(Document());
    }

    [Fact]
    public async Task RestoreSavedAppliesWithoutWritingAndClearsDirtyState()
    {
        var (session, daemon, store) = await Open();
        using var lifetime = session;
        await session.ApplyAsync(Document(120));
        Assert.True((await session.RestoreSavedAsync()).IsRestored);
        Assert.False(session.HasUnsavedChanges);
        Assert.Equal(0, store.Attempts);
        Assert.Equal(100, daemon.Settings!.Profiles[0].AbsoluteModeSettings.Tablet.Width);
    }
}
