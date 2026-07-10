using OpenTabletDriver.Desktop;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Guards the daemon Stop/Start auto-reconnect gate. A user-initiated Stop must suppress the
/// client's automatic reconnect (otherwise it spins trying to reach the daemon it just killed
/// and races the subsequent Start); Start/Restart must re-enable it.
/// </summary>
public class AppSessionLifecycleTests
{
    // The daemon exe is "present" (so the session treats it as reachable and goes through the normal
    // launch/connect path), but Launch is a counted no-op and nothing ever connects — so the
    // auto-reconnect gate + timeout behavior is what's exercised, not the missing-exe short-circuit.
    private sealed class FakeLifecycle : IDaemonLifecycleService
    {
        public int StopAllCount { get; private set; }
        public int LaunchCount { get; private set; }
        public string? ExpectedExePath() => "fake-daemon.exe"; // present → reachable
        public string? FindExe() => "fake-daemon.exe";
        public bool IsRunning() => false;
        public void Launch() => LaunchCount++;                 // no real process — never connects
        public void StopAll() => StopAllCount++;
        public string? GetProcessPath(int processId) => null;
        public string? GetSingleRunningDaemonPath() => null;
    }

    // No exe present and nothing running → the missing-exe short-circuit should fire.
    private sealed class MissingLifecycle : IDaemonLifecycleService
    {
        public int StopAllCount { get; private set; }
        public int LaunchCount { get; private set; }
        public string? ExpectedExePath() => null;
        public string? FindExe() => null;
        public bool IsRunning() => false;
        public void Launch() => LaunchCount++;
        public void StopAll() => StopAllCount++;
        public string? GetProcessPath(int processId) => null;
        public string? GetSingleRunningDaemonPath() => null;
    }

    private sealed class FakeSettingsStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }
        public bool TrySave(Settings settings, string path) => true;
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    private static AppSession NewSession(out FakeLifecycle lifecycle)
    {
        lifecycle = new FakeLifecycle();
        return new AppSession(new DaemonClient(), lifecycle, new FakeSettingsStore())
        {
            // No real daemon in tests, so Start/Restart never connect — keep the timeout tiny
            // so the "didn't come online" path is exercised in milliseconds, not 30s.
            DaemonOperationTimeout = TimeSpan.FromMilliseconds(150),
        };
    }

    [Fact]
    public async Task StopDaemon_SuppressesAutoReconnect_AndStopsTheProcess()
    {
        using var session = NewSession(out var lifecycle);
        Assert.True(session.Daemon.AutoReconnect); // default

        // Already disconnected, so the "wait for drop" completes immediately (success, no error).
        await session.StopDaemonCommand.ExecuteAsync(null);

        Assert.False(session.Daemon.AutoReconnect);
        Assert.Equal(1, lifecycle.StopAllCount);
        Assert.False(session.IsDaemonBusy);
        Assert.False(session.HasDaemonOperationError);
    }

    [Fact]
    public async Task StartDaemon_ReenablesAutoReconnect_AndReportsTimeout()
    {
        using var session = NewSession(out _);
        session.Daemon.AutoReconnect = false; // as if a prior Stop left it off

        // FindExe() returns null so Launch is a no-op and IsConnected stays false; the command
        // flips the gate back on, then times out waiting for a connection that never comes.
        await session.StartDaemonCommand.ExecuteAsync(null);

        Assert.True(session.Daemon.AutoReconnect);
        Assert.False(session.IsDaemonBusy);            // cleared in finally
        Assert.True(session.HasDaemonOperationError);  // timed out (no daemon)
    }

    [Fact]
    public async Task RestartDaemon_ReenablesAutoReconnect_AndReportsTimeout()
    {
        using var session = NewSession(out var lifecycle);
        session.Daemon.AutoReconnect = false;

        await session.RestartDaemonCommand.ExecuteAsync(null);

        Assert.True(session.Daemon.AutoReconnect);
        Assert.Equal(1, lifecycle.StopAllCount);
        Assert.False(session.IsDaemonBusy);
        Assert.True(session.HasDaemonOperationError);
    }

    [Fact]
    public async Task LifecycleCommand_ClearsPreviousError_OnRerun()
    {
        using var session = NewSession(out _);

        await session.StartDaemonCommand.ExecuteAsync(null);
        Assert.True(session.HasDaemonOperationError); // first run times out

        // A Stop succeeds (already disconnected) and must clear the stale error.
        await session.StopDaemonCommand.ExecuteAsync(null);
        Assert.False(session.HasDaemonOperationError);
    }

    // --- Daemon-exe-missing short-circuit (checked before any connect attempt) ---

    private static AppSession NewMissingSession(out MissingLifecycle lifecycle)
    {
        lifecycle = new MissingLifecycle();
        return new AppSession(new DaemonClient(), lifecycle, new FakeSettingsStore())
        {
            DaemonOperationTimeout = TimeSpan.FromMilliseconds(150),
        };
    }

    [Fact]
    public async Task StartAndConnect_WhenExeMissing_FlagsMissing_AndDoesNotLaunchOrConnect()
    {
        using var session = NewMissingSession(out var lifecycle);

        await session.StartAndConnectAsync();

        Assert.True(session.IsDaemonExeMissing);
        Assert.True(session.HasDaemonOperationError);
        Assert.Equal(AppSession.DaemonExeMissingMessage, session.DaemonOperationError);
        Assert.Equal(0, lifecycle.LaunchCount); // no point launching a nonexistent exe
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task StartDaemon_WhenExeMissing_FlagsMissing_AndDoesNotLaunch()
    {
        using var session = NewMissingSession(out var lifecycle);

        await session.StartDaemonCommand.ExecuteAsync(null);

        Assert.True(session.IsDaemonExeMissing);
        Assert.True(session.HasDaemonOperationError);
        Assert.Equal(0, lifecycle.LaunchCount);
        Assert.False(session.IsDaemonBusy); // cleared in finally
    }

    [Fact]
    public async Task RestartDaemon_WhenExeMissing_DoesNotStopTheRunningDaemon()
    {
        using var session = NewMissingSession(out var lifecycle);

        await session.RestartDaemonCommand.ExecuteAsync(null);

        Assert.True(session.IsDaemonExeMissing);
        // Must not kill a daemon it can't relaunch.
        Assert.Equal(0, lifecycle.StopAllCount);
        Assert.False(session.IsDaemonBusy);
    }
}
