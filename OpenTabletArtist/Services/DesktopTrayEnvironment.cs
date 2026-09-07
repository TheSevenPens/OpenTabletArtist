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

    private static bool IsGnome() =>
        IsGnomeDesktop(
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"),
            Environment.GetEnvironmentVariable("DESKTOP_SESSION"));

    /// <summary>
    /// Whether these three desktop-identifying variables describe a GNOME session — the pure half of the
    /// probe, taking the values rather than reading them, so it is testable without mutating the process
    /// environment (#610).
    /// <para>
    /// XDG_CURRENT_DESKTOP may be colon-separated (e.g. "ubuntu:GNOME"), so this matches as a substring
    /// rather than an equality, and falls back to the session/desktop variables for logins that leave the
    /// primary one unset.
    /// </para>
    /// </summary>
    public static bool IsGnomeDesktop(string? currentDesktop, string? sessionDesktop, string? desktopSession)
    {
        foreach (var value in new[] { currentDesktop, sessionDesktop, desktopSession })
            if (!string.IsNullOrEmpty(value) && value.Contains("GNOME", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// How a finished <c>gdbus NameHasOwner</c> call is read: it prints "(true,)" when something owns the
    /// StatusNotifierWatcher name and "(false,)" when nothing does.
    /// <para>
    /// Only an explicit "false" reports the host ABSENT. A non-zero exit, or output in neither form, means
    /// we did not get an answer — and this type's contract is that an undetermined result never nags, so
    /// those report present. This used to be <c>exitCode != 0 || output.Contains("true")</c>, which read
    /// an unrecognised line on a successful exit as "no host" and would have hinted on the strength of
    /// output it had failed to understand (#610).
    /// </para>
    /// </summary>
    public static bool HostPresentFromProbe(int exitCode, string output)
    {
        if (exitCode != 0) return true;                                          // the call itself failed
        if (output.Contains("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (output.Contains("false", StringComparison.OrdinalIgnoreCase)) return false;
        return true;                                                             // unreadable → don't nag
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
            return HostPresentFromProbe(proc.ExitCode, output);
        }
        catch { return true; }
    }
}
