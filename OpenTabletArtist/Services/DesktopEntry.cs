using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia.Platform;

namespace OpenTabletArtist.Services;

/// <summary>
/// The Linux counterpart to <see cref="StartMenuShortcut"/>: writes a freedesktop <c>.desktop</c> entry so
/// OpenTabletArtist shows up in the application menu / launcher (GNOME app grid, KDE menu, …) registered
/// under its display name. Same motivation as the Windows Start-menu shortcut — a dev build run from its
/// output folder isn't a registered app, so tooling keyed to the installed-app list (e.g. desktop-automation
/// / screenshot grants) can't find it; the entry registers it. Kept deliberately separate from the Windows
/// path so each OS does its own native thing. Linux-only.
/// </summary>
public static class DesktopEntry
{
    // XDG_DATA_HOME (or ~/.local/share). SpecialFolder.LocalApplicationData resolves to it on Linux.
    private static string DataHome =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>The per-user path the entry is written to (<c>~/.local/share/applications/…</c>).</summary>
    public static string EntryPath => Path.Combine(DataHome, "applications", "OpenTabletArtist.desktop");

    /// <summary>Where the extracted launcher icon is written (a <c>.desktop</c> Icon= needs a real file).</summary>
    public static string IconPath => Path.Combine(DataHome, "icons", "opentabletartist.png");

    /// <summary>True if the entry already exists.</summary>
    public static bool Exists => File.Exists(EntryPath);

    /// <summary>Create (or overwrite) the entry, pointing at the current process's exe.</summary>
    /// <returns>true on success; false with <paramref name="error"/> set on failure.</returns>
    public static bool TryCreate(out string path, out string? error)
    {
        path = EntryPath;
        error = null;
        if (!OperatingSystem.IsLinux())
        {
            error = "Application menu entries are only supported on Linux.";
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

            // Extract the embedded app icon to a real file — an iconless entry still works, so this is
            // best-effort and never fails the whole operation.
            var icon = TryWriteIcon();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildEntry(exe, icon));

            // Refresh the menu cache so the entry appears without a re-login. Optional — GNOME picks up new
            // entries on its own — so a missing tool or failure is ignored.
            TryUpdateDesktopDatabase();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildEntry(string exe, string? icon)
    {
        // Exec is quoted so a build-output path containing spaces stays a single argument (the Desktop Entry
        // spec allows a whole argument to be double-quoted). '\n' line endings, UTF-8 (File.WriteAllText).
        var sb = new StringBuilder();
        sb.Append("[Desktop Entry]\n");
        sb.Append("Type=Application\n");
        sb.Append("Name=OpenTabletArtist\n");
        sb.Append("Comment=Alternative GUI for OpenTabletDriver\n");
        sb.Append($"Exec=\"{exe}\"\n");
        if (!string.IsNullOrEmpty(icon))
            sb.Append($"Icon={icon}\n");
        sb.Append("Terminal=false\n");
        // A single main category — listing two (e.g. Graphics;Utility) can make the app appear twice in menus.
        sb.Append("Categories=Graphics;\n");
        // Associates the running window with this entry (Avalonia's default WM class is the assembly name).
        sb.Append("StartupWMClass=OpenTabletArtist\n");
        return sb.ToString();
    }

    private static string? TryWriteIcon()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconPath)!);
            using var src = AssetLoader.Open(new Uri("avares://OpenTabletArtist/Assets/appicon.png"));
            using var dst = File.Create(IconPath);
            src.CopyTo(dst);
            return IconPath;
        }
        catch
        {
            return null; // no icon isn't fatal — the entry still launches, just without a custom icon
        }
    }

    private static void TryUpdateDesktopDatabase()
    {
        try
        {
            var dir = Path.GetDirectoryName(EntryPath)!;
            using var proc = Process.Start(new ProcessStartInfo("update-desktop-database", $"\"{dir}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            proc?.WaitForExit(3000);
        }
        catch { /* optional cache refresh; absence or failure is harmless */ }
    }
}
