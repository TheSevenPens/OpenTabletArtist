using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Services;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// A wait the artist did not choose says how long it is prepared to be (#912).
/// </summary>
///
/// <remarks>
/// <para>
/// The indicator counted seconds upward and stopped there. Elapsed time tells you time is passing; it
/// does not tell you whether to keep waiting, which is what the artist actually needs to decide. The
/// bound was in the code as <c>DaemonOperationTimeout</c> and nowhere on screen.
/// </para>
/// <para>
/// Only the connect says it. Start, Stop and Restart are bounded by the same timeout, but they are acts
/// the artist chose knowing something would happen — not a wait they were dropped into.
/// </para>
/// </remarks>
public class ConnectWaitDisclosureTests
{
    /// <summary>
    /// While connecting, the text carries the elapsed time, the bound, and what the bound does.
    /// </summary>
    /// <remarks>
    /// "of up to 30s" was the first wording and it implied a deadline. At the timeout the spinner goes
    /// and the attempt is marked stalled, but the transport keeps retrying — so the sentence has to say
    /// that rather than let the reader assume it stops (#955).
    /// </remarks>
    [AvaloniaFact]
    public void WhileConnecting_ItSaysHowLongItWillWait()
    {
        using var session = Session();
        session.DaemonOperationTimeout = TimeSpan.FromSeconds(30);
        session.ConnectionStatus = "Connecting...";
        session.ConnectPhase = "Connecting to the daemon…";
        session.ConnectElapsedSeconds = 12;

        Assert.Equal(
            "Connecting to the daemon…  ·  12s · after 30s we keep trying in the background",
            session.DaemonActivityText);
    }

    /// <summary>The bound follows the timeout rather than being written into the sentence.</summary>
    /// <remarks>
    /// A hardcoded "30s" would read correctly today and lie the moment the timeout moved, which is the
    /// kind of wrong a reader cannot see.
    /// </remarks>
    [AvaloniaFact]
    public void TheBoundIsTheActualTimeout()
    {
        using var session = Session();
        session.DaemonOperationTimeout = TimeSpan.FromSeconds(5);
        session.ConnectionStatus = "Connecting...";
        session.ConnectElapsedSeconds = 2;

        Assert.Contains("after 5s we keep trying", session.DaemonActivityText);
    }

    /// <summary>Before the counter starts there is no time to report, so it says none.</summary>
    [AvaloniaFact]
    public void BeforeTheCounterStarts_ItIsJustThePhase()
    {
        using var session = Session();
        session.ConnectionStatus = "Connecting...";
        session.ConnectPhase = "Starting the daemon…";

        Assert.Equal("Starting the daemon…", session.DaemonActivityText);
    }

    /// <summary>A Start, Stop or Restart keeps its own wording.</summary>
    /// <remarks>
    /// The control that stops this becoming "append the timeout everywhere": those are bounded by the
    /// same 30 seconds, and telling someone who pressed Stop that it may take half a minute is noise
    /// about an act they chose.
    /// </remarks>
    [AvaloniaFact]
    public void ALifecycleOperationDoesNotAnnounceTheBound()
    {
        using var session = Session();
        session.IsDaemonBusy = true;
        session.DaemonOperationStatus = "Stopping daemon…";
        session.ConnectElapsedSeconds = 4;

        Assert.Equal("Stopping daemon…  ·  4s", session.DaemonActivityText);
    }

    /// <summary>
    /// Pressing Reconnect again while one attempt is already running does not start a second.
    /// </summary>
    /// <remarks>
    /// The numerator and the bound have to describe the same interval. They did not: each call started
    /// a fresh monitor and a fresh deadline, while <c>SyncActivityTimer</c> left the stopwatch alone
    /// because the activity flag never went false — so the indicator showed one attempt's elapsed
    /// against another's bound, and with a short timeout it read "3s of up to 2s" (#955).
    ///
    /// <para>
    /// Coalescing rather than resetting both: a second attempt was never wanted. The first is still
    /// running and the transport is still retrying, so the honest response to the second press is that
    /// nothing new needs to happen.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task PressingReconnectTwice_DoesNotStartASecondAttempt()
    {
        var daemon = new FakeDaemonTransport();
        var lifecycle = new FakeLifecycle { Running = true };
        using var session = new AppSession(
            FakeSession.Over(daemon, new MemorySettingsFileStore()), lifecycle);

        await session.RefreshAsync();
        Assert.True(session.IsConnecting, "the first press should be connecting");
        var afterFirst = daemon.ConnectCalls;

        await session.RefreshAsync();

        Assert.Equal(afterFirst, daemon.ConnectCalls);
    }

    private static AppSession Session() =>
        new(FakeSession.Over(new FakeDaemonTransport(), new MemorySettingsFileStore()), new FakeLifecycle());
}
