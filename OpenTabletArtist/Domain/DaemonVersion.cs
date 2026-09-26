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
        try { return OtdHealth.Collector.FileProbes.DaemonVersion(executablePath, siblingAssemblyName); }
        catch { return ""; }
    }

    /// <summary>Compare numeric major.minor.patch, ignoring the fourth component and suffixes.</summary>
    public static bool SameRelease(string a, string b) => OtdHealth.OtdVersion.SameRelease(a, b);

}
