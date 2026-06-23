using System.Diagnostics;
using System.IO;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Owns the OTD daemon process lifecycle: locating the built exe, launching, stopping,
/// and resolving process paths. Extracted from <c>MainViewModel</c> so the process/
/// filesystem seam is isolated behind an interface (it can be faked in view-model tests,
/// and the daemon-process concerns live in one place).
/// </summary>
public interface IDaemonLifecycleService
{
    /// <summary>The daemon exe this project builds from the submodule (build output only), or null if not built.</summary>
    string? ExpectedExePath();

    /// <summary>Daemon exe to launch: the expected build, falling back to a running instance's path. Null if none found.</summary>
    string? FindExe();

    /// <summary>True if any OTD daemon process is currently running.</summary>
    bool IsRunning();

    /// <summary>Launches the daemon with no window, if an exe can be found. No-op otherwise.</summary>
    void Launch();

    /// <summary>Kills all running OTD daemon processes (best effort).</summary>
    void StopAll();

    /// <summary>Full executable path for a process id, or null if it can't be read (e.g. elevated).</summary>
    string? GetProcessPath(int processId);
}

/// <inheritdoc />
public class DaemonLifecycleService : IDaemonLifecycleService
{
    private const string ProcessName = "OpenTabletDriver.Daemon";

    public string? ExpectedExePath()
    {
        // Look relative to our exe: up to repo root, then into the daemon build output.
        var baseDir = AppContext.BaseDirectory;
        foreach (var config in new[] { "Debug", "Release" })
        {
            var candidate = Path.GetFullPath(Path.Combine(
                baseDir, "..", "..", "..", "..",
                "external", "OpenTabletDriver", "OpenTabletDriver.Daemon",
                "bin", config, "net8.0", "OpenTabletDriver.Daemon.exe"));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public string? FindExe()
    {
        var expected = ExpectedExePath();
        if (expected != null) return expected;

        // Fallback: if a daemon is already running, use its path.
        foreach (var proc in Process.GetProcessesByName(ProcessName))
        {
            try { var p = proc.MainModule?.FileName; if (p != null) return p; } catch { }
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

    public void StopAll()
    {
        foreach (var proc in Process.GetProcessesByName(ProcessName))
        {
            try { proc.Kill(); } catch { }
        }
    }

    public string? GetProcessPath(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.MainModule?.FileName;
        }
        catch { return null; }
    }
}
