using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using Xunit;

namespace OtdInterop.Tests;

public class ConnectionSessionTests
{
    /// <summary>
    /// A write whose RPC task faulted when the connection dropped is still outstanding (#922).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Losing the connection faults the client's task while the server method carries on running — the
    /// reply simply has nowhere to go. Clearing the block on <c>IsCompleted</c> therefore cleared it on
    /// exactly the case it exists for, because a faulted task is a completed one.
    /// </para>
    /// <para>
    /// Codex reproduced the consequence over a real pipe: drop the connection mid-write, reconnect, apply
    /// 140 and save it, then let the original write land. The daemon ends up holding 180, the file and
    /// OTA's snapshot hold 140, and nothing reports a conflict. This is that fault modelled at the seam
    /// where the decision is made.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AWriteWhoseTaskFaultedOnDisconnect_StillBlocksEditing()
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

        // The connection goes, and with it the channel the answer would have come back on.
        daemon.RaiseDisconnected();
        stranded.SetException(new IOException("the pipe went away mid-write"));
        daemon.SetSettingsHandler = null;
        daemon.Reconnect();
        await session.InitializeAsync();

        Assert.False(session.CanEditSettings,
            "a faulted task says the answer was lost, not that the write was");
        Assert.Contains("never confirmed", session.SettingsProblem);
    }

    /// <summary>
    /// A disconnect that arrives before the write's own timeout still leaves it recorded (#922).
    /// </summary>
    /// <remarks>
    /// The write used to be recorded in the catch that handles giving up on it. A disconnect reaches the
    /// owner first, so retirement asked what was outstanding and got nothing — the record appeared
    /// afterwards, on a coordinator already thrown away. Recorded when the write is sent instead, which
    /// is the only moment that is certainly before anything can go wrong.
    /// </remarks>
    [Fact]
    public async Task AWriteStillInFlightWhenTheConnectionDrops_IsRetainedByTheOwner()
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

        // Held open, and the connection drops well inside the RPC timeout.
        var stranded = new TaskCompletionSource<bool>();
        daemon.SetSettingsHandler = _ => stranded.Task;
        var applying = session.Settings!.ApplyAsync(SettingsSessionTests.Document(180));
        await WaitFor(() => daemon.Applied.Count > 0, "the write to reach the daemon");

        daemon.RaiseDisconnected();
        daemon.SetSettingsHandler = null;
        daemon.Reconnect();
        await session.InitializeAsync();

        Assert.False(session.CanEditSettings,
            "a write in flight when the connection dropped was forgotten with the session");

        // And it ends when the write finally answers.
        stranded.SetResult(true);
        await applying;
        await session.InitializeAsync();
        Assert.True(session.CanEditSettings);
    }

    /// <summary>
    /// A disconnect raised <em>inside</em> the send still leaves the write recorded (#923).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recording the write on the line after the send closed the window that started at the timeout and
    /// left the one inside the send itself. Sending is exactly where a connection goes: the disconnect
    /// arrives while <c>SetSettingsAsync</c> has not yet returned a task to record, retirement asks what
    /// is outstanding and gets nothing, and editing reopens on the next connection over a write the
    /// daemon is still holding.
    /// </para>
    /// <para>
    /// So the marker is published before the transport is entered, and settled from the real write when
    /// it answers. Before the send is the only point that is certainly earlier than anything the send can
    /// do.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADisconnectRaisedInsideTheSend_StillLeavesTheWriteRecorded()
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

        // The connection goes while the send is still running, before it has a task to hand back.
        var stranded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            daemon.RaiseDisconnected();
            return stranded.Task;
        };

        var applying = session.Settings!.ApplyAsync(SettingsSessionTests.Document(180));
        await WaitFor(() => daemon.Applied.Count > 0, "the write to reach the daemon");

        daemon.SetSettingsHandler = null;
        daemon.Reconnect();
        await session.InitializeAsync();

        Assert.False(session.CanEditSettings,
            "the write was sent and never answered, but nothing recorded it");
        Assert.Contains("never confirmed", session.SettingsProblem);

        // And it ends when the daemon finally answers, as it does everywhere else. The apply gave up at
        // the disconnect, so waiting on it establishes nothing about the write; the marker settles from
        // the write's own completion, a moment after the daemon's method returns.
        stranded.SetResult(true);
        await applying;
        await WaitFor(async () =>
        {
            await session.InitializeAsync();
            return session.CanEditSettings;
        }, "editing to reopen once the write answered");
    }

    /// <summary>As <see cref="WaitFor(Func{bool}, string)"/>, for a condition that has to be asked.</summary>
    private static async Task WaitFor(Func<Task<bool>> until, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!await until())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {what}.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
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
