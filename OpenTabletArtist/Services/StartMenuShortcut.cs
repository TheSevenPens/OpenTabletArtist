using System;
using System.Diagnostics;
using System.IO;

namespace OpenTabletArtist.Services;

/// <summary>
/// Creates a Start-menu shortcut (.lnk) pointing at the running OpenTabletArtist executable. A dev build
/// run straight from its output folder isn't a "registered" app, so tooling that keys off the installed-app
/// list (e.g. the desktop-automation access grant used for UI screenshots) can't see it. Dropping a
/// shortcut into the per-user Start-menu Programs folder registers it under its display name, which is
/// enough. Windows-only (uses the WScript.Shell COM object, bound late so there's no build-time reference).
/// </summary>
public static class StartMenuShortcut
{
    /// <summary>The per-user Start-menu path the shortcut is written to.</summary>
    public static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "OpenTabletArtist.lnk");

    /// <summary>True if the shortcut already exists.</summary>
    public static bool Exists => File.Exists(ShortcutPath);

    /// <summary>Create (or overwrite) the shortcut, pointing at the current process's exe.</summary>
    /// <returns>true on success; false with <paramref name="error"/> set on failure.</returns>
    public static bool TryCreate(out string path, out string? error)
    {
        path = ShortcutPath;
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "Start-menu shortcuts are only supported on Windows.";
            return false;
        }
        try
        {
            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe))
            {
                error = "Couldn't determine the application's executable path.";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                error = "Shortcut creation isn't available on this platform.";
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.Description = "OpenTabletArtist";
            shortcut.Save();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
