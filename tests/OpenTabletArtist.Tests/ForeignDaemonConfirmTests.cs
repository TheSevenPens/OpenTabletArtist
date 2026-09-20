using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;
using OtdInterop;
using OtdInterop.Tests;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Stopping or restarting a daemon this app didn't start asks first (#613, option 2). The policy that
/// matters is the negative one: declining must leave the daemon alone AND leave the session as it was —
/// a half-applied stop (auto-reconnect off, busy flag set) after a "No" would be worse than no prompt.
/// </summary>
public class ForeignDaemonConfirmTests
{
    private sealed class FakeLifecycle : IDaemonLifecycleService
    {
        public int StopAllCount { get; private set; }
        public string? ExpectedExePath() => "fake-daemon.exe";
        public bool IsAppManaged(string? path) => path != null && ExpectedExePath() != null;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => "fake-daemon.exe";
        public bool IsRunning() => false;
        public string? Launch() => null;
        public bool Stop(int processId) => true;
        public void StopAll() => StopAllCount++;
        public string? PathOf(int processId) => null;
        public string? SingleRunningDaemonPath() => null;
    }

    private sealed class FakeStore : IPresetStore
    {
        public bool TryLoad(string path, out OpenTabletDriver.Desktop.Settings? settings)
        { settings = null; return false; }
        public void Save(OpenTabletDriver.Desktop.Settings settings, string path) { }
        public bool TrySave(OpenTabletDriver.Desktop.Settings settings, string path) => true;
    }

    /// <summary>
    /// Provenance is three-valued, not two: ours, theirs, or unread — and those want different
    /// behaviour. It used to take a pair of booleans and warn that <c>owned</c> had to be stated rather
    /// than inferred from <c>foreign</c>; #742 made that a single value, so the illegal fourth
    /// combination can no longer be written.
    /// </summary>
    private static AppSession NewSession(out FakeLifecycle lifecycle, DaemonOwnership ownership) =>
        NewSession(out lifecycle, out _, ownership);

    /// <summary>
    /// Also hands back the transport, for the test that watches the reconnect gate. That flag belongs to
    /// the connection, which the app no longer reaches directly (#807).
    /// </summary>
    private static AppSession NewSession(out FakeLifecycle lifecycle, out FakeDaemonTransport daemon,
        DaemonOwnership ownership)
    {
        lifecycle = new FakeLifecycle();
        daemon = new FakeDaemonTransport();
        return new AppSession(FakeSession.Over(daemon), lifecycle)
        {
            DaemonOperationTimeout = TimeSpan.FromMilliseconds(150),
            Ownership = ownership,
        };
    }

    [Fact]
    public async Task Stop_AsksFirstWhenTheDaemonIsForeign()
    {
        using var session = NewSession(out var lifecycle, out var daemon, DaemonOwnership.External);
        var asked = new List<string>();
        session.ConfirmForeignDaemonAction = verb => { asked.Add(verb); return Task.FromResult(false); };

        await session.StopDaemonCommand.ExecuteAsync(null);

        Assert.Equal(["stop"], asked);
        Assert.Equal(0, lifecycle.StopAllCount);           // declined → their daemon is untouched
        Assert.True(daemon.AutoReconnect);                 // ...and nothing was half-applied
        Assert.False(session.IsDaemonBusy);
    }

    [Fact]
    public async Task Stop_ProceedsOnceTheUserAgrees()
    {
        using var session = NewSession(out var lifecycle, DaemonOwnership.External);
        session.ConfirmForeignDaemonAction = _ => Task.FromResult(true);

        await session.StopDaemonCommand.ExecuteAsync(null);

        Assert.Equal(1, lifecycle.StopAllCount);
    }

    [Fact]
    public async Task Stop_DoesNotAskAboutOurOwnDaemon()
    {
        using var session = NewSession(out var lifecycle, DaemonOwnership.Owned);
        var asked = 0;
        session.ConfirmForeignDaemonAction = _ => { asked++; return Task.FromResult(false); };

        await session.StopDaemonCommand.ExecuteAsync(null);

        Assert.Equal(0, asked);
        Assert.Equal(1, lifecycle.StopAllCount);
    }

    /// <summary>The case the old gate missed. When OTA can't read which binary answered — a daemon running
    /// as another user, say — it is neither owned nor foreign. Gating on the foreign flag skipped the
    /// confirmation exactly when OTA knew least about what it would kill.</summary>
    [Fact]
    public async Task Stop_AsksWhenItCannotTellWhoseDaemonThisIs()
    {
        using var session = NewSession(out var lifecycle, DaemonOwnership.Unknown);
        var asked = new List<string>();
        session.ConfirmForeignDaemonAction = verb => { asked.Add(verb); return Task.FromResult(false); };

        await session.StopDaemonCommand.ExecuteAsync(null);

        Assert.Equal(["stop"], asked);
        Assert.Equal(0, lifecycle.StopAllCount);
    }

    /// <summary>Restart's stop phase kills the foreign daemon and its start phase launches OUR build, so
    /// it swaps which daemon you are running — a bigger question than Stop's, not a smaller one.</summary>
    [Fact]
    public async Task Restart_AsksToo_AndSaysSoIsARestart()
    {
        using var session = NewSession(out var lifecycle, DaemonOwnership.External);
        var asked = new List<string>();
        session.ConfirmForeignDaemonAction = verb => { asked.Add(verb); return Task.FromResult(false); };

        await session.RestartDaemonCommand.ExecuteAsync(null);

        Assert.Equal(["restart"], asked);
        Assert.Equal(0, lifecycle.StopAllCount);
        Assert.False(session.HasDaemonOperationError);     // declining is not a failure
    }

    /// <summary>No hook wired (tests, and the window before the shell attaches one) must not mean "always
    /// refuse" — a service that cannot ask has to fall through, or Stop breaks wherever there is no UI.</summary>
    [Fact]
    public async Task NoConfirmHook_StillStops()
    {
        using var session = NewSession(out var lifecycle, DaemonOwnership.External);

        await session.StopDaemonCommand.ExecuteAsync(null);

        Assert.Equal(1, lifecycle.StopAllCount);
    }
    // --- The tri-state itself (#742) ---

    /// <summary>The two booleans are views of one value, so they can no longer disagree — or, worse,
    /// both read false and be taken for "ours" by every write guard.</summary>
    [Theory]
    [InlineData(DaemonOwnership.Owned, true, false)]
    [InlineData(DaemonOwnership.External, false, true)]
    [InlineData(DaemonOwnership.Unknown, false, false)]
    public void TheDerivedFlagsMatchTheOwnership(DaemonOwnership ownership, bool owned, bool foreign)
    {
        using var session = NewSession(out _, ownership);

        Assert.Equal(owned, session.IsAppOwnedDaemon);
        Assert.Equal(foreign, session.IsForeignDaemon);
    }

    /// <summary>The distinction the whole issue turns on, stated once as an executable claim.</summary>
    [Fact]
    public void NotForeignDoesNotMeanOwned()
    {
        using var session = NewSession(out _, DaemonOwnership.Unknown);

        Assert.False(session.IsForeignDaemon);
        Assert.False(session.IsAppOwnedDaemon);
    }
}
