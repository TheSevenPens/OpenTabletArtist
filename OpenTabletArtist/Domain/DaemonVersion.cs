using System;
using System.Diagnostics;
using System.IO;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Reads the OTD daemon's version off its on-disk binary (the daemon doesn't report it over RPC).
/// A native apphost (macOS/Linux, extension-less) carries no Win32 version resource, so the read of the
/// executable itself comes back empty — in that case fall back to the sibling managed assembly
/// (<c>OpenTabletDriver.Daemon.dll</c>), which does carry the version. The fallback is platform-neutral
/// (it also helps any Windows apphost that lacks a version resource). (#140)
/// </summary>
public static class DaemonVersion
{
    public const string SiblingAssemblyName = "OpenTabletDriver.Daemon.dll";

    /// <summary>The daemon's product/file version, or "" if none can be read. Strips SemVer build
    /// metadata (e.g. "+abc123").</summary>
    public static string Read(string executablePath, string siblingAssemblyName = SiblingAssemblyName)
    {
        var version = FromFile(executablePath);
        if (!string.IsNullOrEmpty(version)) return version;

        var dir = Path.GetDirectoryName(executablePath);
        if (dir == null) return "";
        var sibling = Path.Combine(dir, siblingAssemblyName);
        // Don't re-read the same file (e.g. when the executable already *is* the managed .dll on Windows).
        if (string.Equals(sibling, executablePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(sibling))
            return "";
        return FromFile(sibling);
    }

    private static string FromFile(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = (info.ProductVersion ?? info.FileVersion ?? "").Trim();
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
        catch
        {
            return "";
        }
    }
}
