using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

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

    /// <summary>True if two version strings denote the same release, comparing numeric major.minor.patch and
    /// ignoring a 4th component or any suffix — the daemon binary reports e.g. "0.6.7" while OTA's bundled
    /// assembly version is "0.6.7.0". False if either can't be parsed to at least a major version.</summary>
    public static bool SameRelease(string a, string b)
    {
        var pa = ParseTriple(a);
        var pb = ParseTriple(b);
        return pa is not null && pb is not null && pa.Value == pb.Value;
    }

    // Parse the leading numeric major.minor.patch; minor/patch default to 0. Null if there's no major.
    private static (int major, int minor, int patch)? ParseTriple(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Trim().Split('.');
        if (!TryLeadingInt(parts.ElementAtOrDefault(0), out var major)) return null;
        TryLeadingInt(parts.ElementAtOrDefault(1), out var minor);
        TryLeadingInt(parts.ElementAtOrDefault(2), out var patch);
        return (major, minor, patch);
    }

    private static bool TryLeadingInt(string? s, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;
        int i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return i > 0 && int.TryParse(s.AsSpan(0, i), out value);
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
