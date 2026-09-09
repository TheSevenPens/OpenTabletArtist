using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Ordered candidate locations for the OpenTabletDriver daemon exe. Pure (no filesystem access) so the
/// ordering is unit-testable; the caller picks the first that exists.
///
/// The ladder covers the three daemon-acquisition modes in
/// <c>docs/design/official-otd-release.md</c>: a path the user pointed us at, the copy bundled with a
/// published release, an OTD the user already installed ("adopt"), and finally the dev build tree.
/// </summary>
public static class DaemonExePaths
{
    /// <summary>The daemon's executable file name, platform-aware (#140): the .NET apphost is
    /// <c>OpenTabletDriver.Daemon.exe</c> on Windows and extension-less (<c>OpenTabletDriver.Daemon</c>)
    /// on macOS / Linux. Hardcoding <c>.exe</c> is what broke Restart on the macOS port.</summary>
    public static string DaemonExeName { get; } =
        OperatingSystem.IsWindows() ? "OpenTabletDriver.Daemon.exe" : "OpenTabletDriver.Daemon";

    /// <summary><see cref="Services.AppSettings"/> key holding a daemon location the user chose
    /// explicitly ("it's already on my system"). Wins over every discovered location.</summary>
    public const string UserPathSettingKey = "daemon.userPath";

    /// <summary>The macOS app-bundle name OTD installs under.</summary>
    private const string MacBundleName = "OpenTabletDriver.app";

    /// <summary>
    /// Daemon paths inside an OTD installed the normal way on macOS — the system <c>/Applications</c>
    /// and the per-user <c>~/Applications</c>. Pure: <paramref name="homeDir"/> is passed in rather than
    /// read from the environment so the layout is testable off-Mac.
    /// </summary>
    public static IEnumerable<string> InstalledMacPaths(string? homeDir)
    {
        yield return MacBundleDaemon(Path.Combine("/", "Applications"));
        if (!string.IsNullOrEmpty(homeDir))
            yield return MacBundleDaemon(Path.Combine(homeDir, "Applications"));
    }

    private static string MacBundleDaemon(string appsDir) =>
        Path.GetFullPath(Path.Combine(appsDir, MacBundleName, "Contents", "MacOS", "OpenTabletDriver.Daemon"));

    /// <summary>
    /// Normalizes whatever the user pointed us at into a daemon exe path. Accepts the exe itself, an
    /// <c>OpenTabletDriver.app</c> bundle, or a directory that contains the exe — a file picker set to
    /// "choose OTD" plausibly yields any of the three, and rejecting two of them would just be a puzzle.
    /// Returns null for null/blank input or a path that can't be normalized.
    /// </summary>
    public static string? NormalizeUserPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim());

            // An .app bundle → the daemon inside it. (Checked on the string, not the filesystem, so this
            // stays pure; a wrong guess simply fails the File.Exists probe in the caller.)
            if (full.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(full, "Contents", "MacOS", "OpenTabletDriver.Daemon");

            // Already the exe.
            if (string.Equals(Path.GetFileName(full), DaemonExeName, StringComparison.OrdinalIgnoreCase))
                return full;

            // Otherwise treat it as a directory holding the exe.
            return Path.Combine(full, DaemonExeName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The search ladder, most-specific first. <paramref name="userPath"/> is the raw user setting (it is
    /// normalized here); <paramref name="installed"/> is the set of already-installed OTD locations to
    /// consider, supplied by the caller so this stays platform-agnostic and pure.
    /// </summary>
    public static IEnumerable<string> Candidates(
        string baseDir,
        string? userPath = null,
        IEnumerable<string>? installed = null)
    {
        // 0. An explicit user choice outranks anything we discover — including a bundled copy. If someone
        //    has told us which OTD to drive, silently preferring a different one is never right.
        var chosen = NormalizeUserPath(userPath);
        if (chosen != null) yield return chosen;

        // 1. Bundled next to the app — published release layout: <app>/Daemon/<exe>
        yield return Path.GetFullPath(Path.Combine(baseDir, "Daemon", DaemonExeName));

        // 2. An OTD the user already installed. Ahead of the dev tree so a working, permission-granted
        //    install wins over a freshly built daemon that macOS has never granted Input Monitoring to
        //    (see docs/design/official-otd-release.md). Empty on platforms the caller hasn't enabled
        //    adoption for yet — Windows keeps its bundled-then-dev-tree order until Phase D.
        foreach (var path in installed ?? [])
            yield return Path.GetFullPath(path);

        // 3/4. Dev build tree: <app>/bin/<cfg>/net10.0 → up to repo root → submodule daemon output.
        foreach (var config in new[] { "Debug", "Release" })
            yield return Path.GetFullPath(Path.Combine(
                baseDir, "..", "..", "..", "..",
                "external", "OpenTabletDriver", "OpenTabletDriver.Daemon",
                "bin", config, "net8.0", DaemonExeName));
    }

    /// <summary>
    /// True when <paramref name="path"/> is a daemon this project produced — the bundled release copy or
    /// the submodule dev build — as opposed to an OTD installed on the system that we merely drive.
    /// Drives the "not our build" caution around Stop/Restart; see <c>AppSession.UpdateDaemonSource</c>.
    /// </summary>
    public static bool IsOwnBuild(string baseDir, string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        // Deliberately ignores the user-path and installed tiers: both are, by definition, not ours.
        return Candidates(baseDir).Any(c => ExecutablePath.SameFile(path, c));
    }
}
