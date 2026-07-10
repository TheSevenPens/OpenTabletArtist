using System;
using System.Collections.Generic;
using System.IO;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Ordered candidate locations for the OpenTabletDriver daemon exe, relative to the app's base
/// directory. Pure (no filesystem access) so the ordering is unit-testable; the caller picks the
/// first that exists. Covers both the published-release layout (daemon bundled next to the app)
/// and the dev build tree (daemon built into the submodule's bin output).
/// </summary>
public static class DaemonExePaths
{
    /// <summary>The daemon's executable file name, platform-aware (#140): the .NET apphost is
    /// <c>OpenTabletDriver.Daemon.exe</c> on Windows and extension-less (<c>OpenTabletDriver.Daemon</c>)
    /// on macOS / Linux. Hardcoding <c>.exe</c> is what broke Restart on the macOS port.</summary>
    public static string DaemonExeName { get; } =
        OperatingSystem.IsWindows() ? "OpenTabletDriver.Daemon.exe" : "OpenTabletDriver.Daemon";

    public static IEnumerable<string> Candidates(string baseDir)
    {
        // 1. Bundled next to the app — published release layout: <app>/Daemon/<exe>
        yield return Path.GetFullPath(Path.Combine(baseDir, "Daemon", DaemonExeName));

        // 2/3. Dev build tree: <app>/bin/<cfg>/net10.0 → up to repo root → submodule daemon output.
        foreach (var config in new[] { "Debug", "Release" })
            yield return Path.GetFullPath(Path.Combine(
                baseDir, "..", "..", "..", "..",
                "external", "OpenTabletDriver", "OpenTabletDriver.Daemon",
                "bin", config, "net8.0", DaemonExeName));
    }
}
