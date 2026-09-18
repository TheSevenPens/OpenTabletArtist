using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Installs the .NET runtime OpenTabletDriver's official release needs (#786, D2).
///
/// Windows only. macOS gets a self-contained OTD artifact and Linux gets it from a distribution package,
/// so neither has this prerequisite — it exists because OTD's Windows release is framework-dependent
/// where OTA is self-contained.
///
/// <para><b>Why there is no pinned digest here, unlike every other download OTA makes.</b> The VMulti
/// archive and the OTD release are fixed artifacts, so OTA pins their hashes and refuses anything else
/// (#769). Microsoft's runtime installer is not fixed: the same URL serves whatever patch is current
/// (8.0.31 today), and pinning would mean shipping an installer that goes stale and eventually
/// disappears. The protection is different in kind:</para>
/// <list type="bullet">
/// <item>HTTPS to a Microsoft-controlled host;</item>
/// <item>the file is checked for an Authenticode signature naming Microsoft before OTA offers to run it,
/// which catches a wrong or tampered file before the user is asked to elevate;</item>
/// <item>and Windows itself validates that signature when the elevation prompt appears, showing the
/// verified publisher. A tampered installer reaches the user as "Unknown publisher", which is a
/// stronger signal than anything OTA could print.</item>
/// </list>
/// <para>That is a deliberate position rather than an omission, and it should be revisited if Microsoft
/// ever publishes per-release hashes at a stable location.</para>
/// </summary>
public sealed class DotnetRuntimeInstaller
{
    /// <summary>Progress messages for the card, same shape as <see cref="OtdInstaller"/>.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Download progress, 0-100.</summary>
    public event Action<int>? ProgressChanged;

    /// <summary>
    /// What happened. <see cref="Cancelled"/> is kept apart from <see cref="Problem"/> deliberately: a
    /// user who declines the elevation prompt has answered the question, and the caller must not treat
    /// that as a failure to retry (#786 review, point 4).
    /// </summary>
    public sealed record Result(bool Installed, string? Problem, bool Cancelled = false, bool RebootRequired = false)
    {
        public static readonly Result Ok = new(true, null);
        public static readonly Result NeedsReboot = new(true, null, RebootRequired: true);
        public static readonly Result Declined = new(false, null, Cancelled: true);
        public static Result Failed(string problem) => new(false, problem);
    }

    /// <summary>
    /// The installer for the architecture this process runs as. An x64 OTA needs the x64 runtime: an
    /// arm64 install would not satisfy it, and on arm64 Windows the reverse is equally true, so the
    /// process architecture — not the OS — is what decides.
    /// </summary>
    public static string DownloadUrl =>
        $"https://aka.ms/dotnet/{DotnetRuntime.DaemonMajor}.0/dotnet-runtime-win-{ArchSuffix}.exe";

    private static string ArchSuffix => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64",
    };

    /// <summary>What the card tells the user before they press anything: what it downloads, from whom,
    /// and that it will ask for administrator rights.</summary>
    public static string Description =>
        $"Downloads the .NET {DotnetRuntime.DaemonMajor} runtime installer ({ArchSuffix}) from Microsoft "
        + "and runs it. Windows will ask for administrator rights and show Microsoft as the publisher.";

    public async Task<Result> InstallAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return Result.Failed("The .NET runtime installer is only used on Windows.");

        var temp = Path.Combine(Path.GetTempPath(), "ota-dotnet-" + Guid.NewGuid().ToString("N")[..8]);
        var installer = Path.Combine(temp, "dotnet-runtime.exe");
        try
        {
            Directory.CreateDirectory(temp);

            StatusChanged?.Invoke("Downloading the .NET runtime from Microsoft…");
            var downloadProblem = await DownloadAsync(installer, ct);
            if (downloadProblem != null) return Result.Failed(downloadProblem);

            StatusChanged?.Invoke("Checking the installer's signature…");
            if (SignerName(installer) is not { } signer)
                return Result.Failed("The downloaded installer isn't signed; refusing to run it.");
            if (!IsMicrosoftSigner(signer))
                return Result.Failed($"The downloaded installer is signed by \"{signer}\", not Microsoft; "
                                     + "refusing to run it.");

            StatusChanged?.Invoke("Installing (Windows will ask for administrator rights)…");
            return RunInstaller(installer);
        }
        catch (OperationCanceledException)
        {
            return Result.Declined;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Couldn't install the .NET runtime.", ex);
            return Result.Failed($"Couldn't install the .NET runtime: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
            catch { /* best-effort temp cleanup */ }
        }
    }

    private async Task<string?> DownloadAsync(string destination, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenTabletArtist/1.0");

        using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return $"Couldn't download the .NET runtime ({(int)response.StatusCode} from Microsoft).";

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;
            if (total > 0) ProgressChanged?.Invoke((int)(written * 100 / total));
        }
        await file.FlushAsync(ct);
        return null;
    }

    /// <summary>
    /// Whether a certificate subject belongs to Microsoft.
    ///
    /// Matches on the <b>organisation</b>, not the common name, because the real installer's certificate
    /// is <c>CN=.NET, O=Microsoft Corporation, L=Redmond, S=Washington, C=US</c> — its common name is
    /// ".NET". Checking the common name for "Microsoft" rejects the genuine installer, which is a failure
    /// mode that looks like tamper detection working and never gets reported as a bug, because the person
    /// hitting it concludes their download was broken.
    /// </summary>
    public static bool IsMicrosoftSigner(string subject) =>
        subject.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The full subject of the installer's Authenticode certificate, or null when it has none.
    ///
    /// The whole distinguished name rather than a friendly name, because the organisation is the part
    /// worth trusting and it only appears there.
    ///
    /// This reads the embedded certificate; it does not by itself prove the file is untampered, and it
    /// finds nothing on a catalog-signed file (most Windows system binaries are signed that way — the
    /// redistributables OTA downloads are not). The verification that does prove it is Windows', when the
    /// elevation prompt appears; this is the cheap check that stops OTA asking the user to elevate
    /// something obviously wrong.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? SignerName(string path)
    {
        try
        {
            // SYSLIB0057 points at X509CertificateLoader, which loads a certificate *file*. There is no
            // non-obsolete API for pulling the Authenticode certificate out of a signed executable, which
            // is what this needs, so the suppression is narrow and deliberate rather than deferred
            // maintenance.
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch
        {
            return null;   // unsigned, or the signature can't be read at all
        }
    }

    /// <summary>
    /// Runs the installer elevated and interprets what it says. <c>runas</c> is what produces the UAC
    /// prompt — and with it the publisher Windows has verified.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Result RunInstaller(string installer)
    {
        Process? proc;
        try
        {
            proc = Process.Start(new ProcessStartInfo(installer)
            {
                // Quiet, because the user already agreed on OTA's card; no restart, because deciding to
                // reboot someone's machine is not OTA's call.
                Arguments = "/install /quiet /norestart",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The canonical "user said no to UAC". Not a failure to report or retry — they answered.
            return Result.Declined;
        }

        if (proc == null) return Result.Failed("The .NET runtime installer didn't start.");

        proc.WaitForExit();
        return InterpretExitCode(proc.ExitCode);
    }

    /// <summary>Exit codes from the installer bundle. Separated out so the mapping is testable without
    /// installing anything.</summary>
    public static Result InterpretExitCode(int exitCode) => exitCode switch
    {
        0 => Result.Ok,
        // Installed, but something it replaced is in use. The runtime is present either way.
        3010 => Result.NeedsReboot,
        // 1602 is "user cancelled"; 1223 is the same answer given to the elevation prompt.
        1602 or 1223 => Result.Declined,
        // A newer runtime of this major version is already there, which is exactly what we wanted.
        1638 => Result.Ok,
        _ => Result.Failed($"The .NET runtime installer failed (exit code {exitCode})."),
    };
}
