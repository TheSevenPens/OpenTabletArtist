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
    public string? Launch(string? executablePath = null)
    { Launched = executablePath; LaunchAction?.Invoke(); return null; }
    public bool Stop(int processId)
    {
        if (StopAction is null) throw new InvalidOperationException("Stop was not approved.");
        StopAction();
        return true;
    }
    public void StopAll() => throw new InvalidOperationException("Stop was not approved.");
    public string? PathOf(int processId) => "daemon.exe";
    public string? SingleRunningDaemonPath() => "daemon.exe";
}
