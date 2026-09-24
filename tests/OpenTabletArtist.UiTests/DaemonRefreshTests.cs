using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// What the daemon menu's re-check does, and what it refuses to spend (#912).
/// </summary>
///
/// <remarks>
/// <para>
/// It was "Refresh status", and connected that is what it is: a reload. Disconnected it opened a
/// connection instead, bounded by the thirty-second operation timeout — and a connection attempt on a
/// machine with no daemon process is thirty seconds of waiting for an answer that cannot come.
/// </para>
/// <para>
/// The cost is not only the wait. <c>ShowStartButton</c> is <c>!IsConnected &amp;&amp; !IsConnecting</c>,
/// so for the duration of that attempt <b>Start is hidden</b> — the one action that would have helped,
/// withdrawn by an action the artist had no reason to think would block. Found in a Windows Sandbox
/// where the daemon could not start at all, so "not running" was the steady state and the wait was
/// reliably useless.
/// </para>
/// </remarks>
public class DaemonRefreshTests
{
    /// <summary>With nothing to reach, it says so instead of waiting.</summary>
    /// <remarks>
    /// The assertion that matters is <c>Connects == 0</c>: not that a message appears, but that no
    /// attempt was started. A version that said the right thing and connected anyway would still hide
    /// Start for thirty seconds.
    /// </remarks>
    [AvaloniaFact]
    public async Task WithNoDaemonRunning_ItDoesNotAttemptAConnection()
    {
        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;
        lifecycle.Running = false;

        await session.RefreshAsync();

        Assert.Equal(0, daemon.ConnectCalls);
        Assert.False(session.IsConnecting, "entering the connecting state is what hides Start");
        Assert.True(session.ShowStartButton, "Start is the action that works from here");
        Assert.Equal(AppSession.NoDaemonToReachMessage, session.DaemonOperationError);
    }

    /// <summary>With a daemon there, it still reconnects.</summary>
    /// <remarks>
    /// The control. Without it the guard above could be "never connect" and every assertion would pass.
    /// </remarks>
    [AvaloniaFact]
    public async Task WithADaemonRunning_ItStillTriesToReachIt()
    {
        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;
        lifecycle.Running = true;

        await session.RefreshAsync();

        Assert.True(daemon.ConnectCalls > 0);
    }

    /// <summary>And the message goes when there is something to reach again.</summary>
    /// <remarks>
    /// Otherwise the first refusal would sit on the page through every later success, which is the
    /// failure mode of any message written once and never cleared.
    /// </remarks>
    [AvaloniaFact]
    public async Task OnceADaemonIsThere_TheRefusalIsCleared()
    {
        var (session, _, lifecycle) = Session();
        using var lifetime = session;

        lifecycle.Running = false;
        await session.RefreshAsync();
        Assert.Equal(AppSession.NoDaemonToReachMessage, session.DaemonOperationError);

        lifecycle.Running = true;
        await session.RefreshAsync();

        Assert.Equal("", session.DaemonOperationError);
    }

    /// <summary>The refusal goes when the daemon turns up on its own.</summary>
    /// <remarks>
    /// The case the first version missed. A stalled attempt keeps retrying in the background, so the
    /// artist can be told "nothing to reconnect to", start the daemon outside OTA, and have the
    /// connection land by itself — with the refusal still on screen, and Home's daemon problem card
    /// still showing, because nothing on the success path cleared it (#949).
    /// </remarks>
    [AvaloniaFact]
    public async Task WhenTheDaemonTurnsUpByItself_TheRefusalGoes()
    {
        var (session, daemon, lifecycle) = Session();
        using var lifetime = session;

        lifecycle.Running = false;
        await session.RefreshAsync();
        Assert.Equal(AppSession.NoDaemonToReachMessage, session.DaemonOperationError);

        // Started outside OTA; the background retry reaches it.
        lifecycle.Running = true;
        daemon.Reconnect();
        await PumpUntil(() => session.IsConnected);

        Assert.Equal("", session.DaemonOperationError);
    }

    /// <summary>And connected, it reloads rather than reopening the connection.</summary>
    /// <remarks>
    /// The branch none of the first three tests touched, and the one the menu spends most of its life
    /// in. Both halves are asserted: that no connection was opened, <em>and</em> that a read happened.
    /// Checking only the first would pass just as well against a connected branch that did nothing at
    /// all, which is the same shape of hole as the three it was written to fill (#950).
    /// </remarks>
    [AvaloniaFact]
    public async Task WhenConnected_ItReloadsWithoutReconnecting()
    {
        var (session, daemon, _) = Session();
        using var lifetime = session;

        daemon.Reconnect();
        await PumpUntil(() => session.CurrentSettings is not null);

        var connectsBefore = daemon.ConnectCalls;
        var readsBefore = daemon.GetSettingsCalls;

        await session.RefreshAsync();
        await PumpUntil(() => daemon.GetSettingsCalls > readsBefore);

        Assert.Equal(connectsBefore, daemon.ConnectCalls);
        Assert.True(daemon.GetSettingsCalls > readsBefore, "a reload has to actually read");
    }

    /// <summary>
    /// Pumps the dispatcher until something is true, and <b>fails if it never is</b>.
    /// </summary>
    /// <remarks>
    /// Without the assertion an expired wait reads as completed setup, and whatever the test asserts
    /// next is asserted against a state that never arrived (#950).
    /// </remarks>
    private static async Task PumpUntil(System.Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(done(), "the state this test needs never arrived");
    }

    private static (AppSession Session, FakeDaemonTransport Daemon, FakeLifecycle Lifecycle) Session()
    {
        // Real settings, so a reload has something to read and can be seen to have read it.
        var daemon = new FakeDaemonTransport
        {
            Settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "T" } } },
        };
        var store = new MemorySettingsFileStore();
        var lifecycle = new FakeLifecycle();
        return (new AppSession(FakeSession.Over(daemon, store), lifecycle), daemon, lifecycle);
    }
}
