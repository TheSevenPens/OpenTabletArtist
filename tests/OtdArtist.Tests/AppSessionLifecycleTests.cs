using OpenTabletDriver.Desktop;
using OtdArtist.Services;
using Xunit;

namespace OtdArtist.Tests;

/// <summary>
/// Guards the daemon Stop/Start auto-reconnect gate. A user-initiated Stop must suppress the
/// client's automatic reconnect (otherwise it spins trying to reach the daemon it just killed
/// and races the subsequent Start); Start/Restart must re-enable it.
/// </summary>
public class AppSessionLifecycleTests
{
    private sealed class FakeLifecycle : IDaemonLifecycleService
    {
        public int StopAllCount { get; private set; }
        public int LaunchCount { get; private set; }
        public string? ExpectedExePath() => null;
        public string? FindExe() => null;            // no real daemon — Launch becomes a no-op
        public bool IsRunning() => false;
        public void Launch() => LaunchCount++;
        public void StopAll() => StopAllCount++;
        public string? GetProcessPath(int processId) => null;
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
}
