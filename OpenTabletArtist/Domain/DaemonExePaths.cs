using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Where OTA looks for a daemon to <b>launch</b>. Pure (no filesystem access) so the ordering is
/// unit-testable; the caller picks the first that exists.
///
/// <b>Only OTA's own copy.</b> A daemon already running owns the pipe and OTA simply connects to it,
/// whatever it is — so this ladder answers one narrower question: which executable to start when
/// nothing is running. The answer is the copy shipped beside the app, or its dev-tree equivalent when
/// running from a build. It used to also take a location the artist had chosen and an OpenTabletDriver
/// found installed on the system; both are gone. Someone who wants their own driver starts it, and OTA
/// connects to it like any other.
/// </summary>
public static class DaemonExePaths
{
    /// <summary>The daemon's executable file name, platform-aware (#140): the .NET apphost is
    /// <c>OpenTabletDriver.Daemon.exe</c> on Windows and extension-less (<c>OpenTabletDriver.Daemon</c>)
    /// on macOS / Linux. Hardcoding <c>.exe</c> is what broke Restart on the macOS port.</summary>
    public static string DaemonExeName { get; } =
        OperatingSystem.IsWindows() ? "OpenTabletDriver.Daemon.exe" : "OpenTabletDriver.Daemon";

    /// <summary>
    /// OpenTabletDriver's own settings window, beside the daemon it belongs to, or null if it is not
    /// there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three names for one thing, because the UX is built per toolkit: WPF on Windows, GTK on Linux, and
    /// a macOS build whose binary sits in the same folder as the daemon inside the .app bundle. Beside
    /// the daemon in every case, which is what makes one rule enough.
    /// </para>
    /// <para>
    /// Derived from the daemon OTA is actually connected to rather than searched for independently. The
    /// point of the button is to open the UX <em>for this driver</em>; finding some other installation's
    /// copy and pointing it at a different daemon would be worse than offering nothing.
    /// </para>
    /// <para>
    /// Probed rather than assumed: a daemon can be running from a build tree or a packaged install that
    /// ships no UX at all, and an offer that opens nothing is worse than an absent one.
    /// </para>
    /// </remarks>
    public static string? UxBeside(string? daemonExePath)
    {
        if (string.IsNullOrWhiteSpace(daemonExePath)) return null;
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(daemonExePath.Trim()));
            if (string.IsNullOrEmpty(folder)) return null;

            var ux = Path.Combine(folder, UxExeName);
            return File.Exists(ux) ? ux : null;
        }
        catch { return null; }   // an unusable path is simply no UX, like a missing one
    }

    /// <summary>A place <see cref="FindUx"/> looked, and why it looked there.</summary>
    public sealed record UxSearched(string Folder, string Where);

    /// <summary>Where OpenTabletDriver's own window is, or everywhere it was looked for and was not.</summary>
    public sealed record UxLookup(string? Path, IReadOnlyList<UxSearched> Searched)
    {
        /// <summary>What to tell the artist when <see cref="Path"/> is null: where it looked, so the answer is
        /// something they can check rather than a bare "not found".</summary>
        public string NotFoundMessage()
        {
            var name = UxExeName;
            if (Searched.Count == 0)
                return $"OpenTabletArtist couldn't find OpenTabletDriver's own window ({name}). It doesn't know where an "
                    + "OpenTabletDriver is installed, so it has nowhere to look. Start the daemon and try again, or "
                    + "reinstall OpenTabletArtist.";
            var where = string.Join("\n", Searched.Select(s => $"• {s.Where}: {s.Folder}"));
            return $"OpenTabletArtist couldn't find OpenTabletDriver's own window ({name}). It looked:\n\n{where}\n\n"
                + "It isn't in any of those places.";
        }
    }

    /// <summary>
    /// Finds OpenTabletDriver's own window (#1043): beside the daemon that is running if one is, and only
    /// there; with none running, beside the daemon OTA ships or can start.
    /// </summary>
    /// <remarks>
    /// A running daemon is the only place looked, and that is the rule <see cref="UxBeside"/> was written
    /// for: the window should belong to <em>that</em> driver, and another install's copy would point the
    /// artist at a daemon that is not the one the page is about. So a running daemon with no window beside
    /// it is an answer ("not there"), not a reason to open someone else's.
    ///
    /// With nothing running there is no driver to honour, and the copy that ships with the app is the
    /// sensible one. Only folders that exist are reported as searched: a developer build probes paths that
    /// were never going to be there, and listing them would bury the one the artist can actually look in.
    /// </remarks>
    public static UxLookup FindUx(string? runningDaemonPath, string baseDir)
    {
        var searched = new List<UxSearched>();

        string? Look(string? daemonPath, string where)
        {
            if (UxBeside(daemonPath) is { } found) return found;
            try
            {
                if (string.IsNullOrWhiteSpace(daemonPath)) return null;
                var folder = Path.GetDirectoryName(Path.GetFullPath(daemonPath.Trim()));
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)
                    && !searched.Any(s => string.Equals(s.Folder, folder,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    searched.Add(new UxSearched(folder, where));
            }
            catch { /* an unusable path is simply nowhere to look */ }
            return null;
        }

        if (!string.IsNullOrWhiteSpace(runningDaemonPath))
            return new UxLookup(Look(runningDaemonPath, "beside the daemon that is running"), searched);

        string? ux = null;
        foreach (var candidate in Candidates(baseDir))
            if ((ux = Look(candidate, "beside the daemon OpenTabletArtist ships with")) is not null) break;
        return new UxLookup(ux, searched);
    }

    /// <summary>The OTD settings window's executable file name for this platform.</summary>
    public static string UxExeName { get; } =
        OperatingSystem.IsWindows() ? "OpenTabletDriver.UX.Wpf.exe"
        : OperatingSystem.IsMacOS() ? "OpenTabletDriver.UX.MacOS"
        : "OpenTabletDriver.UX.Gtk";

    /// <summary>The copy shipped inside a published release: <c>&lt;app&gt;/Daemon/&lt;exe&gt;</c>. Present on
    /// Windows releases; absent on macOS, where nothing is bundled yet (Phase C of
    /// docs/design/official-otd-release.md) — so "switch to the bundled daemon" is not always an option
    /// that exists.</summary>
    public static string BundledPath(string baseDir) =>
        Path.GetFullPath(Path.Combine(baseDir, "Daemon", DaemonExeName));

    /// <summary>
    /// The copies of the daemon that are OTA's to start, in order.
    /// </summary>
    /// <remarks>
    /// Both entries are the same thing wearing different clothes: the daemon this build of OTA ships.
    /// A published release carries it in <c>Daemon/</c>; a developer build has no such folder, so the
    /// submodule's own output stands in for it. There is deliberately nothing else here — see the type's
    /// summary for why a driver the artist installed is not OTA's to launch.
    /// </remarks>
    public static IEnumerable<string> Candidates(string baseDir)
    {
        // Bundled next to the app — published release layout: <app>/Daemon/<exe>
        yield return BundledPath(baseDir);

        // Dev build tree: <app>/bin/<cfg>/net10.0 → up to repo root → submodule daemon output.
        foreach (var config in new[] { "Debug", "Release" })
            yield return Path.GetFullPath(Path.Combine(
                baseDir, "..", "..", "..", "..",
                "external", "OpenTabletDriver", "OpenTabletDriver.Daemon",
                "bin", config, "net10.0", DaemonExeName));
    }

    /// <summary>
    /// True when <paramref name="path"/> is a daemon <b>OTA manages</b> — the copy bundled beside the app,
    /// or the submodule dev build — as opposed to an OpenTabletDriver installed on the system that OTA
    /// merely drives.
    ///
    /// <b>Managed, not built.</b> Since #794 the bundled copy is OpenTabletDriver's own released binary,
    /// downloaded at package time, so OTA builds no daemon at all. What this answers is "did OTA put this
    /// here, in its own folder?" — which is the question that actually matters for the decisions it feeds:
    /// whether Stop/Restart needs a confirmation, and whether plugins may be installed without asking. A
    /// bundled upstream binary is still OTA's to manage; a user's own install is not, whoever compiled it.
    ///
    /// If you are here to "fix" this because the name said "built" and the binary plainly isn't ours —
    /// that was the stale name, not a bug. See <c>AppSession.UpdateDaemonSource</c>.
    /// </summary>
    public static bool IsAppManaged(string baseDir, string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return Candidates(baseDir).Any(c => ExecutablePath.SameFile(path, c));
    }
}
