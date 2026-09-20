using System;
using System.Diagnostics;

namespace OpenTabletArtist.Services;

/// <summary>
/// Cross-platform shell integrations that used to be hardcoded to Windows (#140). Each is best-effort —
/// it never throws, so a missing handler off-Windows degrades to a no-op instead of a
/// <see cref="System.ComponentModel.Win32Exception"/> (the bug: several "open folder" commands called
/// <c>Process.Start("explorer.exe", …)</c> unguarded, which threw on macOS). The launcher name per OS is a
/// pure, unit-tested helper; the path is passed via <see cref="ProcessStartInfo.ArgumentList"/> so it's
/// escaped correctly (spaces, quotes) without manual quoting.
/// </summary>
///
/// <remarks>
/// Every public entry here has an <c>internal</c> twin that takes the launch as a parameter, and
/// <see cref="Start"/> is the only place a process is actually started (#885). The tests use those twins.
/// <para>
/// This is not tidiness. The test for "a bogus path degrades to a no-op" ran <c>explorer.exe</c> against a
/// path that does not exist, and Explorer answers an unparseable argument by opening its <em>default</em>
/// folder — so a suite run left a window on the developer's desktop, and the assertion that the call
/// "never throws" passed while the call was doing something the comment said it would not do. Off-Windows
/// the same pair opened Finder and System Settings. CI never saw any of it: Linux runners have no
/// <c>xdg-open</c> handler and macOS runners are headless, so it was paid for entirely by whoever was at a
/// desk.
/// </para>
/// <para>
/// The seam also reaches what the real launcher hid. A launcher that <em>throws</em> is what the
/// best-effort contract is about, and it could not be provoked before; nor could the escaping claim above
/// be checked, since nothing could see the <see cref="ProcessStartInfo"/> that was built.
/// </para>
/// </remarks>
public static class PlatformShell
{
    /// <summary>Open a folder in the OS file manager — Explorer (Windows), Finder (macOS), the freedesktop
    /// handler (Linux). Best-effort; the caller is expected to check the path exists first.</summary>
    public static void RevealInFileManager(string path) => Reveal(path, Start);

    /// <summary>Open the OS display-settings pane. Windows: <c>ms-settings:display</c>; macOS: the Displays
    /// pane in System Settings; elsewhere a no-op. Best-effort.</summary>
    public static void OpenDisplaySettings() => OpenDisplaySettings(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        OperatingSystem.IsLinux(),
        Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
        Start);

    /// <summary>The file-manager launcher for an OS: Explorer (Windows), <c>open</c> → Finder (macOS),
    /// <c>xdg-open</c> (Linux). Pure — unit-tested.</summary>
    public static string FileManagerExe(bool isWindows, bool isMacOS)
        => isWindows ? "explorer.exe" : isMacOS ? "open" : "xdg-open";

    /// <summary>
    /// <see cref="RevealInFileManager"/> with the launch supplied, so a test can watch what would be run.
    /// </summary>
    internal static void Reveal(string path, Action<ProcessStartInfo> start)
        => Launch(FileManagerExe(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()), path, start);

    /// <summary>
    /// <see cref="OpenDisplaySettings()"/> with the OS, the desktop and the launch supplied, so every
    /// branch is reachable from a test and none of them starts anything.
    /// </summary>
    /// <remarks>
    /// The desktop is passed in rather than read, which is the convention
    /// <see cref="DesktopTrayEnvironment.IsGnomeDesktop"/> set for the same reason (#610): a test that has
    /// to set <c>XDG_CURRENT_DESKTOP</c> is mutating process-wide state while the rest of the suite runs
    /// beside it.
    /// </remarks>
    internal static void OpenDisplaySettings(
        bool isWindows, bool isMacOS, bool isLinux, string? desktop, Action<ProcessStartInfo> start)
    {
        try
        {
            if (isWindows)
                start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
            else if (isMacOS)
                Launch("open", "x-apple.systempreferences:com.apple.preference.displays", start);
            else if (isLinux)
                OpenLinuxDisplaySettings(desktop, start);
        }
        catch { /* best-effort */ }
    }

    private static void OpenLinuxDisplaySettings(string? desktop, Action<ProcessStartInfo> start)
    {
        desktop ??= "";
        if (desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase))
            Launch("gnome-control-center", "display", start);
        else if (desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase))
            Launch("systemsettings", "kcm_kscreen", start);
        else
            Launch("xdg-open", "x-settings:display", start); // best-effort freedesktop fallback
    }

    private static void Launch(string exe, string arg, Action<ProcessStartInfo> start)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add(arg);   // ArgumentList escapes spaces/quotes — no manual quoting needed
            start(psi);
        }
        catch { /* best-effort: no handler / not supported on this OS */ }
    }

    /// <summary>The real launch. The only place in this class that starts a process.</summary>
    private static void Start(ProcessStartInfo psi) => Process.Start(psi);
}
