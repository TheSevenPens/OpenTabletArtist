using System;
using System.Diagnostics;

namespace OpenTabletArtist.Services;

/// <summary>
/// Looks up the running OpenTabletDriver daemon process — independent of OTA's IPC connection — for the
/// DAEMON PROCESS card: whether one is running, its executable path, and when it started (→ uptime).
/// Best-effort: any failure reports "not running". (.NET matches the full process name here even though
/// Linux truncates the kernel comm to 15 chars — verified against the packaged daemon.)
/// </summary>
public static class DaemonProcess
{
    private const string ProcessName = "OpenTabletDriver.Daemon";

    /// <summary>Snapshot of the daemon process, if one is running.</summary>
    public sealed record Info(bool Running, DateTime? StartTime, string? Path);

    public static Info Query()
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName(ProcessName); }
        catch { return new Info(false, null, null); }

        try
        {
            if (procs.Length == 0) return new Info(false, null, null);
            var p = procs[0]; // the daemon is effectively a singleton; first match is it
            DateTime? start = null;
            string? path = null;
            try { start = p.StartTime; } catch { /* may be unreadable for an elevated/foreign process */ }
            try { path = p.MainModule?.FileName; } catch { /* ditto */ }
            return new Info(true, start, path);
        }
        finally
        {
            foreach (var p in procs) { try { p.Dispose(); } catch { } }
        }
    }
}
