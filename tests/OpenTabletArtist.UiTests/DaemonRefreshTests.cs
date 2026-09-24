using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Services;
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

    private static (AppSession Session, FakeDaemonTransport Daemon, FakeLifecycle Lifecycle) Session()
    {
        var daemon = new FakeDaemonTransport();
        var store = new MemorySettingsFileStore();
        var lifecycle = new FakeLifecycle();
        return (new AppSession(FakeSession.Over(daemon, store), lifecycle), daemon, lifecycle);
    }
}
