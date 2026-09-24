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
    /// <summary>While connecting, the text carries both the elapsed time and the bound.</summary>
    [AvaloniaFact]
    public void WhileConnecting_ItSaysHowLongItWillWait()
    {
        using var session = Session();
        session.DaemonOperationTimeout = TimeSpan.FromSeconds(30);
        session.ConnectionStatus = "Connecting...";
        session.ConnectPhase = "Connecting to the daemon…";
        session.ConnectElapsedSeconds = 12;

        Assert.Equal("Connecting to the daemon…  ·  12s of up to 30s", session.DaemonActivityText);
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

        Assert.Contains("of up to 5s", session.DaemonActivityText);
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

    private static AppSession Session() =>
        new(FakeSession.Over(new FakeDaemonTransport(), new MemorySettingsFileStore()), new FakeLifecycle());
}
