using System;
using System.Diagnostics;
using System.IO;

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
/// <para>
/// <b>What the tests do and do not establish (#887).</b> They cover the request that gets built and what
/// happens when a launch fails. They do <em>not</em> cover the two public wrappers, which bind the real OS,
/// the real environment and the real launcher: nothing asserts that those arguments are passed in the
/// right positions. That binding is checked by reading it and by the manual smoke test, and the wrappers
/// are kept to one call each, with named arguments, so a transposition is visible on the page. A unit
/// suite that covered them would have to launch something, which is the problem this solved.
/// </para>
/// </remarks>
public static class PlatformShell
{
    /// <summary>Open a folder in the OS file manager — Explorer (Windows), Finder (macOS), the freedesktop
    /// handler (Linux). Best-effort; a folder that is not there is a no-op.</summary>
    public static void RevealInFileManager(string? path) => Reveal(path, Start);

    /// <summary>Open the OS display-settings pane. Windows: <c>ms-settings:display</c>; macOS: the Displays
    /// pane in System Settings; elsewhere a no-op. Best-effort.</summary>
    public static void OpenDisplaySettings() => OpenDisplaySettings(
        isWindows: OperatingSystem.IsWindows(),
        isMacOS: OperatingSystem.IsMacOS(),
        isLinux: OperatingSystem.IsLinux(),
        desktop: Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
        start: Start);

    /// <summary>The file-manager launcher for an OS: Explorer (Windows), <c>open</c> → Finder (macOS),
    /// <c>xdg-open</c> (Linux). Pure — unit-tested.</summary>
    public static string FileManagerExe(bool isWindows, bool isMacOS)
        => isWindows ? "explorer.exe" : isMacOS ? "open" : "xdg-open";

    /// <summary>
    /// <see cref="RevealInFileManager"/> with the launch supplied, so a test can watch what would be run.
    /// </summary>
    /// <remarks>
    /// The folder has to exist before anything is launched, and that check lives here rather than in each
    /// caller (#887). Explorer answers an argument it cannot resolve by opening its <em>default</em>
    /// folder, so "reveal a folder that is gone" is not a no-op on Windows — it is a window pointed
    /// somewhere the user did not ask for. Every caller was already paying for this check, so it costs no
    /// extra filesystem hit; it is also inherently racy (the folder can go between the check and the
    /// launch), which is why the failure handling below stays.
    /// </remarks>
    internal static void Reveal(string? path, Action<ProcessStartInfo> start)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        Launch(FileManagerExe(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()), path, start);
    }

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
        if (isWindows)
            // UseShellExecute is load-bearing, not a default: a ms-settings: URI reaches the Settings app
            // through its registered protocol handler, and there is no executable to run without it.
            Launch(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }, start);
        else if (isMacOS)
            Launch("open", "x-apple.systempreferences:com.apple.preference.displays", start);
        else if (isLinux)
            OpenLinuxDisplaySettings(desktop, start);
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
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add(arg);   // ArgumentList escapes spaces/quotes — no manual quoting needed
        Launch(psi, start);
    }

    /// <summary>
    /// The one place a launch is attempted, and the one place a failed one is reported.
    /// </summary>
    /// <remarks>
    /// Still best-effort: a desktop with no handler is not worth interrupting anyone over, and these are
    /// optional "open this for me" actions, so a malformed argument should not take down the command
    /// either. But a click that silently did nothing should leave evidence — one line, naming what was
    /// asked for, in the log a user is asked to attach (#887). Narrowing the catch instead would have
    /// changed which failures reach the UI, which is a different decision from making them visible.
    /// </remarks>
    private static void Launch(ProcessStartInfo psi, Action<ProcessStartInfo> start)
    {
        try
        {
            start(psi);
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                psi.ArgumentList.Count > 0
                    ? $"Couldn't ask {psi.FileName} to open \"{psi.ArgumentList[0]}\"; nothing was opened."
                    : $"Couldn't open {psi.FileName}; nothing was opened.",
                ex);
        }
    }

    /// <summary>The real launch. The only place in this class that starts a process.</summary>
    private static void Start(ProcessStartInfo psi) => Process.Start(psi);
}
