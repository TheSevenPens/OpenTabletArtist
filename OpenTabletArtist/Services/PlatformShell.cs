using System;
using System.Diagnostics;

namespace OpenTabletArtist.Services;

/// <summary>
/// Cross-platform shell integrations that used to be hardcoded to Windows (#140). Each is best-effort —
/// it never throws, so a missing handler off-Windows degrades to a no-op instead of a
/// <see cref="System.ComponentModel.Win32Exception"/> (the bug: several "open folder" commands called
/// <c>Process.Start("explorer.exe", …)</c> unguarded, which threw on macOS). The OS→command mapping is a
/// pure, unit-tested helper.
/// </summary>
public static class PlatformShell
{
    /// <summary>Open a folder in the OS file manager — Explorer (Windows), Finder (macOS), the freedesktop
    /// handler (Linux). Best-effort; the caller is expected to check the path exists first.</summary>
    public static void RevealInFileManager(string path)
    {
        var (exe, args) = FileManagerCommand(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), path);
        Launch(exe, args, shellExecute: false);
    }

    /// <summary>Open the OS display-settings pane. Windows: <c>ms-settings:display</c>; macOS: the Displays
    /// pane in System Settings; elsewhere a no-op. Best-effort.</summary>
    public static void OpenDisplaySettings()
    {
        if (OperatingSystem.IsWindows())
            Launch("ms-settings:display", "", shellExecute: true);           // URI scheme → needs the shell
        else if (OperatingSystem.IsMacOS())
            Launch("open", "\"x-apple.systempreferences:com.apple.preference.displays\"", shellExecute: false);
        // Linux: no portable display-settings target — no-op.
    }

    /// <summary>Pure OS→file-manager-command mapping (unit-tested). Path is quoted so spaces are safe.</summary>
    public static (string exe, string args) FileManagerCommand(bool isWindows, bool isMacOS, string path)
    {
        var quoted = $"\"{path}\"";
        if (isWindows) return ("explorer.exe", quoted);
        if (isMacOS) return ("open", quoted);
        return ("xdg-open", quoted);
    }

    private static void Launch(string exe, string args, bool shellExecute)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = shellExecute });
        }
        catch { /* best-effort: no handler / not supported on this OS */ }
    }
}
