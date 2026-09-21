using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// That the card holding the way back to the bundled daemon is on screen when the offer is (#725).
/// </summary>
///
/// <remarks>
/// <para>
/// The offer was shipped inside a card whose own condition excluded it. `ShowDriverCard` was
/// `ShowLocateCard || ShowInstallCard`, and `ShowLocateCard` is false exactly when a bundled copy exists,
/// no location is chosen and the exe is not missing — which is precisely when the switch applies. So the
/// button rendered never, and the sentence telling a user they could not take the offer yet rendered
/// always. Both halves were unit-tested and correct; what was wrong was the composition.
/// </para>
/// <para>
/// A decision test cannot see that, which is why this one builds the view models. It was found by looking
/// at the running page, and it is cheap to keep from here.
/// </para>
/// </remarks>
public class DriverCardVisibilityTests
{
    /// <summary>On someone else's daemon with nothing chosen: the offer, and a card to hold it.</summary>
    [AvaloniaFact]
    public async Task WithNoChosenLocation_TheOfferAndItsCardAreBothOnScreen()
    {
        var page = await PageOnForeignDaemon(chosenLocation: null);

        Assert.True(page.CanSwitchToBundledDaemon, "the switch applies here");
        Assert.True(page.ShowDriverCard, "and the card that holds it has to be on screen for it to render");
        Assert.False(page.BundledIsBehindAChosenLocation);
    }

    /// <summary>With a location chosen, the offer becomes an explanation — and still has a card.</summary>
    [AvaloniaFact]
    public async Task WithAChosenLocation_TheExplanationTakesItsPlace()
    {
        var page = await PageOnForeignDaemon(chosenLocation: "C:/elsewhere/OpenTabletDriver.Daemon.exe");

        Assert.False(page.CanSwitchToBundledDaemon, "a restart would relaunch the chosen one, not ours");
        Assert.True(page.BundledIsBehindAChosenLocation);
        Assert.True(page.ShowDriverCard);
    }

    private static async Task<DaemonViewModel> PageOnForeignDaemon(string? chosenLocation)
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = SettingsFor("Any") };

        // Someone else's daemon is answering: the locator reports a path the app does not manage.
        var locator = new FakeProcessLocator { Path = "C:/someone-elses/OpenTabletDriver.Daemon.exe" };
        var lifecycle = new BundledLifecycle { Managed = "C:/app/Daemon/OpenTabletDriver.Daemon.exe" };

        var otd = OtdSession.ForTesting(daemon, new NullStore(), NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, locator);
        var session = new AppSession(otd, lifecycle);

        daemon.Reconnect();
        await session.ReloadAsync();
        Assert.Equal(DaemonOwnership.External, session.Ownership);

        return new DaemonViewModel(new DaemonStatusViewModel(session))
        {
            // What the picker would have stored. Set here rather than through AppSettings so the test
            // does not depend on this machine's settings file.
            UserDaemonPath = chosenLocation ?? "",
        };
    }

    private static Settings SettingsFor(string tablet) => new()
    {
        Profiles = new ProfileCollection { new Profile { Tablet = tablet } },
    };

    private sealed class NullStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }
        public bool TrySave(Settings settings, string path) => true;
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    /// <summary>A build that ships a bundled daemon, which is every Windows release.</summary>
    private sealed class BundledLifecycle : IDaemonLifecycleService
    {
        public string Managed { get; init; } = "";

        public string? ExpectedExePath() => Managed;
        public bool IsAppManaged(string? path) =>
            path != null && string.Equals(path, Managed, StringComparison.OrdinalIgnoreCase);
        public bool HasBundledDaemon() => true;
        public string? FindExe() => Managed;
        public bool IsRunning() => true;
        public string? Launch() => null;
        public bool Stop(int processId) => true;
        public void StopAll() { }
        public string? PathOf(int processId) => null;
        public string? SingleRunningDaemonPath() => null;
    }
}
