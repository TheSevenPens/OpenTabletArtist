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

    /// <summary>Where an assisted install puts OpenTabletDriver: the per-user <c>~/Applications</c>.
    /// Chosen over <c>/Applications</c> because it needs no authorization, and the search ladder already
    /// looks there (<see cref="DaemonExePaths.InstalledMacPaths"/>), so the install is found with no
    /// stored path to keep in step.</summary>
    public static string InstallDirectory(string homeDir) =>
        Path.GetFullPath(Path.Combine(homeDir, "Applications"));

    /// <summary>Full path of the installed bundle.</summary>
    public static string InstalledBundlePath(string homeDir) =>
        Path.Combine(InstallDirectory(homeDir), MacBundleName);

    /// <summary>The daemon inside an installed bundle — what the caller probes to confirm the install
    /// produced something runnable.</summary>
    public static string InstalledDaemonPath(string homeDir) =>
        Path.Combine(InstalledBundlePath(homeDir), "Contents", "MacOS", "OpenTabletDriver.Daemon");
}
