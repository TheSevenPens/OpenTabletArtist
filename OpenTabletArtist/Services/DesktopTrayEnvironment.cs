using System;
using System.Diagnostics;

namespace OpenTabletArtist.Services;

/// <summary>
/// Detects whether the running desktop can actually render an app tray icon. On Linux, Avalonia publishes
/// the tray icon through the freedesktop StatusNotifierItem (SNI) protocol; GNOME Shell ships no built-in
/// SNI host, so without the AppIndicator extension the icon is published to the session bus but never
/// shown. This probes that situation so the health catalog can hint at it (Linux/GNOME only). Windows and
/// macOS render the tray natively and always report it available.
/// </summary>
public static class DesktopTrayEnvironment
{
    // The desktop + SNI-host state is effectively fixed for a session, and probing shells out to gdbus, so
    // resolve once and cache — the 3s health re-evaluation must not spawn a process every time.
    private static readonly Lazy<bool> _trayHostUnavailable = new(Probe);

    /// <summary>True only on a GNOME desktop with no StatusNotifierItem host on the session bus. False on
    /// Windows/macOS, on non-GNOME Linux, when a host is present (e.g. the AppIndicator extension), or
    /// whenever it can't be determined — so a false negative never nags.</summary>
    public static bool TrayHostUnavailable => _trayHostUnavailable.Value;

    private static bool Probe()
    {
        // Only Linux delivers the tray via SNI; Windows/macOS render it natively.
        if (!OperatingSystem.IsLinux()) return false;
        // GNOME is the desktop that ships no SNI host. Other DEs (KDE, XFCE with a plugin, …) provide one,
        // so a missing watcher there means something else and isn't ours to hint about.
        if (!IsGnome()) return false;
        return !StatusNotifierHostPresent();
    }

    private static bool IsGnome()
    {
        // XDG_CURRENT_DESKTOP may be colon-separated (e.g. "ubuntu:GNOME"), so match as a substring. Fall
        // back to the session/desktop vars for logins that leave the primary one unset.
        foreach (var name in new[] { "XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP", "DESKTOP_SESSION" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value) && value.Contains("GNOME", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool StatusNotifierHostPresent()
    {
        // Ask the session bus whether anything owns org.kde.StatusNotifierWatcher (the SNI host name).
        // gdbus ships with GLib and is present on GNOME systems; if it's missing or the call fails we
        // assume a host IS present (return true) so we never surface a false hint.
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("gdbus",
                "call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus " +
                "--method org.freedesktop.DBus.NameHasOwner org.kde.StatusNotifierWatcher")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (proc == null) return true;
            // WaitForExit before reading (output is tiny, so no full-pipe deadlock) to keep a real timeout —
            // a hung gdbus mustn't block health evaluation.
            if (!proc.WaitForExit(2000)) { try { proc.Kill(); } catch { /* best effort */ } return true; }
            var output = proc.StandardOutput.ReadToEnd();
            // Success prints "(true,)" when a host owns the name, "(false,)" otherwise.
            return proc.ExitCode != 0 || output.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }
}
