using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace OpenTabletArtist.Services;

public class VMultiInstaller
{
    private const string DownloadUrl =
        "https://github.com/X9VoiD/vmulti-bin/releases/download/v1.0/VMulti.Driver.zip";

    public event Action<string>? StatusChanged;
    public event Action<int>? ProgressChanged;

    /// <summary>The VMulti package bundled next to the app in a release (<c>Bundled/VMulti.Driver.zip</c>),
    /// or null in a dev build that doesn't bundle it — in which case the installer downloads it instead.</summary>
    private static string? BundledVMultiZip()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Bundled", "VMulti.Driver.zip");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Produces <c>VMulti.Driver.zip</c> in <paramref name="tempDir"/>: copies the bundled copy
    /// (offline install), or downloads it as a fallback. Throws <see cref="HttpRequestException"/> if the
    /// download is needed but fails.</summary>
    private async Task<string> AcquirePackageAsync(string tempDir, CancellationToken ct)
    {
        string zipPath = Path.Combine(tempDir, "VMulti.Driver.zip");

        if (BundledVMultiZip() is { } bundled)
        {
            StatusChanged?.Invoke("Preparing bundled VMulti driver...");
            File.Copy(bundled, zipPath, overwrite: true);
            return zipPath;
        }

        StatusChanged?.Invoke("Downloading VMulti driver...");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenTabletArtist/1.0");
        using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var fileStream = File.Create(zipPath);
        await response.Content.CopyToAsync(fileStream, ct);
        await fileStream.FlushAsync(ct);
        return zipPath;
    }

    /// <summary>
    /// Downloads and extracts the VMulti package, then installs the driver in-app: an elevated,
    /// hidden devcon call creates the root-enumerated <c>pentablet\hid</c> device from
    /// <c>vmulti.inf</c> (no self-elevating <c>install_hiddriver.bat</c> / visible cmd window, #111).
    /// Admin elevation (one UAC prompt) is required; a restart is recommended afterward.
    /// </summary>
    public async Task<InstallResult> InstallAsync(CancellationToken ct = default)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "VMultiInstall_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempDir);

            // Step 1: Get the VMulti package — the copy bundled next to the app (offline install), or
            // download it as a fallback (dev builds don't bundle it).
            ProgressChanged?.Invoke(10);
            string zipPath = await AcquirePackageAsync(tempDir, ct);
            ProgressChanged?.Invoke(40);

            // Step 2: Extract
            StatusChanged?.Invoke("Extracting driver files...");
            string extractDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            ProgressChanged?.Invoke(60);

            // Locate the driver folder (devcon.exe + vmulti.inf live together).
            string? inf = FindFile(extractDir, "vmulti.inf");
            if (inf == null)
                return new InstallResult(false, "Could not find vmulti.inf in the downloaded archive.");
            string driverDir = Path.GetDirectoryName(inf)!;

            // Write our own install script instead of the stock install_hiddriver.bat, which
            // self-elevates and pops a visible cmd window. devcon creates the root-enumerated
            // pentablet\hid device and installs the driver from vmulti.inf. No `/r` — we don't want to
            // auto-reboot the user's machine; we surface "restart recommended" instead.
            //
            // Both the devcon.exe path AND the vmulti.inf path use %~dp0 (the batch's own directory):
            // when we launch elevated via ShellExecute "runas", the UAC/AppInfo service commonly starts
            // the process in C:\Windows\System32 and ignores WorkingDirectory, so a bare "vmulti.inf"
            // wouldn't be found and devcon would fail with exit code 2. devcon output is teed to a log we
            // read back on failure so the error is diagnosable instead of a bare exit code.
            string logPath = Path.Combine(driverDir, "otd_install.log");
            string script = Path.Combine(driverDir, "otd_install_vmulti.bat");
            await File.WriteAllTextAsync(script, string.Join("\r\n", new[]
            {
                "@echo off",
                "\"%~dp0devcon.exe\" install \"%~dp0vmulti.inf\" \"pentablet\\hid\" > \"%~dp0otd_install.log\" 2>&1",
                "exit /b %errorlevel%",
                "",
            }), ct);

            // Step 3: Run the install once, elevated, with a hidden window (single UAC, no console).
            StatusChanged?.Invoke("Installing driver (admin required)...");
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
                return new InstallResult(false, "Failed to start the installer process.");

            await process.WaitForExitAsync(ct);
            ProgressChanged?.Invoke(100);

            // devcon: 0 = installed, 1 = installed but a reboot is needed; treat both as success and
            // recommend a restart (VMulti's virtual HID device only enumerates after one). Anything
            // else is a failure — the card re-detects the real state afterward.
            if (process.ExitCode is 0 or 1)
            {
                StatusChanged?.Invoke("VMulti driver installed.");
                return new InstallResult(true, "VMulti installed. A restart is recommended to finish setup.",
                    RebootRecommended: true);
            }

            StatusChanged?.Invoke("Installation may have failed.");
            string detail = ReadInstallerLog(logPath);
            return new InstallResult(false,
                $"The installer exited with code {process.ExitCode}. The driver may not be installed — check the VMulti status."
                + detail);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined UAC
            StatusChanged?.Invoke("Installation cancelled.");
            return new InstallResult(false, "Installation was cancelled (admin permission denied).");
        }
        catch (HttpRequestException)
        {
            return new InstallResult(false,
                "Couldn't download the VMulti driver. Check your internet connection and try again.");
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

            // Step 1: Get the VMulti package (we need its driver files + devcon). Bundled copy first,
            // download as a fallback.
            ProgressChanged?.Invoke(10);
            string zipPath = await AcquirePackageAsync(tempDir, ct);
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
                // Absolute %~dp0 paths — an elevated ShellExecute launch may start us in System32 and
                // ignore WorkingDirectory, so a bare "vmulti.inf" wouldn't be found (see InstallAsync).
                "\"%~dp0DIFxCmd.exe\" /u \"%~dp0vmulti.inf\"",
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

            var message = process.ExitCode == 0
                ? "VMulti was removed. A restart is recommended to finish cleaning it up."
                : "VMulti removal finished with warnings. Check Device Manager — the card will show the detected state.";
            StatusChanged?.Invoke("VMulti driver removed.");
            return new InstallResult(true, message, RebootRecommended: true);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusChanged?.Invoke("Uninstallation cancelled.");
            return new InstallResult(false, "Uninstallation was cancelled (admin permission denied).");
        }
        catch (HttpRequestException)
        {
            return new InstallResult(false,
                "Couldn't download the VMulti package. Check your internet connection and try again.");
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

    /// <summary>Best-effort read of the devcon log so a failure surfaces the real reason (missing INF,
    /// signature rejection, etc.) rather than a bare exit code. Returns "" if the log is absent/empty;
    /// otherwise a trimmed tail prefixed with two newlines, ready to append to the failure message.</summary>
    private static string ReadInstallerLog(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
                return "";
            string text = File.ReadAllText(logPath).Trim();
            if (text.Length == 0)
                return "";
            // Keep the message dialog-sized; devcon's failure reason is near the end of its output.
            const int max = 600;
            if (text.Length > max)
                text = "…" + text[^max..];
            return "\n\nDetails:\n" + text;
        }
        catch
        {
            return "";
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
