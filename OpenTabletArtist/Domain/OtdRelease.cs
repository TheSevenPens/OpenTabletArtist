using System;
using System.IO;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// The official OpenTabletDriver release OTA installs when the user has none — pinned, not "latest".
///
/// Pinned to the same release the submodule is on, so an assisted install produces a driver that matches
/// what OTA was compiled against: the happy path then raises no <c>otd.versionMismatch</c>, and installs
/// are reproducible rather than drifting with whatever upstream published today.
/// (docs/design/official-otd-release.md)
///
/// Pure — no filesystem, no network — so the naming rules are unit-testable.
/// </summary>
public static class OtdRelease
{
    /// <summary>The git tag of the pinned release. For a three-part release the tag is <c>v0.6.8</c> while the
    /// assemblies inside report <c>0.6.8.0</c> (a .NET four-part version); both name the same release. A
    /// fourth part, as in <c>v0.6.8.1</c>, is real and is carried through unchanged.</summary>
    public const string Tag = "v0.6.8.1";

    /// <summary>The version as it appears in the release asset's file name — including any prerelease
    /// suffix, which <see cref="System.Version"/> cannot carry.</summary>
    public const string AssetVersion = "0.6.8.1";

    /// <summary>
    /// The pinned release as a four-part <see cref="System.Version"/> — the shape the OTD assemblies
    /// report (<c>0.6.8.1</c>; a three-part release such as <c>0.6.8</c> reports <c>0.6.8.0</c>), so
    /// comparisons against plugin manifests and daemon versions are unchanged.
    /// The prerelease suffix is dropped: the assemblies do not carry it, and <c>Version.Parse</c> rejects it.
    ///
    /// <b>Read this, never a linked OTD assembly's version.</b> Publishing the app passes
    /// <c>-p:Version=&lt;OTA version&gt;</c>, and MSBuild applies that to every project in the graph —
    /// including the OTD submodule projects OTA references. So in a release build
    /// <c>typeof(AppInfo).Assembly.GetName().Version</c> reports <em>OTA's</em> version, not OTD's, and
    /// every comparison built on it is wrong. Dev builds pass no version and keep the driver's own, which
    /// is why this never showed up locally.
    /// </summary>
    public static readonly System.Version Version = FourPart(AssetVersion);

    /// <summary>A release name as a four-part version: the suffix dropped, and zeros appended to make four
    /// parts. Appending a fixed <c>.0</c> would turn <c>0.6.8.1</c> into the five-part <c>0.6.8.1.0</c>,
    /// which <see cref="System.Version"/> rejects.</summary>
    internal static System.Version FourPart(string release)
    {
        var parts = release.Split('-')[0].Split('.').ToList();
        while (parts.Count < 4) parts.Add("0");
        return System.Version.Parse(string.Join('.', parts));
    }

    /// <summary>The macOS artifact OTA installs, which is the x64 one: on Apple Silicon the daemon runs under
    /// Rosetta 2. OTA itself stays native arm64; they are separate processes over RPC, so the mixed
    /// architecture is fine. OpenTabletDriver has published a native <c>osx-arm64</c> artifact since 0.6.8
    /// (before that, x64 only); moving to it is open until it has been tried on a real Apple Silicon Mac.</summary>
    public const string MacAssetName = $"OpenTabletDriver-{AssetVersion}_osx-x64.tar.gz";

    /// <summary>The app bundle the tarball unpacks to, and the name it is installed under.</summary>
    public const string MacBundleName = "OpenTabletDriver.app";

    /// <summary>Direct download for the pinned macOS artifact.</summary>
    public static string MacDownloadUrl =>
        $"https://github.com/OpenTabletDriver/OpenTabletDriver/releases/download/{Tag}/{MacAssetName}";

    /// <summary>
    /// Where an assisted install puts OpenTabletDriver: the system <c>/Applications</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is where macOS users look for and remove apps, and where OpenTabletDriver's own instructions
    /// put it. Those are now the whole of the argument. The deciding reason used to be that it was the
    /// first entry on the daemon search ladder, so installing lower would leave the install shadowable
    /// by whatever appeared here later — but #930 cut that ladder back to OTA's own copy, and an
    /// OpenTabletDriver installed here is one OTA connects to rather than one it launches. There is no
    /// candidate left for this path to line up with.
    /// </para>
    /// <para>
    /// <c>/Applications</c> is <c>drwxrwxr-x root:admin</c>, so an admin account — the default on a
    /// personal Mac — writes here with no authorization prompt. A standard account cannot; that case
    /// fails with an explanation rather than falling back to a per-user location, and is deliberately
    /// left for later.
    /// </para>
    /// <para>
    /// Normalized with <see cref="Path.GetFullPath(string)"/> because it is compared and shown, not just
    /// joined: <c>OtdInstaller</c> tests it for writability and checks
    /// <see cref="InstalledBundlePath"/> for an install already there, and the Daemon page prints it.
    /// A drive-less "/Applications" is not a full path off macOS.
    /// </para>
    /// </remarks>
    public static string InstallDirectory => Path.GetFullPath(Path.Combine("/", "Applications"));

    /// <summary>Full path of the installed bundle.</summary>
    public static string InstalledBundlePath => Path.Combine(InstallDirectory, MacBundleName);

    /// <summary>The daemon inside an installed bundle — what the caller probes to confirm the install
    /// produced something runnable.</summary>
    public static string InstalledDaemonPath =>
        Path.GetFullPath(Path.Combine(InstalledBundlePath, "Contents", "MacOS", "OpenTabletDriver.Daemon"));
}
