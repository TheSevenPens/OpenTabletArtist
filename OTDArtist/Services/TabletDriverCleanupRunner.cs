using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace OtdArtist.Services;

/// <summary>
/// Manages the TabletDriverCleanup tool from
/// https://github.com/OpenTabletDriver/TabletDriverCleanup
///
/// The tool is installed once to a per-user location (no admin required
/// for the install itself — only for running the tool, since it modifies
/// drivers). Once installed, it can be run multiple times, browsed to,
/// or uninstalled.
/// </summary>
public class TabletDriverCleanupRunner
{
    // GitHub's /releases/latest/download/<filename> redirects to the newest
    // release's asset, so we don't need to pin a version.
    private const string DownloadUrl =
        "https://github.com/OpenTabletDriver/TabletDriverCleanup/releases/latest/download/tabletdrivercleanup.zip";

    /// <summary>
    /// Per-user install location. No admin required to write here.
    /// </summary>
    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TabletDriverCleanup");

    public static string ExePath => Path.Combine(InstallDir, "TabletDriverCleanup.exe");

    /// <summary>
    /// Returns true if the cleanup tool has been installed to InstallDir.
    /// </summary>
    public static bool IsInstalled() => File.Exists(ExePath);

    public event Action<string>? StatusChanged;
    public event Action<int>? ProgressChanged;

    /// <summary>
    /// Downloads and extracts the cleanup tool to InstallDir. No admin required.
    /// </summary>
    public async Task<CleanupResult> InstallAsync(CancellationToken ct = default)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TabletDriverCleanup_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, "tabletdrivercleanup.zip");

            // Step 1: Download
            StatusChanged?.Invoke("Downloading TabletDriverCleanup...");
            ProgressChanged?.Invoke(10);

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("OtdArtist/1.0");
                using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using var fileStream = File.Create(zipPath);
                await response.Content.CopyToAsync(fileStream, ct);
            }

            ProgressChanged?.Invoke(50);

            // Step 2: Extract to a sibling temp folder first so we can locate
            // the contents, then copy into the final install directory.
            StatusChanged?.Invoke("Extracting files...");
            string extractDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            ProgressChanged?.Invoke(75);

            // The exe may be at the root of the archive or in a subfolder —
            // find it and use its directory as the source.
            string? foundExe = FindFile(extractDir, "TabletDriverCleanup.exe");
            if (foundExe == null)
                return new CleanupResult(false, "Could not find TabletDriverCleanup.exe in the downloaded archive.");

            string sourceDir = Path.GetDirectoryName(foundExe)!;

            // Step 3: Replace any existing install with the new files
            StatusChanged?.Invoke("Installing to " + InstallDir);
            if (Directory.Exists(InstallDir))
            {
                try { Directory.Delete(InstallDir, true); } catch { /* best effort */ }
            }
            CopyDirectory(sourceDir, InstallDir);
            ProgressChanged?.Invoke(100);

            StatusChanged?.Invoke("Installed.");
            return new CleanupResult(true, $"TabletDriverCleanup installed to {InstallDir}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Install failed.");
            return new CleanupResult(false, $"Error: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>
    /// Runs the installed cleanup tool. Requires admin elevation (UAC prompt).
    /// The terminal window stays visible so the user can read the output.
    /// </summary>
    public async Task<CleanupResult> RunAsync(CancellationToken ct = default)
    {
        if (!IsInstalled())
            return new CleanupResult(false, "TabletDriverCleanup is not installed.");

        try
        {
            StatusChanged?.Invoke("Running cleanup (admin required)...");

            var psi = new ProcessStartInfo
            {
                FileName = ExePath,
                WorkingDirectory = InstallDir,
                Verb = "runas",
                UseShellExecute = true,
                // Do NOT set CreateNoWindow — we want the terminal visible so
                // the user can read the cleanup output.
            };

            var process = Process.Start(psi);
            if (process == null)
                return new CleanupResult(false, "Failed to start the cleanup process.");

            await process.WaitForExitAsync(ct);

            StatusChanged?.Invoke("Cleanup finished.");
            return new CleanupResult(true, "TabletDriverCleanup finished. If it removed any drivers, a restart may help.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusChanged?.Invoke("Cleanup cancelled.");
            return new CleanupResult(false, "Cleanup was cancelled (admin permission denied).");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Cleanup failed.");
            return new CleanupResult(false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the installed cleanup tool from InstallDir.
    /// </summary>
    public CleanupResult Uninstall()
    {
        try
        {
            if (Directory.Exists(InstallDir))
            {
                Directory.Delete(InstallDir, true);
                StatusChanged?.Invoke("Uninstalled.");
                return new CleanupResult(true, "TabletDriverCleanup uninstalled.");
            }
            return new CleanupResult(true, "TabletDriverCleanup was not installed.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Uninstall failed.");
            return new CleanupResult(false, $"Error: {ex.Message}");
        }
    }

    private static string? FindFile(string dir, string fileName)
    {
        foreach (var file in Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories))
            return file;
        return null;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

public record CleanupResult(bool Success, string Message);
