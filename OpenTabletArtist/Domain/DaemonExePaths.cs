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

    /// <summary>The OTD settings window's executable file name for this platform.</summary>
    public static string UxExeName { get; } =
        OperatingSystem.IsWindows() ? "OpenTabletDriver.UX.Wpf.exe"
        : OperatingSystem.IsMacOS() ? "OpenTabletDriver.UX.MacOS"
        : "OpenTabletDriver.UX.Gtk";

    /// <summary><see cref="Services.AppSettings"/> key holding a daemon location the user chose
    /// explicitly ("it's already on my system"). Wins over every discovered location.</summary>
    public const string UserPathSettingKey = "daemon.userPath";

    /// <summary>The copy shipped inside a published release: <c>&lt;app&gt;/Daemon/&lt;exe&gt;</c>. Present on
    /// Windows releases; absent on macOS, where nothing is bundled yet (Phase C of
    /// docs/design/official-otd-release.md) — so "switch to the bundled daemon" is not always an option
    /// that exists.</summary>
    public static string BundledPath(string baseDir) =>
        Path.GetFullPath(Path.Combine(baseDir, "Daemon", DaemonExeName));

    /// <summary>What the driver card can honestly offer about the bundled daemon (#725).</summary>
    public enum BundledOffer
    {
        /// <summary>Nothing to say: no bundled copy, or the connected daemon is already ours.</summary>
        Nothing,

        /// <summary>A restart would reach the bundled copy, so the switch can be offered.</summary>
        Switch,

        /// <summary>The bundled copy exists but a chosen location is in front of it in the ladder.</summary>
        ClearTheChosenLocationFirst,
    }

    /// <summary>
    /// Whether a way back to the bundled daemon can be offered, and truthfully.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The switch is a restart, and a restart launches whatever <see cref="Candidates"/> resolves to:
    /// user-chosen first, then bundled. So the bundled copy is what a restart reaches only while no
    /// location has been chosen. With one chosen, the same press relaunches <em>that</em> — which is the
    /// daemon the user is trying to leave.
    /// </para>
    /// <para>
    /// The button this replaces asked only whether a foreign daemon was connected and whether a bundled
    /// copy existed, so in exactly that case it offered a switch and did the opposite. It was removed in
    /// 660b49a as duplication; what was wrong with it was never the duplication.
    /// </para>
    /// <para>
    /// Pure, and the OS is not one of its inputs: whether a copy is bundled is already the caller's
    /// answer (<c>HasBundledDaemon</c>), which is false on macOS for the reason
    /// <see cref="BundledPath"/> gives.
    /// </para>
    /// </remarks>
    public static BundledOffer OfferBundled(bool onForeignDaemon, bool hasBundled, bool hasChosenLocation)
    {
        if (!onForeignDaemon || !hasBundled) return BundledOffer.Nothing;

        return hasChosenLocation ? BundledOffer.ClearTheChosenLocationFirst : BundledOffer.Switch;
    }

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

    /// <summary>Outcome of vetting a path the user chose: the resolved daemon exe, or the reason it was
    /// rejected (shown verbatim, so it has to read as an explanation rather than an error code).</summary>
    public sealed record UserPathResult(string? Path, string? Problem)
    {
        public bool Accepted => Path != null;
    }

    /// <summary>
    /// Vets a path the user picked. Pure: <paramref name="exists"/> is injected so the rules are testable
    /// without touching disk. Accepts the same three shapes as <see cref="NormalizeUserPath"/>, and
    /// rejects anything that doesn't actually resolve to a daemon on disk — storing a path that will
    /// silently lose the ladder race is worse than refusing it while the picker is still open.
    /// </summary>
    public static UserPathResult ValidateUserPath(string? raw, Func<string, bool> exists)
    {
        var normalized = NormalizeUserPath(raw);
        if (normalized == null)
            return new UserPathResult(null, "That doesn't look like a path to OpenTabletDriver.");
        if (!exists(normalized))
            return new UserPathResult(null,
                $"No OpenTabletDriver daemon there — expected to find {DaemonExeName} at {normalized}.");
        return new UserPathResult(normalized, null);
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
        yield return BundledPath(baseDir);

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
        // Deliberately ignores the user-path and installed tiers: both are, by definition, not ours.
        return Candidates(baseDir).Any(c => ExecutablePath.SameFile(path, c));
    }
}
