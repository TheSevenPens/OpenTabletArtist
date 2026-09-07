using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Owns the OTD daemon process lifecycle: locating the built exe, launching, stopping,
/// and resolving process paths. Extracted from <c>MainViewModel</c> so the process/
/// filesystem seam is isolated behind an interface (it can be faked in view-model tests,
/// and the daemon-process concerns live in one place).
/// </summary>
public interface IDaemonLifecycleService
{
    /// <summary>The daemon exe shipped with / built by this project — the bundled copy next to a
    /// published app, or the submodule build output in dev. Null if none is present.</summary>
    string? ExpectedExePath();

    /// <summary>Daemon exe to launch: the expected build, falling back to a running instance's path. Null if none found.</summary>
    string? FindExe();

    /// <summary>True if any OTD daemon process is currently running.</summary>
    bool IsRunning();

    /// <summary>Launches the daemon with no window, if an exe can be found. No-op otherwise.</summary>
    void Launch();

    /// <summary>
    /// Kills ONE daemon process by id (best effort) — the one we are connected to. Prefer this over
    /// <see cref="StopAll"/> wherever the pid is known: Stop should stop the daemon this app is talking
    /// to, not every process that happens to share its name (#601).
    /// </summary>
    /// <returns>True if the process is gone (killed, or already not running); false if it could not be
    /// stopped, or if the pid turned out not to be a daemon at all.</returns>
    bool Stop(int processId);

    /// <summary>Kills all running OTD daemon processes (best effort). The fallback for when the connected
    /// daemon's pid isn't known — off-Windows, where the pipe→pid lookup is unavailable and the daemon is
    /// effectively a singleton anyway.</summary>
    void StopAll();

    /// <summary>Full executable path for a process id, or null if it can't be read (e.g. elevated).</summary>
    string? GetProcessPath(int processId);

    /// <summary>The single running daemon's executable path, or null if none — or more than one — is
    /// running. A macOS/Linux fallback for when the Win32 pipe→PID lookup is unavailable: the daemon is
    /// effectively a singleton there, so an unambiguous single match is the one we're connected to. The
    /// count guard means it never misattributes when several daemons are somehow present. (#140)</summary>
    string? GetSingleRunningDaemonPath();
}

/// <inheritdoc />
public class DaemonLifecycleService : IDaemonLifecycleService
{
    private const string ProcessName = "OpenTabletDriver.Daemon";

    public string? ExpectedExePath() =>
        // Bundled-next-to-app (release) first, then the dev build tree. See DaemonExePaths.
        DaemonExePaths.Candidates(AppContext.BaseDirectory).FirstOrDefault(File.Exists);

    public string? FindExe()
    {
        var expected = ExpectedExePath();
        if (expected != null) return expected;

        // Fallback: if a daemon is already running, use its path.
        foreach (var proc in Process.GetProcessesByName(ProcessName))
        {
            try { var p = proc.MainModule?.FileName; if (p != null) return p; }
            catch (Exception ex) { AppLog.Debug($"Couldn't read a running daemon's exe path (pid {proc.Id}).", ex); }
        }
        return null;
    }

    public bool IsRunning() => Process.GetProcessesByName(ProcessName).Length > 0;

    public void Launch()
    {
        var daemonPath = FindExe();
        if (daemonPath == null) return;

        Process.Start(new ProcessStartInfo(daemonPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(daemonPath) ?? "",
        });
    }

    public bool Stop(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            // Process ids get recycled. Between the pipe→pid lookup and this call the daemon may have
            // exited and something else inherited its number, so confirm what we are about to kill really
            // is a daemon. Without this, a stale pid turns "Stop the daemon" into "kill an arbitrary
            // process", which is worse than the kill-by-name behaviour this replaces.
            if (!string.Equals(proc.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Warn($"Not stopping pid {processId}: it is \"{proc.ProcessName}\", not {ProcessName}.");
                return false;
            }
            proc.Kill();
            return true;
        }
        catch (ArgumentException)
        {
            // No process with that id — it has already exited, which is the outcome Stop wanted.
            return true;
        }
        catch (Exception ex)
        {
            // A failed Kill means the Stop didn't fully take — worth a Warn, not a silent no-op (#21).
            AppLog.Warn($"Couldn't stop daemon process (pid {processId}).", ex);
            return false;
        }
    }

    public void StopAll()
    {
        foreach (var proc in Process.GetProcessesByName(ProcessName))
        {
            // A failed Kill means the Stop didn't fully take — worth a Warn, not a silent no-op (#21).
            try { proc.Kill(); }
            catch (Exception ex) { AppLog.Warn($"Couldn't stop daemon process (pid {proc.Id}).", ex); }
        }
    }

    public string? GetProcessPath(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            // Reading another process's module path can fail (e.g. an elevated daemon) — expected, Debug (#21).
            AppLog.Debug($"Couldn't read daemon exe path for pid {processId}.", ex);
            return null;
        }
    }

    public string? GetSingleRunningDaemonPath()
    {
        var procs = Process.GetProcessesByName(ProcessName);
        try
        {
            // Only when unambiguous — with multiple daemons we can't tell which the pipe connects to.
            return procs.Length == 1 ? procs[0].MainModule?.FileName : null;
        }
        catch (Exception ex)
        {
            AppLog.Debug("Couldn't read the running daemon's exe path.", ex);
            return null;
        }
        finally { foreach (var p in procs) p.Dispose(); }
    }
}
