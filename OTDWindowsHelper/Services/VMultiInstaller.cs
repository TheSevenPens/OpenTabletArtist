using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace OtdWindowsHelper.Services;

public class VMultiInstaller
{
    private const string DownloadUrl =
        "https://github.com/X9VoiD/vmulti-bin/releases/download/v1.0/VMulti.Driver.zip";

    public event Action<string>? StatusChanged;
    public event Action<int>? ProgressChanged;

    /// <summary>
    /// Downloads, extracts, and installs the VMulti driver.
    /// The install step requires admin elevation (UAC prompt).
    /// Returns true if the install script completed (may still need reboot).
    /// </summary>
    public async Task<InstallResult> InstallAsync(CancellationToken ct = default)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "VMultiInstall_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, "VMulti.Driver.zip");

            // Step 1: Download
            StatusChanged?.Invoke("Downloading VMulti driver...");
            ProgressChanged?.Invoke(10);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OtdWindowsHelper/1.0");

            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var fileStream = File.Create(zipPath);
            await response.Content.CopyToAsync(fileStream, ct);
            await fileStream.FlushAsync(ct);
            fileStream.Close();

            ProgressChanged?.Invoke(40);

            // Step 2: Extract
            StatusChanged?.Invoke("Extracting driver files...");
            string extractDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            ProgressChanged?.Invoke(60);

            // Find the install batch file (may be in a subfolder)
            string? installBat = FindFile(extractDir, "install_hiddriver.bat");
            if (installBat == null)
                return new InstallResult(false, "Could not find install_hiddriver.bat in the downloaded archive.");

            string driverDir = Path.GetDirectoryName(installBat)!;

            // Step 3: Run the official install batch file with admin privileges
            StatusChanged?.Invoke("Installing driver (admin required)...");
            ProgressChanged?.Invoke(70);

            var psi = new ProcessStartInfo
            {
                FileName = installBat,
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = driverDir,
            };

            var process = Process.Start(psi);
            if (process == null)
                return new InstallResult(false, "Failed to start the installer process.");

            await process.WaitForExitAsync(ct);
            ProgressChanged?.Invoke(100);

            if (process.ExitCode == 0)
            {
                StatusChanged?.Invoke("VMulti driver installed successfully.");
                return new InstallResult(true, "Driver installed. A reboot may be required.");
            }
            else
            {
                StatusChanged?.Invoke("Installation may have failed.");
                return new InstallResult(false, $"Installer exited with code {process.ExitCode}. The driver may still have been installed — check Device Manager.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined UAC
            StatusChanged?.Invoke("Installation cancelled.");
            return new InstallResult(false, "Installation was cancelled (admin permission denied).");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Installation failed.");
            return new InstallResult(false, $"Error: {ex.Message}");
        }
        finally
        {
            // Clean up temp files (best effort)
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>
    /// Downloads the VMulti package and runs remove_hiddriver.bat with admin elevation.
    /// </summary>
    public async Task<InstallResult> UninstallAsync(CancellationToken ct = default)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "VMultiUninstall_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, "VMulti.Driver.zip");

            // Step 1: Download (we need the uninstall batch file from the package)
            StatusChanged?.Invoke("Downloading VMulti package...");
            ProgressChanged?.Invoke(10);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OtdWindowsHelper/1.0");

            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var fileStream = File.Create(zipPath);
            await response.Content.CopyToAsync(fileStream, ct);
            await fileStream.FlushAsync(ct);
            fileStream.Close();

            ProgressChanged?.Invoke(40);

            // Step 2: Extract
            StatusChanged?.Invoke("Extracting...");
            string extractDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            ProgressChanged?.Invoke(60);

            // Find the uninstall batch file
            string? uninstallBat = FindFile(extractDir, "remove_hiddriver.bat");
            if (uninstallBat == null)
                return new InstallResult(false, "Could not find remove_hiddriver.bat in the downloaded archive.");

            string driverDir = Path.GetDirectoryName(uninstallBat)!;

            // Step 3: Run uninstall with admin privileges
            StatusChanged?.Invoke("Uninstalling driver (admin required)...");
            ProgressChanged?.Invoke(70);

            var psi = new ProcessStartInfo
            {
                FileName = uninstallBat,
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = driverDir,
            };

            var process = Process.Start(psi);
            if (process == null)
                return new InstallResult(false, "Failed to start the uninstaller process.");

            await process.WaitForExitAsync(ct);
            ProgressChanged?.Invoke(100);

            if (process.ExitCode == 0)
            {
                StatusChanged?.Invoke("VMulti driver uninstalled successfully.");
                return new InstallResult(true, "Driver uninstalled. A reboot may be required.");
            }
            else
            {
                StatusChanged?.Invoke("Uninstallation may have failed.");
                return new InstallResult(false, $"Uninstaller exited with code {process.ExitCode}. Check Device Manager.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusChanged?.Invoke("Uninstallation cancelled.");
            return new InstallResult(false, "Uninstallation was cancelled (admin permission denied).");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Uninstallation failed.");
            return new InstallResult(false, $"Error: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch { }
        }
    }

    private static string? FindFile(string dir, string fileName)
    {
        foreach (var file in Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories))
            return file;
        return null;
    }
}

public record InstallResult(bool Success, string Message);
