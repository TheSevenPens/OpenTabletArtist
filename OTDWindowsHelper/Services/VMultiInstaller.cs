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
    /// Downloads the VMulti package for its bundled tools, then runs an in-app removal script
    /// (no <c>@pause</c>) elevated with a hidden window: DIFxCmd uninstalls the driver, devcon removes
    /// the active device and the leftover driverless <c>djpnewton\vmulti</c> nodes (#110/#112). The
    /// card re-detects the real state afterward; a restart is recommended.
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

            // Locate the driver folder (DIFxCmd.exe / devcon.exe / vmulti.inf live together).
            string? inf = FindFile(extractDir, "vmulti.inf");
            if (inf == null)
                return new InstallResult(false, "Could not find vmulti.inf in the downloaded archive.");
            string driverDir = Path.GetDirectoryName(inf)!;

            // Write our own removal script instead of the stock remove_hiddriver.bat — its trailing
            // `@pause` leaves a cmd window open waiting for a keypress (and blocks our completion).
            // We also remove the leftover driverless `djpnewton\vmulti` nodes that `devcon remove
            // pentablet\hid` leaves behind (Device Manager Code 28, #110), so detection comes back clean.
            string script = Path.Combine(driverDir, "otd_remove_vmulti.bat");
            await File.WriteAllTextAsync(script, string.Join("\r\n", new[]
            {
                "@echo off",
                "\"%~dp0DIFxCmd.exe\" /u vmulti.inf",
                "\"%~dp0devcon.exe\" remove \"pentablet\\hid\"",
                "\"%~dp0devcon.exe\" remove \"djpnewton\\vmulti\"",
                // Always succeed: devcon returns non-zero when a node was already gone, which isn't a
                // failure for us. The card re-detects the real state after this returns.
                "exit /b 0",
                "",
            }), ct);

            // Step 3: Run the removal once, elevated, with a hidden window (single UAC prompt, no
            // lingering console). UAC is unavoidable for driver removal.
            StatusChanged?.Invoke("Removing driver (admin required)...");
            ProgressChanged?.Invoke(70);

            var psi = new ProcessStartInfo
            {
                FileName = script,
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                WorkingDirectory = driverDir,
            };

            var process = Process.Start(psi);
            if (process == null)
                return new InstallResult(false, "Failed to start the uninstaller process.");

            await process.WaitForExitAsync(ct);
            ProgressChanged?.Invoke(100);

            StatusChanged?.Invoke("VMulti driver removed.");
            return new InstallResult(true,
                "VMulti was removed. A restart is recommended to finish cleaning it up.",
                RebootRecommended: true);
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

public record InstallResult(bool Success, string Message, bool RebootRecommended = false);
