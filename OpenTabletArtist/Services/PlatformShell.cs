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

    /// <summary>Run an application OTA has already located. Best-effort; a missing file is a no-op.</summary>
    public static void RunApp(string? exePath) => RunApp(exePath, Start);

    /// <summary>
    /// <see cref="RunApp(string?)"/> with the launch supplied, so a test can observe what was started.
    /// </summary>
    /// <remarks>
    /// <c>UseShellExecute</c> is on, unlike the file-manager launches: this starts a GUI application in
    /// its own right rather than handing an argument to a known tool, and on Windows that is what gives
    /// it a normal window and working directory instead of inheriting OTA's.
    /// </remarks>
    internal static void RunApp(string? exePath, Action<ProcessStartInfo> start)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;

        Launch(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
        }, start);
    }

    /// <summary>Open the OS display-settings pane. Windows: <c>ms-settings:display</c>; macOS: the Displays
    /// pane in System Settings; elsewhere a no-op. Best-effort.</summary>
    public static void OpenDisplaySettings() => OpenDisplaySettings(
        isWindows: OperatingSystem.IsWindows(),
        isMacOS: OperatingSystem.IsMacOS(),
        isLinux: OperatingSystem.IsLinux(),
        desktop: Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
        start: Start);

    /// <summary>
    /// Open an <c>https://</c> link in whatever the user browses with. Best-effort; anything else is a
    /// no-op.
    /// </summary>
    /// <remarks>
    /// <b>Only https</b>, and that is a refusal rather than a formality (#889). This is reached from the
    /// driver-cleanup card, whose links come out of a <em>detection</em> — text this application did not
    /// author — and <see cref="ProcessStartInfo.UseShellExecute"/> hands whatever it is given to the
    /// registered handler for its scheme. A <c>file:</c> or an arbitrary custom scheme is exactly what
    /// should not reach that. Callers with a narrower rule keep it: the cleanup card also requires the
    /// OTD domain, which this deliberately does not know about.
    /// </remarks>
    public static void OpenUrl(string? url) => OpenUrl(url, Start);

    /// <summary>
    /// Open the macOS Input Monitoring pane. macOS-only; a no-op elsewhere. Best-effort.
    /// </summary>
    /// <remarks>
    /// Its own operation rather than a URL passed to <see cref="OpenUrl"/>: it names a settings pane, the
    /// way <see cref="OpenDisplaySettings()"/> does, and it is reached by a scheme <see cref="OpenUrl"/>
    /// refuses on purpose. Folding the two together would mean either widening that refusal or describing
    /// a settings pane as a link.
    /// </remarks>
    public static void OpenInputMonitoringSettings()
        => OpenInputMonitoringSettings(OperatingSystem.IsMacOS(), Start);

    /// <summary>Whether a string is a link this will open. Pure — unit-tested.</summary>
    public static bool IsWebLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && parsed.Scheme == Uri.UriSchemeHttps;

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
    /// <see cref="OpenUrl(string?)"/> with the launch supplied, so the refusal can be observed.
    /// </summary>
    internal static void OpenUrl(string? url, Action<ProcessStartInfo> start)
    {
        if (!IsWebLink(url))
        {
            // A refusal is still a click that did nothing, so it says so. Only when something was
            // actually offered: a command invoked with no link at all is not an event.
            if (!string.IsNullOrWhiteSpace(url))
                AppLog.Warn($"Refused to open \"{url}\": only https links are opened.");
            return;
        }

        // A browser is reached through the registered handler for the scheme, so this one is shell-execute
        // by necessity -- which is also why the check above is not optional.
        Launch(new ProcessStartInfo(url!) { UseShellExecute = true }, start);
    }

    /// <summary>
    /// <see cref="OpenInputMonitoringSettings()"/> with the OS and the launch supplied.
    /// </summary>
    /// <remarks>
    /// The mechanism is the one this call already used before it moved here: the URI goes to the shell
    /// rather than to <c>open</c> as an argument, which is how <see cref="OpenDisplaySettings()"/> asks
    /// for its pane. Both work; they are left as they were because macOS is not verifiable from the
    /// supported platform, and unifying them blind would be changing a shipped path on a guess.
    /// </remarks>
    internal static void OpenInputMonitoringSettings(bool isMacOS, Action<ProcessStartInfo> start)
    {
        if (!isMacOS) return;

        Launch(
            new ProcessStartInfo("x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent")
            {
                UseShellExecute = true,
            },
            start);
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
