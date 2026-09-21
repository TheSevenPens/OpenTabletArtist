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
