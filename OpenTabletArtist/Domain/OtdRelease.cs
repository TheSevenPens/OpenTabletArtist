using System;
using System.IO;

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
    /// <summary>The git tag of the pinned release. Note the tag is <c>v0.6.7</c> while the assemblies
    /// inside report <c>0.6.7.0</c> (a .NET four-part version); both name the same release.</summary>
    public const string Tag = "v0.6.7";

    /// <summary>The version as it appears in the release asset's file name.</summary>
    public const string AssetVersion = "0.6.7";

    /// <summary>OTD publishes one macOS artifact, and it is x64 only — on Apple Silicon the daemon runs
    /// under Rosetta 2. OTA itself stays native arm64; they are separate processes over RPC, so the
    /// mixed architecture is fine.</summary>
    public const string MacAssetName = $"OpenTabletDriver-{AssetVersion}_osx-x64.tar.gz";

    /// <summary>The app bundle the tarball unpacks to, and the name it is installed under.</summary>
    public const string MacBundleName = "OpenTabletDriver.app";

    /// <summary>Direct download for the pinned macOS artifact.</summary>
    public static string MacDownloadUrl =>
        $"https://github.com/OpenTabletDriver/OpenTabletDriver/releases/download/{Tag}/{MacAssetName}";

    /// <summary>Where an assisted install puts OpenTabletDriver: the system <c>/Applications</c>.
    ///
    /// It is where macOS users look for and remove apps, where OpenTabletDriver's own instructions put it,
    /// and — the deciding reason — the first entry on the daemon search ladder
    /// (<see cref="DaemonExePaths.InstalledMacPaths"/>). Installing anywhere lower would leave OTA's own
    /// install permanently shadowable by whatever appeared here later.
    ///
    /// <c>/Applications</c> is <c>drwxrwxr-x root:admin</c>, so an admin account — the default on a
    /// personal Mac — writes here with no authorization prompt. A standard account cannot; that case
    /// fails with an explanation rather than falling back to a per-user location, and is deliberately
    /// left for later.</summary>
    ///
    /// Normalized with <see cref="Path.GetFullPath(string)"/>, the same way
    /// <see cref="DaemonExePaths.InstalledMacPaths"/> builds it — the install target and the ladder's
    /// first installed candidate have to be the SAME string, and a drive-less "/Applications" is not one
    /// off macOS.</summary>
    public static string InstallDirectory => Path.GetFullPath(Path.Combine("/", "Applications"));

    /// <summary>Full path of the installed bundle.</summary>
    public static string InstalledBundlePath => Path.Combine(InstallDirectory, MacBundleName);

    /// <summary>The daemon inside an installed bundle — what the caller probes to confirm the install
    /// produced something runnable.</summary>
    public static string InstalledDaemonPath =>
        Path.GetFullPath(Path.Combine(InstalledBundlePath, "Contents", "MacOS", "OpenTabletDriver.Daemon"));
}
