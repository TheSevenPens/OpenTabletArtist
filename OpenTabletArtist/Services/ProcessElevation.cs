using System;
using System.Security.Principal;

namespace OpenTabletArtist.Services;

/// <summary>
/// Reports whether this process is running elevated (as Administrator). OpenTabletArtist should run
/// as a normal user — running elevated breaks Windows Ink pointer sync and prevents reading foreground
/// other processes' executable paths. Evaluated once (elevation can't change at runtime) and
/// surfaced as a health recommendation on Home.
/// </summary>
public static class ProcessElevation
{
    private static readonly bool _isElevated = Detect();

    /// <summary>True when the current process holds the Administrator role.</summary>
    public static bool IsElevated => _isElevated;

    private static bool Detect()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            return OtdHealth.Collector.HostProbes.IsElevated();
        }
        catch
        {
            return false;
        }
    }
}
