using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Whether a daemon change re-classifies ownership, and not only re-reads settings (#807 Phase 7).
/// </summary>
///
/// <remarks>
/// <para>
/// Written because of something seen during the Phase 7 packaged-app run and <b>not</b> because a defect
/// was known: after the app reconnected to the bundled daemon, its home tab still read "An
/// OpenTabletDriver you installed, not the bundled copy". The settings path was demonstrably correct
/// across that reconnect — the reload took the new daemon's baseline and a subsequent apply reached it —
/// which made it tempting to call the card cosmetic.
/// </para>
/// <para>
/// It is not safe to call it that without checking. Ownership is not a label: it gates whether OTA
/// disables unapproved filters, whether it installs its plugin, and whether Stop/Restart asks first. If
/// the classification were stuck, those decisions would be made about the wrong daemon.
/// </para>
/// <para>
/// This pins the classification itself. It says nothing about what the health card renders — if this
/// passes and the card still disagrees on a real machine, the fault is presentational and lives
/// elsewhere.
/// </para>
/// </remarks>
public class DaemonOwnershipRefreshTests
{
    /// <summary>
    /// Reconnecting to a daemon the app manages re-classifies it as owned.
    /// </summary>
    [AvaloniaFact]
    public async Task ReconnectingToAnAppManagedDaemon_BecomesOwned()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = SettingsFor("Baseline") };
        var locator = new FakeProcessLocator { Path = "C:/elsewhere/OpenTabletDriver.Daemon.exe" };
        var lifecycle = new PathAwareLifecycle { Managed = "C:/app/Daemon/OpenTabletDriver.Daemon.exe" };

        var otd = OtdSession.ForTesting(daemon, new NullStore(), NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, locator);
        using var session = new AppSession(otd, lifecycle);

        daemon.Reconnect();
        await session.ReloadAsync();

        // Someone else's install: not ours, and known not to be.
        Assert.Equal(DaemonOwnership.External, session.Ownership);

        // The daemon changes to the one this app manages.
        locator.Path = "C:/app/Daemon/OpenTabletDriver.Daemon.exe";
        daemon.Reconnect();
        await session.ReloadAsync();

        Assert.Equal(DaemonOwnership.Owned, session.Ownership);
        Assert.True(session.IsAppOwnedDaemon);
        Assert.False(session.IsForeignDaemon);
    }

    /// <summary>
    /// And the reverse: reconnecting to someone else's install stops being owned.
    /// </summary>
    /// <remarks>
    /// The direction that matters for safety. Ownership left reading "ours" over a stranger's daemon is
    /// what disables their filters and installs plugins into their install.
    /// </remarks>
    [AvaloniaFact]
    public async Task ReconnectingToSomeoneElsesDaemon_StopsBeingOwned()
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = SettingsFor("Baseline") };
        var locator = new FakeProcessLocator { Path = "C:/app/Daemon/OpenTabletDriver.Daemon.exe" };
        var lifecycle = new PathAwareLifecycle { Managed = "C:/app/Daemon/OpenTabletDriver.Daemon.exe" };

        var otd = OtdSession.ForTesting(daemon, new NullStore(), NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, locator);
        using var session = new AppSession(otd, lifecycle);

        daemon.Reconnect();
        await session.ReloadAsync();
        Assert.Equal(DaemonOwnership.Owned, session.Ownership);

        locator.Path = "C:/elsewhere/OpenTabletDriver.Daemon.exe";
        daemon.Reconnect();
        await session.ReloadAsync();

        Assert.Equal(DaemonOwnership.External, session.Ownership);
        Assert.False(session.IsAppOwnedDaemon);
    }

    // --- harness --------------------------------------------------------------------------------

    /// <summary>A lifecycle service that manages exactly one path, as the packaged app manages its own.</summary>
    private sealed class PathAwareLifecycle : IDaemonLifecycleService
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

    private sealed class NullStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }

        public bool TrySave(Settings settings, string path) => true;

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = null;
            return false;
        }
    }

    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };
}
