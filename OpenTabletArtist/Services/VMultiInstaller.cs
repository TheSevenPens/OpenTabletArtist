using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace OpenTabletArtist.Services;

public class VMultiInstaller
{
    private const string DownloadUrl =
        "https://github.com/X9VoiD/vmulti-bin/releases/download/v1.0/VMulti.Driver.zip";

    /// <summary>Name of the digest file the release workflow writes beside the bundled archive.</summary>
    public const string PackageDigestFileName = "VMulti.Driver.zip.sha256";

    public event Action<string>? StatusChanged;
    public event Action<int>? ProgressChanged;

    /// <summary>The VMulti package bundled next to the app in a release (<c>Bundled/VMulti.Driver.zip</c>),
    /// or null in a dev build that doesn't bundle it — in which case the installer downloads it instead.</summary>
    private static string? BundledVMultiZip()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Bundled", "VMulti.Driver.zip");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// SHA256 of the VMulti release asset <see cref="DownloadUrl"/> points at: the v1.0 archive,
    /// 1,989,586 bytes, containing devcon.exe / DIFxCmd.exe / vmulti.inf. Pinned here so there is an
    /// authoritative value at runtime rather than only in the release workflow (#769).
    ///
    /// The URL is pinned to a tag, but a release asset can be replaced in place, so the tag alone
    /// guarantees nothing about the bytes. This constant is what makes the download path verifiable — and
    /// the download path is the one a dev build and every fallback install take. Kept in step with
    /// release.yml's VMULTI_SHA256 by a test, because two copies of a security constant drift.
    /// </summary>
    public const string PinnedSha256 = "CC34F74A6BEE7F3D1FDC3C10AAE27118A359F56A51DE2F5965B7D0D3E353D3A1";

    /// <summary>
    /// The digest recorded beside the bundled archive at packaging time, or null when there isn't one to
    /// read. The release workflow pins the asset, verifies what it downloaded, and writes this file — so
    /// for a release the digest is fixed when the build is made, not taken from whatever the user's
    /// machine happens to fetch later (#739).
    /// </summary>
    private static string? ReadBundledDigest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Bundled", PackageDigestFileName);
        if (!File.Exists(path)) return null;
        try { return File.ReadAllText(path).Trim(); }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't read the bundled VMulti digest at {path}.", ex);
            return null;
        }
    }

    /// <summary>
    /// What the archive must hash to. Prefers the digest this build was packaged with; falls back to
    /// <see cref="PinnedSha256"/> when there is none to read.
    ///
    /// The fallback is the point of #769. It used to be "no digest, no check", which meant a dev build
    /// and a release whose digest file failed to read both extracted and ran the archive <b>elevated</b>
    /// without verifying anything — a silent pass in exactly the situation that warranted the loudest
    /// failure. There is now always something to check against.
    /// </summary>
    public static string ExpectedDigest() => ResolveExpectedDigest(ReadBundledDigest());

    /// <summary>The resolution rule on its own, so the fallback is testable without a packaged build.
    /// Blank counts as absent: an empty or whitespace digest file is a broken one, not a permissive one.</summary>
    public static string ResolveExpectedDigest(string? bundled) =>
        string.IsNullOrWhiteSpace(bundled) ? PinnedSha256 : bundled.Trim();

    /// <summary>SHA256 of a file as an uppercase hex string.</summary>
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>
    /// True only when <paramref name="path"/> hashes to <paramref name="expected"/>. Comparison is
    /// case-insensitive so either hex casing works.
    ///
    /// This runs before the archive is extracted and its contents executed <b>elevated</b>, which is why
    /// "it came from the right URL" was not enough on its own: a release asset can be replaced in place,
    /// and the bundled copy travels through a CI job and a zip before it gets here. For the same reason
    /// an absent <paramref name="expected"/> is a refusal, not a pass (#769) — a check that cannot be
    /// performed has not been passed, and callers should be getting one from
    /// <see cref="ExpectedDigest"/>, which always has a value.
    /// </summary>
    public static bool VerifyArchive(string path, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            AppLog.Error("No expected digest for the VMulti driver package, so there is nothing to " +
                         "verify it against. Refusing to install it.");
            return false;
        }


        try
        {
            var actual = ComputeSha256(path);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            AppLog.Error($"VMulti archive digest mismatch: expected {expected}, got {actual}. " +
                         "Refusing to install it.");
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Couldn't verify the VMulti archive at {path}.", ex);
            return false;
        }
    }

    /// <summary>Produces <c>VMulti.Driver.zip</c> in <paramref name="tempDir"/>: copies the bundled copy
    /// (offline install), or downloads it as a fallback. Verifies the result against the digest recorded
    /// at packaging time before handing it back (#739). Throws <see cref="HttpRequestException"/> if the
    /// download is needed but fails, or <see cref="InvalidDataException"/> if verification fails.</summary>
    private async Task<string> AcquirePackageAsync(string tempDir, CancellationToken ct)
    {
        string zipPath = Path.Combine(tempDir, "VMulti.Driver.zip");

        if (BundledVMultiZip() is { } bundled)
        {
            StatusChanged?.Invoke("Preparing bundled VMulti driver...");
            File.Copy(bundled, zipPath, overwrite: true);
        }
        else
        {
            StatusChanged?.Invoke("Downloading VMulti driver...");
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenTabletArtist/1.0");
            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var fileStream = File.Create(zipPath);
            await response.Content.CopyToAsync(fileStream, ct);
            await fileStream.FlushAsync(ct);
        }

        if (!VerifyArchive(zipPath, ExpectedDigest()))
            throw new InvalidDataException(
                "The VMulti driver package doesn't match the copy this build expects.");

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
        catch (InvalidDataException ex)
        {
            // Nothing was extracted or elevated — the check runs before either (#739).
            StatusChanged?.Invoke("Installation cancelled.");
            return new InstallResult(false, $"{ex.Message} Nothing was installed.");
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
            // Every step's output and exit code are teed to a log (as InstallAsync does), read back below.
            string logPath = Path.Combine(driverDir, RemovalLogName);
            string script = Path.Combine(driverDir, "otd_remove_vmulti.bat");
            await File.WriteAllTextAsync(script, string.Join("\r\n", BuildRemovalScript()), ct);

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

            // The temp folder (and this log with it) is deleted below, so keep a copy in the app log.
            string log = ReadLog(logPath);
            if (log.Length > 0) AppLog.Info("VMulti removal output:\n" + log);
            var summary = SummarizeRemovalLog(log);
            if (summary.HasProblems)
            {
                AppLog.Warn("VMulti removal: a step reported a problem." + summary.Details);
                StatusChanged?.Invoke("VMulti removal finished with warnings.");
                return new InstallResult(true,
                    "VMulti removal ran, but a step reported a problem — check the VMulti status. "
                    + "A restart is recommended to finish cleaning it up." + summary.Details,
                    RebootRecommended: true);
            }

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
        catch (HttpRequestException)
        {
            return new InstallResult(false,
                "Couldn't download the VMulti package. Check your internet connection and try again.");
        }
        catch (InvalidDataException ex)
        {
            StatusChanged?.Invoke("Removal cancelled.");
            return new InstallResult(false, $"{ex.Message} Nothing was removed.");
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

    internal const string RemovalLogName = "otd_remove.log";

    /// <summary>The removal script, one line per entry. The commands are the package's own
    /// <c>remove_hiddriver.bat</c> (DIFxCmd /u, then devcon remove <c>pentablet\hid</c>) plus one for the
    /// <c>djpnewton\vmulti</c> leftovers (#110). Each step's output and exit code go to the log, under a
    /// <c>=== label ===</c> header. The script always exits 0: devcon and DIFxCmd report non-zero when
    /// there was nothing left to remove, which is not a failure here — the card re-detects the real state
    /// afterwards, and <see cref="SummarizeRemovalLog"/> decides from what the tools printed.</summary>
    internal static string[] BuildRemovalScript()
    {
        // Absolute %~dp0 paths — an elevated ShellExecute launch may start us in System32 and ignore
        // WorkingDirectory, so a bare "vmulti.inf" wouldn't be found (see InstallAsync).
        (string Label, string Command)[] steps =
        [
            (@"DIFxCmd /u vmulti.inf", "\"%~dp0DIFxCmd.exe\" /u \"%~dp0vmulti.inf\""),
            (@"devcon remove pentablet\hid", "\"%~dp0devcon.exe\" remove \"pentablet\\hid\""),
            (@"devcon remove djpnewton\vmulti", "\"%~dp0devcon.exe\" remove \"djpnewton\\vmulti\""),
        ];

        var lines = new List<string> { "@echo off", $"set \"LOG=%~dp0{RemovalLogName}\"", "type nul > \"%LOG%\"" };
        foreach (var (label, command) in steps)
        {
            lines.Add($"echo === {label} === >> \"%LOG%\"");
            lines.Add($"{command} >> \"%LOG%\" 2>&1");
            lines.Add("echo exit=%errorlevel% >> \"%LOG%\"");
        }
        lines.Add("exit /b 0");
        lines.Add("");
        return lines.ToArray();
    }

    internal sealed record RemovalLogSummary(bool HasProblems, string Details);

    /// <summary>Decides from the tools' own output whether a removal step really failed. Exit codes cannot
    /// say: "nothing to remove" is non-zero too. A step has a problem when it prints an <c>ERROR…</c> line
    /// (DIFxCmd: <c>ERROR: failed with error code 0x…</c>) or a line containing "failed" (devcon:
    /// <c>Remove failed</c>, <c>Deleting the specified driver package from the machine failed</c>). Benign
    /// output such as devcon's <c>No matching devices found.</c> is not a problem. Details are the
    /// offending lines, each prefixed with its step, ready to append to a dialog message.</summary>
    internal static RemovalLogSummary SummarizeRemovalLog(string? log)
    {
        if (string.IsNullOrWhiteSpace(log)) return new(false, "");

        const int maxLines = 6, maxLineLength = 200;
        var problems = new List<string>();
        string step = "";
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("===") && line.EndsWith("===")) { step = line.Trim('=', ' '); continue; }
            if (!line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("failed", StringComparison.OrdinalIgnoreCase)) continue;
            if (problems.Count < maxLines)
                problems.Add((step.Length > 0 ? step + ": " : "")
                    + (line.Length > maxLineLength ? line[..maxLineLength] + "…" : line));
        }
        return problems.Count == 0
            ? new(false, "")
            : new(true, "\n\nDetails:\n" + string.Join("\n", problems));
    }

    /// <summary>The whole log, or "" if it is absent or unreadable. Unlike <see cref="ReadInstallerLog"/> it
    /// is not shortened or decorated: it goes to the app log, and the summary picks what to show.</summary>
    private static string ReadLog(string logPath)
    {
        try { return File.Exists(logPath) ? File.ReadAllText(logPath).Trim() : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static string? FindFile(string dir, string fileName)
    {
        foreach (var file in Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories))
            return file;
        return null;
    }
}

public record InstallResult(bool Success, string Message, bool RebootRecommended = false);
