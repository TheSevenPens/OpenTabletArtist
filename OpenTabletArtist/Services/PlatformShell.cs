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
public static class PlatformShell
{
    /// <summary>Open a folder in the OS file manager — Explorer (Windows), Finder (macOS), the freedesktop
    /// handler (Linux). Best-effort; the caller is expected to check the path exists first.</summary>
    public static void RevealInFileManager(string path)
        => Launch(FileManagerExe(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()), path);

    /// <summary>Open the OS display-settings pane. Windows: <c>ms-settings:display</c>; macOS: the Displays
    /// pane in System Settings; elsewhere a no-op. Best-effort.</summary>
    public static void OpenDisplaySettings()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Launch("open", "x-apple.systempreferences:com.apple.preference.displays");
            // Linux: no portable display-settings target — no-op.
        }
        catch { /* best-effort */ }
    }

    /// <summary>The file-manager launcher for an OS: Explorer (Windows), <c>open</c> → Finder (macOS),
    /// <c>xdg-open</c> (Linux). Pure — unit-tested.</summary>
    public static string FileManagerExe(bool isWindows, bool isMacOS)
        => isWindows ? "explorer.exe" : isMacOS ? "open" : "xdg-open";

    private static void Launch(string exe, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add(arg);   // ArgumentList escapes spaces/quotes — no manual quoting needed
            Process.Start(psi);
        }
        catch { /* best-effort: no handler / not supported on this OS */ }
    }
}
