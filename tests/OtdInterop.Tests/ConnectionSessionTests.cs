using System;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using Xunit;

namespace OtdInterop.Tests;

public class ConnectionSessionTests
{
    /// <summary>
    /// A write nobody waited for cannot be allowed to land on top of a replacement session's work
    /// (#919).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ordering my first attempt at this missed. It had the abandoned write complete <em>before</em>
    /// the replacement edited, where the pre-apply comparison catches it — so it proved the easy case and
    /// read as though the hard one were covered. Here the write lands after the replacement has applied
    /// 140 and saved it: the daemon ends up holding 180, the file holds 140, and nothing on the way there
    /// had reason to complain.
    /// </para>
    /// <para>
    /// Detecting that afterwards is not enough, because by then it has happened. The write is the
    /// daemon's to finish and OTA cannot stop it — <c>SetSettings</c> takes no cancellation token — so
    /// the only honest answer is to refuse to edit until it is finished, or until the process it was
    /// sent to is gone. Automatic transport reconnect is untouched; what is withheld is writing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAbandonedWriteBlocksEditingUntilItFinishes()
    {
        var daemon = new FakeDaemonTransport
        {
            Settings = SettingsSessionTests.Document(),
            ServerProcessId = 4242,
        };
        var store = new MemorySettingsFileStore { Saved = SettingsSessionTests.Document() };
        using var session = FakeSession.Over(daemon, store);
        daemon.Reconnect();
        await session.InitializeAsync();

        // A write that never comes back, abandoned when the RPC wait gives up.
        var stranded = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => stranded.Task;
        Assert.False((await session.Settings!.ApplyAsync(SettingsSessionTests.Document(180))).IsLive);

        // The same daemon process comes back.
        daemon.RaiseDisconnected();
        daemon.SetSettingsHandler = null;
        daemon.Reconnect();
        await session.InitializeAsync();

        // Editing is refused: that write is still out there, carrying whole settings.
        Assert.False(session.CanEditSettings, "editing resumed while an abandoned write could still land");
        Assert.Contains("never confirmed", session.SettingsProblem);

        // It finally lands, on a daemon nobody is editing.
        stranded.SetResult(true);
        await session.InitializeAsync();

        Assert.True(session.CanEditSettings, "the write finished, so editing should be possible again");
        Assert.True((await session.Settings!.ApplyAsync(SettingsSessionTests.Document(140))).IsLive);
        Assert.Equal(SettingsSaveStatus.Saved, (await session.Settings.SaveAsync()).Status);
    }

    /// <summary>
    /// A different driver process cannot be holding the old one's write, so editing resumes (#919).
    /// </summary>
    /// <remarks>
    /// Without this the refusal would have no end for the one remedy that actually works. Restarting the
    /// driver is what the unconfirmed-write message asks for, and a restart is a new process — which the
    /// executable and settings paths cannot show, since an intentional restart keeps both.
    /// </remarks>
    [Fact]
    public async Task ARestartedDriverProcessEndsTheRefusal()
    {
        var daemon = new FakeDaemonTransport
        {
            Settings = SettingsSessionTests.Document(),
            ServerProcessId = 4242,
        };
        var store = new MemorySettingsFileStore { Saved = SettingsSessionTests.Document() };
        using var session = FakeSession.Over(daemon, store);
        daemon.Reconnect();
        await session.InitializeAsync();

        var stranded = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => stranded.Task;
        Assert.False((await session.Settings!.ApplyAsync(SettingsSessionTests.Document(180))).IsLive);

        // The artist restarts the driver, which is what the message told them to do.
        daemon.RaiseDisconnected();
        daemon.SetSettingsHandler = null;
        daemon.ServerProcessId = 5151;
        daemon.Reconnect();
        await session.InitializeAsync();

        Assert.True(session.CanEditSettings, "a restarted driver cannot be holding the old write");
        Assert.True((await session.Settings!.ApplyAsync(SettingsSessionTests.Document(140))).IsLive);

        stranded.TrySetResult(true);
    }

    [Fact]
    public async Task SameDaemonReconnectReadsFreshStateWithoutReplayingUnsavedEdits()
    {
        var daemon = new FakeDaemonTransport { Settings = SettingsSessionTests.Document() };
        var store = new MemorySettingsFileStore { Saved = SettingsSessionTests.Document() };
        using var session = FakeSession.Over(daemon, store);
        daemon.Reconnect();
        await session.InitializeAsync();
        var old = Assert.IsAssignableFrom<IOtdSettingsSession>(session.Settings);
        await old.ApplyAsync(SettingsSessionTests.Document(120));
        daemon.RaiseDisconnected();
        daemon.Settings = SettingsSessionTests.Document();
        daemon.Reconnect();
        await session.InitializeAsync();
        Assert.NotSame(old, session.Settings);
        Assert.True(session.CanEditSettings);
        Assert.False(session.Settings!.HasUnsavedChanges);
        Assert.Single(daemon.Applied);
        Assert.Equal(0, store.Attempts);
        Assert.Equal(SettingsApplyStatus.Disconnected, (await old.ApplyAsync(SettingsSessionTests.Document(140))).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedExecutableOrSettingsLocationRequiresAppRestart(bool executable)
    {
        var daemon = new FakeDaemonTransport { Settings = SettingsSessionTests.Document() };
        var locator = new FakeProcessLocator { Path = "first.exe" };
        using var session = FakeSession.Over(daemon, locator: locator);
        daemon.Reconnect();
        await session.InitializeAsync();
        daemon.RaiseDisconnected();
        if (executable) locator.Path = "second.exe";
        else daemon.AppInfo = FakeDaemonTransport.Reporting("other/settings.json");
        daemon.Reconnect();
        await session.InitializeAsync();
        Assert.False(session.CanEditSettings);
        Assert.Null(session.Settings);
        Assert.Contains("Restart OpenTabletArtist", session.SettingsProblem);
        Assert.Empty(daemon.Applied);
    }

    [Fact]
    public async Task UnidentifiedDaemonIsReadOnlyAndMetadataCanBeRetried()
    {
        var daemon = new FakeDaemonTransport { Settings = SettingsSessionTests.Document() };
        var locator = new FakeProcessLocator();
        using var session = FakeSession.Over(daemon, locator: locator);
        daemon.Reconnect();
        await session.InitializeAsync();
        Assert.Null(session.Settings);
        locator.Path = "identified.exe";
        locator.OnlyDaemon = "identified.exe";
        await session.InitializeAsync();
        Assert.True(session.CanEditSettings);
        Assert.Empty(daemon.Applied);
    }

    [Fact]
    public async Task LateInitializationCannotPublishOrPinADisconnectedDaemon()
    {
        var daemon = new FakeDaemonTransport { Settings = SettingsSessionTests.Document() };
        var metadata = new TaskCompletionSource<AppInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetAppInfoHandler = () => metadata.Task;
        var locator = new FakeProcessLocator { Path = "old.exe" };
        using var session = FakeSession.Over(daemon, locator: locator);
        var events = 0;
        session.Connected += _ => events++;
        daemon.Reconnect();
        daemon.RaiseDisconnected();
        locator.Path = "new.exe";
        daemon.GetAppInfoHandler = null;
        daemon.Reconnect();
        metadata.SetResult(FakeDaemonTransport.Reporting("old/settings.json"));
        await session.InitializeAsync();
        Assert.True(session.CanEditSettings);
        Assert.Equal("new.exe", session.ConnectedExecutablePath);
        Assert.Equal(1, events);
    }

    [Fact]
    public async Task DisposeDuringInitializationPublishesNothing()
    {
        var daemon = new FakeDaemonTransport { Settings = SettingsSessionTests.Document() };
        var metadata = new TaskCompletionSource<AppInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetAppInfoHandler = () => metadata.Task;
        var session = FakeSession.Over(daemon);
        var events = 0;
        session.Connected += _ => events++;
        daemon.Reconnect();
        session.Dispose();
        metadata.SetResult(daemon.AppInfo);
        await session.InitializeAsync();
        Assert.Null(session.Settings);
        Assert.Equal(0, events);
        Assert.True(daemon.IsDisposed);
        Assert.False(daemon.AutoReconnect);
    }
}
