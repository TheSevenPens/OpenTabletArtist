using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Installs the pinned official OpenTabletDriver release for someone who has none — the "install it for
/// me" answer of the not-found flow (docs/design/official-otd-release.md).
///
/// Downloads OTD's own published artifact rather than building one, so what lands on disk is a binary
/// upstream produced and can support. Installs to <c>/Applications</c> — the first entry on the daemon
/// search ladder — so a successful install is found with no stored path to keep in step.
///
/// Deliberately does NOT touch the quarantine attribute. Stripping it would be programmatically
/// disabling a macOS security control on the user's behalf; instead <see cref="GatekeeperGuidance"/> is
/// shown so the user can approve the app themselves, the same way OTD's own instructions do it.
/// </summary>
public sealed class OtdInstaller
{
    /// <summary>Progress text for the UI ("Downloading…", "Extracting…").</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Download progress, 0-100. Only meaningful while downloading.</summary>
    public event Action<int>? ProgressChanged;

    /// <summary>What the user may have to do once the files are in place: macOS will refuse to open an
    /// unsigned app downloaded from the internet until it is approved once, by name.</summary>
    public const string GatekeeperGuidance =
        "If macOS says OpenTabletDriver \"cannot be opened\", open your Applications folder, right-click "
        + "OpenTabletDriver and choose Open, then confirm. That approval is only needed once.";

    /// <summary>Outcome of an install attempt: the installed daemon path, or why it didn't happen.</summary>
    public sealed record Result(string? DaemonPath, string? Problem)
    {
        public bool Installed => DaemonPath != null;
    }

    /// <summary>
    /// Downloads and installs the pinned release. Refuses rather than overwrites when a bundle is already
    /// present — replacing an OpenTabletDriver the user put there is not this flow's business, and a
    /// half-replaced install is worse than none.
    /// </summary>
    public async Task<Result> InstallAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS())
            return new Result(null, "Assisted install is only available on macOS at the moment.");

        var bundlePath = OtdRelease.InstalledBundlePath;
        if (Directory.Exists(bundlePath))
            return new Result(null,
                $"OpenTabletDriver is already installed at {bundlePath}. Remove it first, or point OTA at "
                + "it with Locate OpenTabletDriver.");

        // /Applications is writable by admin accounts without a prompt, but not by standard ones. Say so
        // plainly rather than failing halfway through with a raw permissions error.
        if (!CanWriteTo(OtdRelease.InstallDirectory))
            return new Result(null,
                $"This account can't write to {OtdRelease.InstallDirectory}. Install OpenTabletDriver "
                + "yourself, then use Locate OpenTabletDriver to point OTA at it.");

        var work = Directory.CreateTempSubdirectory("ota-otd-install-");
        try
        {
            var archive = Path.Combine(work.FullName, OtdRelease.MacAssetName);
            StatusChanged?.Invoke($"Downloading OpenTabletDriver {OtdRelease.AssetVersion}…");
            await DownloadAsync(OtdRelease.MacDownloadUrl, archive, ct);

            StatusChanged?.Invoke("Extracting…");
            var staged = Path.Combine(work.FullName, "staged");
            Directory.CreateDirectory(staged);
            await ExtractTarGzAsync(archive, staged, ct);

            var extractedBundle = Path.Combine(staged, OtdRelease.MacBundleName);
            if (!Directory.Exists(extractedBundle))
                return new Result(null,
                    $"The download didn't contain {OtdRelease.MacBundleName}. The release may have changed "
                    + "shape — install OpenTabletDriver yourself and use Locate OpenTabletDriver.");

            StatusChanged?.Invoke("Installing…");
            Directory.Move(extractedBundle, bundlePath);

            var daemon = OtdRelease.InstalledDaemonPath;
            if (!File.Exists(daemon))
                return new Result(null, $"Installed, but no daemon at {daemon}.");

            // tar carries the mode bits, but a stray umask or a re-packed archive can land them without
            // the execute bit — and a daemon that can't be exec'd fails in a way that reads like a
            // permissions bug rather than an install one.
            MakeExecutable(daemon);

            StatusChanged?.Invoke("Installed.");
            return new Result(daemon, null);
        }
        catch (OperationCanceledException)
        {
            return new Result(null, "Install cancelled.");
        }
        catch (HttpRequestException ex)
        {
            AppLog.Warn("Couldn't download the OpenTabletDriver release.", ex);
            return new Result(null, $"Couldn't download OpenTabletDriver: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Couldn't install the OpenTabletDriver release.", ex);
            return new Result(null, $"Couldn't install OpenTabletDriver: {ex.Message}");
        }
        finally
        {
            try { work.Delete(recursive: true); }
            catch (Exception ex) { AppLog.Debug("Couldn't clean up the install temp directory.", ex); }
        }
    }

    /// <summary>Probe rather than infer: group membership, ACLs and a managed Mac's restrictions all bear
    /// on this, and the only reliable answer is to try.</summary>
    private static bool CanWriteTo(string directory)
    {
        var probe = Path.Combine(directory, $".ota-install-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Debug($"{directory} isn't writable by this account.", ex);
            return false;
        }
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;
            if (total is > 0) ProgressChanged?.Invoke((int)(written * 100 / total.Value));
        }
    }

    private static async Task ExtractTarGzAsync(string archive, string destination, CancellationToken ct)
    {
        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, destination, overwriteFiles: true, ct);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void MakeExecutable(string path)
    {
        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Couldn't set the execute bit on {path}.", ex);
        }
    }
}
