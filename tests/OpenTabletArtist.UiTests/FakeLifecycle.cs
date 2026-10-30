using System;
using System.Threading.Tasks;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.UiTests;

/// <summary>A daemon lifecycle that records what it was asked to launch and refuses unapproved stops.</summary>
/// <remarks>
/// Shared, because more than one journey needs a whole <see cref="AppSession"/> and the session needs
/// one of these to exist at all.
/// </remarks>
internal sealed class FakeLifecycle : IDaemonLifecycleService
{
    public string Expected { get; set; } = "daemon.exe";
    /// <summary>The version the fake daemon's executable reports. Null means the one this build ships with,
    /// which is what every journey that is not about the version rule wants (#1042).</summary>
    public string? Version { get; set; }
    public string? Launched { get; private set; }
    public Action? StopAction { get; set; }
    public Action? LaunchAction { get; set; }
    public string? ExpectedExePath() => Expected;
    public bool IsAppManaged(string? path) => true;
    public bool HasBundledDaemon() => false;
    public string? FindExe() => "daemon.exe";
    /// <summary>Whether a daemon process exists, which is the cheap question #912 turns on: settable,
    /// because "no daemon is running" is the state the refresh has to tell apart.</summary>
    public bool Running { get; set; } = true;
    public bool IsRunning() => Running;
    /// <summary>How many times a launch was asked for. <see cref="Launched"/> alone cannot tell a launch
    /// with no pinned path from no launch at all, since both read null.</summary>
    public int LaunchCount { get; private set; }
    public string? Launch(string? executablePath = null)
    { LaunchCount++; Launched = executablePath; LaunchAction?.Invoke(); return null; }
    public bool Stop(int processId)
    {
        if (StopAction is null) throw new InvalidOperationException("Stop was not approved.");
        StopAction();
        return true;
    }
    public void StopAll() => throw new InvalidOperationException("Stop was not approved.");
    public string? PathOf(int processId) => "daemon.exe";
    public string? SingleRunningDaemonPath() => "daemon.exe";
    public string ReadVersion(string executablePath) => Version ?? HealthService.ExpectedOtdVersion;
}
