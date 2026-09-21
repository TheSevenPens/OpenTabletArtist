using System;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Where to send someone whose machine has no .NET runtime for the daemon (#786, D2).
///
/// <para><b>Why a link and not an installer.</b> OTA used to download Microsoft's installer itself,
/// verify its Authenticode signature, and run it elevated — around 350 lines, a <c>WinVerifyTrust</c>
/// P/Invoke, and a publisher check, all so a button could do what a browser does natively. Observing it
/// on a clean machine (#878) is what settled it: the download reported no progress the user could trust,
/// there was no way to cancel once started (the call passed no <see cref="System.Threading.CancellationToken"/>),
/// and when OTA happened to be elevated already no UAC prompt appeared and the <c>/quiet</c> install ran
/// with no visible sign at all. The tester concluded it had hung and killed the app — after the install
/// had in fact succeeded. A browser reports progress, allows cancelling, and resumes; Windows prompts for
/// elevation when the file is run. None of that was ours to rebuild.</para>
///
/// <para><b>Why this URL.</b> It is the one the .NET host itself prints when it cannot find a runtime, so
/// OTA points at the remedy Microsoft already recommends for exactly this failure rather than inventing
/// its own. It redirects to the arch-correct installer page — "Download .NET 8.0 Runtime, Windows x64
/// Installer" — so there is nothing to choose. The generic download page is the thing to avoid: it offers
/// SDK, Runtime, Desktop Runtime and ASP.NET Core Runtime across architectures, and a wrong pick fails in
/// a way the user would blame on OTA.</para>
/// </summary>
public static class DotnetRuntimeDownload
{
    /// <summary>
    /// The architecture this process runs as, which is what the daemon must match. An x64 OTA needs the
    /// x64 runtime; an arm64 install would not satisfy it, and on arm64 Windows the reverse holds. The
    /// process architecture decides, not the OS.
    /// </summary>
    public static string ArchSuffix => Architecture(RuntimeInformation.ProcessArchitecture);

    /// <summary>Testable core of <see cref="ArchSuffix"/>.</summary>
    public static string Architecture(Architecture arch) => arch switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        _ => "x64",
    };

    /// <summary>The page to open for this machine.</summary>
    public static string Url => UrlFor(ArchSuffix, DotnetRuntime.DaemonMajor);

    /// <summary>
    /// Testable core of <see cref="Url"/>. <c>missing_runtime=true</c> is what selects the runtime rather
    /// than the SDK, and <c>arch</c>/<c>rid</c> are what make the landing page the right one first time.
    /// </summary>
    public static string UrlFor(string archSuffix, int major) =>
        "https://aka.ms/dotnet-core-applaunch?missing_runtime=true"
        + $"&arch={archSuffix}&rid=win-{archSuffix}&os=win10&apphost_version={major}.0.0";

    /// <summary>What the card says before the button is pressed: where it goes, and what to do there.</summary>
    public static string Description =>
        $"Opens Microsoft's download page for the .NET {DotnetRuntime.DaemonMajor} runtime ({ArchSuffix}) "
        + "in your browser. Run the installer it gives you, then come back and choose Check again.";
}
