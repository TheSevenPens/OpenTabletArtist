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
/// (#769). Microsoft's runtime installer is not fixed: the same URL serves whatever patch is current, and
/// pinning would mean shipping an installer that goes stale and eventually 404s.</para>
/// <para>What replaces the pin is <b>Authenticode verification</b> — the file's signature is checked
/// against its bytes and its certificate chain to a trusted root, and only then is the publisher
/// examined. A changing URL does not need a pinned digest if the signature check is real.</para>
/// <para>An earlier version of this checked only that the embedded certificate <em>named</em> Microsoft,
/// which verified nothing: a tampered file carrying a copied certificate passes that, because reading a
/// certificate is not validating a signature. It also leaned on Windows re-checking at the elevation
/// prompt, which is true but is not OTA's check to delegate — by then the user has already been asked to
/// run the thing.</para>
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
            if (!IsAuthenticodeValid(installer))
                return Result.Failed("The downloaded installer's signature isn't valid; refusing to run it.");
            if (SignerName(installer) is not { } signer)
                return Result.Failed("The downloaded installer isn't signed; refusing to run it.");
            if (!IsMicrosoftSigner(signer))
                return Result.Failed($"The downloaded installer is signed by \"{signer}\", not Microsoft; "
                                     + "refusing to run it.");

            StatusChanged?.Invoke("Installing (Windows will ask for administrator rights)…");
            return await RunInstallerAsync(installer, ct);
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

    /// <summary>ERROR_CANCELLED — the user answered "no" to the elevation prompt.</summary>
    private const int ElevationDeclined = 1223;

    /// <summary>
    /// Runs the installer elevated and interprets what it says. <c>runas</c> is what produces the UAC
    /// prompt — and with it the publisher Windows has verified.
    ///
    /// Waits asynchronously. This is awaited from a UI command whose continuations resume on the UI
    /// thread, so a synchronous wait froze the whole app for the length of an install.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static async Task<Result> RunInstallerAsync(string installer, CancellationToken ct)
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
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ElevationDeclined)
        {
            // Specifically "user said no to UAC". Not a failure to report or retry — they answered.
            return Result.Declined;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Anything else failed to start, and reading that as consent would hide a real problem
            // behind a shrug — the user would see nothing at all.
            AppLog.Warn($"Couldn't start the .NET runtime installer (Win32 {ex.NativeErrorCode}).", ex);
            return Result.Failed($"The .NET runtime installer couldn't be started: {ex.Message}");
        }

        if (proc == null) return Result.Failed("The .NET runtime installer didn't start.");

        using (proc)
        {
            await proc.WaitForExitAsync(ct);
            return InterpretExitCode(proc.ExitCode);
        }
    }

    /// <summary>
    /// Whether Windows itself considers the file's Authenticode signature valid: the signature checked
    /// against the bytes, and the certificate chain to a trusted root.
    ///
    /// This is the check; <see cref="SignerName"/> only says who claims to have signed it, which is
    /// meaningless on its own. Revocation is not checked — it needs the network and fails awkwardly
    /// behind captive portals or offline, and the chain validation is what matters here.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsAuthenticodeValid(string path)
    {
        var file = new WinTrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = Marshal.StringToCoTaskMemUni(path),
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };
        var pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(file, pFile, false);

        var data = new WinTrustData
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            dwUIChoice = WTD_UI_NONE,
            fdwRevocationChecks = WTD_REVOKE_NONE,
            dwUnionChoice = WTD_CHOICE_FILE,
            pFile = pFile,
            dwStateAction = WTD_STATEACTION_VERIFY,
        };
        var pData = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
        Marshal.StructureToPtr(data, pData, false);

        var action = WinTrustActionGenericVerifyV2;
        try
        {
            var result = WinVerifyTrust(IntPtr.Zero, ref action, pData);

            // Always run the CLOSE pass: VERIFY allocates state that leaks otherwise.
            var closing = Marshal.PtrToStructure<WinTrustData>(pData);
            closing.dwStateAction = WTD_STATEACTION_CLOSE;
            Marshal.StructureToPtr(closing, pData, false);
            WinVerifyTrust(IntPtr.Zero, ref action, pData);

            return result == 0;   // anything else: unsigned, tampered, or an untrusted chain
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't verify the signature of {path}.", ex);
            return false;   // couldn't check means don't run it
        }
        finally
        {
            Marshal.FreeCoTaskMem(file.pcwszFilePath);
            Marshal.FreeCoTaskMem(pFile);
            Marshal.FreeCoTaskMem(pData);
        }
    }

    private static Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
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
