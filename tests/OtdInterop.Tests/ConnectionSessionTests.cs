using System;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using Xunit;

namespace OtdInterop.Tests;

public class ConnectionSessionTests
{
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
