using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The version rule (#1042): the app attaches only to a daemon of the release it ships with.
/// </summary>
/// <remarks>
/// <para>
/// The release is the first three numbers. A different one, or a version that cannot be read, is refused:
/// nothing is loaded from it, nothing is written to it, and the app says why. The fourth number is a
/// revision of the same release, which works and is noted.
/// </para>
/// <para>
/// What each refusal test asserts is that nothing was <i>attached</i>, not that a message appeared: a
/// version that explained itself and connected anyway would pass every assertion about the message.
/// </para>
/// </remarks>
public class DaemonVersionGateTests
{
    private static Version Expected => Version.Parse(HealthService.ExpectedOtdVersion);

    /// <summary>The same release with its revision moved. Built from the shipped version rather than
    /// written out, so the tests keep meaning the same thing when the bundled OTD is bumped.</summary>
    private static string Revised(int by) =>
        new Version(Expected.Major, Expected.Minor, Math.Max(Expected.Build, 0), Math.Max(Expected.Revision, 0) + by).ToString();

    /// <summary>A different release: the patch number moved.</summary>
    private static string OtherRelease() =>
        new Version(Expected.Major, Expected.Minor, Math.Max(Expected.Build, 0) + 1, Math.Max(Expected.Revision, 0)).ToString();

    [AvaloniaFact]
    public async Task ADifferentRelease_IsRefused_AndNothingIsAttached()
    {
        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;
        lifecycle.Version = OtherRelease();
        var connectedEvents = 0;
        session.Connected += () => connectedEvents++;

        daemon.Reconnect();
        await PumpUntil(() => session.IsDaemonRefused);

        Assert.False(session.IsConnected);
        Assert.Equal(0, connectedEvents);
        Assert.Null(session.CurrentSettings);
        Assert.Empty(session.Profiles);
        Assert.Empty(daemon.Applied);

        // And it says what it saw, what it needs, and what to do.
        Assert.Equal(OtherRelease(), session.DaemonVersion);
        Assert.Contains(OtherRelease(), session.DaemonRefusalReason);
        Assert.Contains(HealthService.ExpectedOtdVersion, session.DaemonRefusalReason);
        Assert.Contains("Stop that daemon", session.DaemonRefusalReason);
        Assert.Equal("Wrong daemon version", session.DaemonStatusText);
    }

    /// <summary>Control: the same fixture with a matching version connects. Without it the refusals above
    /// could be "never connect in this harness" and every one of them would pass.</summary>
    [AvaloniaFact]
    public async Task TheShippedRelease_Attaches()
    {
        var (session, daemon, _) = Session();
        using var lifetime = session;
        var connectedEvents = 0;
        session.Connected += () => connectedEvents++;

        daemon.Reconnect();
        await PumpUntil(() => session.IsConnected);

        Assert.False(session.IsDaemonRefused);
        Assert.Equal(1, connectedEvents);
        Assert.NotNull(session.CurrentSettings);
        Assert.False(session.HasDaemonVersionNote);
    }

    [AvaloniaTheory]
    [InlineData(1, "newer")]
    [InlineData(-1, "older")]
    public async Task ADifferentRevisionOfTheSameRelease_Attaches_AndIsNoted(int by, string word)
    {
        if (by < 0 && Math.Max(Expected.Revision, 0) == 0) return;   // nothing older to be

        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;
        lifecycle.Version = Revised(by);

        daemon.Reconnect();
        await PumpUntil(() => session.IsConnected);

        Assert.False(session.IsDaemonRefused);
        Assert.True(session.HasDaemonVersionNote);
        Assert.Contains(word, session.DaemonVersionNote);
        Assert.Contains(Revised(by), session.DaemonVersionNote);
    }

    /// <summary>An older revision is said more strongly: it may lack a fix.</summary>
    [AvaloniaFact]
    public async Task AnOlderRevisionIsSaidMoreStronglyThanANewerOne()
    {
        var (older, olderDaemon, olderLifecycle) = Session();
        using var a = older;
        olderLifecycle.Version = Revised(-1);
        var (newer, newerDaemon, newerLifecycle) = Session();
        using var b = newer;
        newerLifecycle.Version = Revised(1);

        olderDaemon.Reconnect();
        newerDaemon.Reconnect();
        await PumpUntil(() => older.IsConnected && newer.IsConnected);

        if (Math.Max(Expected.Revision, 0) == 0) return;   // no older revision exists to compare
        Assert.Contains("missing fixes", older.DaemonVersionNote);
        Assert.DoesNotContain("missing fixes", newer.DaemonVersionNote);
    }

    /// <summary>The path cannot be read (an elevated or another user's daemon), so neither can the version.</summary>
    [AvaloniaFact]
    public async Task AVersionThatCannotBeRead_IsRefused()
    {
        var daemon = new FakeDaemonTransport { Settings = Settings() };
        var session = new AppSession(
            FakeSession.Over(daemon, new MemorySettingsFileStore(), new FakeProcessLocator { Path = null, OnlyDaemon = null }),
            new FakeLifecycle());
        using var lifetime = session;

        daemon.Reconnect();
        await PumpUntil(() => session.IsDaemonRefused);

        Assert.False(session.IsConnected);
        Assert.Contains("couldn't read", session.DaemonRefusalReason);
    }

    /// <summary>Start connects to whatever is already running, which here is the refused daemon. Offering it
    /// could only repeat the refusal, so it is not offered and, if invoked, starts nothing.</summary>
    [AvaloniaFact]
    public async Task WhileRefused_StartIsNotOffered_AndStartsNothing()
    {
        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;
        lifecycle.Version = OtherRelease();
        daemon.Reconnect();
        await PumpUntil(() => session.IsDaemonRefused);
        var connects = daemon.ConnectCalls;

        Assert.False(session.ShowStartButton);
        Assert.False(session.CanStartDaemon);

        await session.StartDaemonCommand.ExecuteAsync(null);

        Assert.Equal(connects, daemon.ConnectCalls);
        Assert.Null(lifecycle.Launched);
        Assert.Equal(session.DaemonRefusalReason, session.DaemonOperationError);
    }

    /// <summary>The way out. Stopping the refused daemon is what clears the refusal and offers Start again,
    /// and the next daemon is judged afresh.</summary>
    [AvaloniaFact]
    public async Task WhenTheRefusedDaemonGoesAway_TheRefusalClears_AndStartIsOffered()
    {
        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;
        lifecycle.Version = OtherRelease();
        daemon.Reconnect();
        await PumpUntil(() => session.IsDaemonRefused);

        daemon.RaiseDisconnected();
        await PumpUntil(() => !session.IsDaemonRefused);

        Assert.Equal("", session.DaemonRefusalReason);
        Assert.Equal("", session.DaemonVersion);
        Assert.True(session.ShowStartButton);

        // The bundled daemon comes up and is accepted.
        lifecycle.Version = null;
        daemon.Reconnect();
        await PumpUntil(() => session.IsConnected);
        Assert.False(session.IsDaemonRefused);
    }

    /// <summary>The bundled daemon that replaces a refused one is used, not called "a different
    /// OpenTabletDriver" and held read-only until the app restarts.</summary>
    /// <remarks>
    /// The test above gives both daemons one path, which is exactly what hid this: the library pins the first
    /// daemon it adopts, so only two daemons that differ can show it. Found by running the app against a real
    /// OpenTabletDriver 0.6.7.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheDaemonThatReplacesARefusedOne_IsUsed_NotCalledADifferentDriver()
    {
        var daemon = new FakeDaemonTransport { Settings = Settings() };
        daemon.AppInfo = FakeDaemonTransport.Reporting("foreign/settings.json");
        var locator = new FakeProcessLocator { Path = "foreign.exe" };
        var lifecycle = new FakeLifecycle { Version = OtherRelease() };
        using var session = new AppSession(
            FakeSession.Over(daemon, new MemorySettingsFileStore(), locator), lifecycle);
        daemon.Reconnect();
        await PumpUntil(() => session.IsDaemonRefused);
        daemon.RaiseDisconnected();
        await PumpUntil(() => !session.IsDaemonRefused);

        lifecycle.Version = null;
        locator.Path = "bundled.exe";
        daemon.AppInfo = FakeDaemonTransport.Reporting("bundled/settings.json");
        daemon.Reconnect();
        await PumpUntil(() => session.IsConnected);

        Assert.True(session.CanEditSettings);
        Assert.DoesNotContain("Restart OpenTabletArtist", session.SaveStatusText);
    }

    /// <summary>A refused daemon must not become the one a later Start relaunches. Start and Restart
    /// relaunch the first daemon the app accepted, and pinning a refused one would put it straight back.</summary>
    [AvaloniaFact]
    public async Task ARefusedDaemon_IsNotPinnedAsTheOneToRelaunch()
    {
        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;
        lifecycle.Running = false;
        lifecycle.Version = OtherRelease();
        daemon.Reconnect();
        await PumpUntil(() => session.IsDaemonRefused);
        daemon.RaiseDisconnected();
        await PumpUntil(() => !session.IsDaemonRefused);

        // Fire and forget: no daemon will answer, so the start would wait out its timeout.
        _ = session.StartDaemonCommand.ExecuteAsync(null);
        await PumpUntil(() => lifecycle.LaunchCount > 0);

        Assert.Null(lifecycle.Launched);
    }

    /// <summary>Control for the test above: an accepted daemon IS pinned, so a missing pin is the refusal's
    /// doing and not something this harness never does.</summary>
    [AvaloniaFact]
    public async Task AnAcceptedDaemon_IsPinnedAsTheOneToRelaunch()
    {
        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;
        daemon.Reconnect();
        await PumpUntil(() => session.IsConnected);
        daemon.RaiseDisconnected();
        await PumpUntil(() => !session.IsConnected);
        lifecycle.Running = false;

        _ = session.StartDaemonCommand.ExecuteAsync(null);
        await PumpUntil(() => lifecycle.LaunchCount > 0);

        Assert.Equal("daemon.exe", lifecycle.Launched);
    }

    private static Settings Settings() => new()
    {
        Profiles = new ProfileCollection { new Profile { OutputMode = Tests.TestOtd.Output(), Tablet = "T" } },
    };

    private static (AppSession Session, FakeDaemonTransport Daemon, FakeLifecycle Lifecycle) Session()
    {
        var daemon = new FakeDaemonTransport { Settings = Settings() };
        var lifecycle = new FakeLifecycle();
        return (new AppSession(FakeSession.Over(daemon, new MemorySettingsFileStore()), lifecycle), daemon, lifecycle);
    }

    /// <summary>Pumps the dispatcher until something is true, and fails if it never is (#950).</summary>
    private static async Task PumpUntil(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(done(), "the state this test needs never arrived");
    }
}
