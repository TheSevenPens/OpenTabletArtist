using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// That the card holding the way back to the bundled daemon is on screen when the offer is (#725, #900).
/// </summary>
///
/// <remarks>
/// <para>
/// The offer shipped inside a card whose own condition excluded it. <c>ShowDriverCard</c> was
/// <c>ShowLocateCard || ShowInstallCard</c>, and <c>ShowLocateCard</c> is false exactly when a bundled
/// copy exists, no location is chosen and the exe is not missing — which is precisely when the switch
/// applies. So the button rendered never, and the sentence saying the offer cannot be taken yet rendered
/// always. Both halves were unit-tested and correct; the composition was not.
/// </para>
/// <para>
/// Reached through the state the page reads rather than through an <c>AppSession</c> (#900). Until that
/// seam existed this needed a session built over four fakes and an async reload, which is why the daemon
/// page had no tests and why both defects in this one feature were found by eye.
/// </para>
/// <para>
/// Every case sets <c>UserDaemonPath</c> explicitly. The view model initialises it from
/// <c>AppSettings</c> — this machine's own <c>settings.json</c> — so a case that leaves it alone is
/// testing the developer's configuration rather than the code. That is not hypothetical: the machine
/// this was written on has a stored path, and the one case that omitted it failed there and nowhere else.
/// </para>
/// </remarks>
public class DriverCardVisibilityTests
{
    /// <summary>On someone else's daemon with nothing chosen: the offer, and a card to hold it.</summary>
    [Fact]
    public void WithNoChosenLocation_TheOfferAndItsCardAreBothOnScreen()
    {
        using var page = PageOn(onForeignDaemon: true, hasBundled: true, chosenLocation: "");

        Assert.True(page.CanSwitchToBundledDaemon, "the switch applies here");
        Assert.True(page.ShowDriverCard, "and the card that holds it has to be on screen for it to render");
        Assert.False(page.BundledIsBehindAChosenLocation);
    }

    /// <summary>With a location chosen, the offer becomes an explanation — and still has a card.</summary>
    [Fact]
    public void WithAChosenLocation_TheExplanationTakesItsPlace()
    {
        using var page = PageOn(
            onForeignDaemon: true, hasBundled: true, chosenLocation: "C:/elsewhere/OpenTabletDriver.Daemon.exe");

        Assert.False(page.CanSwitchToBundledDaemon, "a restart would relaunch the chosen one, not ours");
        Assert.True(page.BundledIsBehindAChosenLocation);
        Assert.True(page.ShowDriverCard);
    }

    /// <summary>On our own daemon there is nothing to switch away from, so neither state applies.</summary>
    [Fact]
    public void OnTheAppsOwnDaemon_NeitherIsOffered()
    {
        using var page = PageOn(onForeignDaemon: false, hasBundled: true, chosenLocation: "");

        Assert.False(page.CanSwitchToBundledDaemon);
        Assert.False(page.BundledIsBehindAChosenLocation);
    }

    /// <summary>
    /// A build that bundles nothing has no such offer to make — which is every macOS build today.
    /// </summary>
    /// <remarks>
    /// Also the state a developer sees by default: a dev tree ships no daemon under <c>Daemon/</c>, so
    /// this is the branch the running app takes from <c>bin/Debug</c> (#901).
    /// </remarks>
    [Fact]
    public void WithNothingBundled_ThereIsNoOfferToMake()
    {
        using var page = PageOn(onForeignDaemon: true, hasBundled: false, chosenLocation: "");

        Assert.False(page.CanSwitchToBundledDaemon);
        Assert.False(page.BundledIsBehindAChosenLocation);
    }

    /// <summary>The offer follows the daemon: adopting someone else's makes it appear, live.</summary>
    /// <remarks>
    /// Through the notification rather than by rebuilding the page, because that is the wiring: the card
    /// has to move when the connected daemon changes, without a reload.
    /// </remarks>
    [Fact]
    public void WhenAForeignDaemonIsAdopted_TheOfferAppearsWithoutARebuild()
    {
        var connection = new FakeConnectionState { IsConnected = true, HasBundledDaemon = true };

        // UserDaemonPath is set explicitly even though "" is what this test means, because the view
        // model's field initializer reads AppSettings -- the real machine's settings.json. Left to
        // default, this passes or fails according to whether whoever runs it has ever pointed the app at
        // a daemon of their own. It did fail that way here.
        using var page = new DaemonViewModel(new DaemonStatusViewModel(connection)) { UserDaemonPath = "" };

        Assert.False(page.CanSwitchToBundledDaemon);

        connection.ShowForeignDaemonWarning = true;

        Assert.True(page.CanSwitchToBundledDaemon);
        Assert.True(page.ShowDriverCard);
    }

    private static DaemonViewModel PageOn(bool onForeignDaemon, bool hasBundled, string chosenLocation)
    {
        var connection = new FakeConnectionState
        {
            IsConnected = true,
            ShowForeignDaemonWarning = onForeignDaemon,
            HasBundledDaemon = hasBundled,
        };

        return new DaemonViewModel(new DaemonStatusViewModel(connection))
        {
            // What the picker would have stored. Set here rather than through AppSettings so the test
            // does not depend on this machine's settings file.
            UserDaemonPath = chosenLocation,
        };
    }
}
