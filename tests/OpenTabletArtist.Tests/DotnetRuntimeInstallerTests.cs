using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The .NET runtime install (#786, D2). Covers the decisions — which installer, what an exit code means,
/// and whether a file is signed by who it claims — without installing anything.
///
/// The install itself cannot be tested here: it elevates, and the machine already has the runtime. That
/// gap is recorded on #786 and needs a clean Windows box.
/// </summary>
public class DotnetRuntimeInstallerTests
{
    // --- Which installer -------------------------------------------------------------------

    /// <summary>
    /// The architecture that matters is the <em>process</em>'s, not the OS's. An x64 OTA needs the x64
    /// runtime even on arm64 Windows, where x64 processes run under emulation and an arm64 runtime would
    /// not satisfy them.
    /// </summary>
    [Fact]
    public void TheDownloadMatchesTheProcessArchitecture()
    {
        var expected = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };

        Assert.Contains($"dotnet-runtime-win-{expected}.exe", DotnetRuntimeInstaller.DownloadUrl);
    }

    [Fact]
    public void TheDownloadIsHttpsToMicrosoftAndNamesTheDaemonsMajorVersion()
    {
        var url = DotnetRuntimeInstaller.DownloadUrl;

        Assert.StartsWith("https://aka.ms/dotnet/", url);
        Assert.Contains($"/{OpenTabletArtist.Domain.DotnetRuntime.DaemonMajor}.0/", url);
    }

    /// <summary>The card has to say what it is about to do before the user presses it: what it downloads,
    /// from whom, and that Windows will ask for administrator rights.</summary>
    [Fact]
    public void TheDescriptionSaysWhatItDownloadsAndThatItElevates()
    {
        var description = DotnetRuntimeInstaller.Description;

        Assert.Contains("Microsoft", description);
        Assert.Contains("administrator", description);
    }

    // --- What the installer's exit code means ----------------------------------------------

    [Fact]
    public void SuccessIsSuccess()
    {
        Assert.True(DotnetRuntimeInstaller.InterpretExitCode(0).Installed);
    }

    /// <summary>3010 means installed, but something it replaced is in use. The runtime is there either
    /// way, so reporting it as a failure would send the user to install what they already have.</summary>
    [Fact]
    public void ARebootRequestStillCountsAsInstalled()
    {
        var result = DotnetRuntimeInstaller.InterpretExitCode(3010);

        Assert.True(result.Installed);
        Assert.True(result.RebootRequired);
    }

    /// <summary>1638 is "a newer one of this major is already present", which is exactly the goal.</summary>
    [Fact]
    public void AlreadyPresentCountsAsInstalled()
    {
        Assert.True(DotnetRuntimeInstaller.InterpretExitCode(1638).Installed);
    }

    /// <summary>
    /// The distinction the review asked for: declining is an answer, not a fault. It must not be reported
    /// as a problem, and the caller must not treat it as something to retry.
    /// </summary>
    [Theory]
    [InlineData(1602)]   // installer reports user cancellation
    [InlineData(1223)]   // the same answer given to the elevation prompt
    public void DecliningIsNotAFailure(int exitCode)
    {
        var result = DotnetRuntimeInstaller.InterpretExitCode(exitCode);

        Assert.False(result.Installed);
        Assert.True(result.Cancelled);
        Assert.Null(result.Problem);   // nothing to show as an error
    }

    [Fact]
    public void AnyOtherCodeIsAReportedFailure()
    {
        var result = DotnetRuntimeInstaller.InterpretExitCode(1603);

        Assert.False(result.Installed);
        Assert.False(result.Cancelled);
        Assert.Contains("1603", result.Problem);
    }

    // --- The signature check ----------------------------------------------------------------

    /// <summary>
    /// The real installer's certificate, observed by downloading it: its common name is <b>".NET"</b>, and
    /// only the organisation says Microsoft. An earlier version of this check looked for "Microsoft" in the
    /// common name and would have rejected every genuine download — a failure that looks exactly like
    /// tamper detection working correctly, and that a user would report as "the download is broken", if
    /// they reported it at all.
    /// </summary>
    [Fact]
    public void TheRealInstallersSubjectIsAccepted()
    {
        const string real = "CN=.NET, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";

        Assert.True(DotnetRuntimeInstaller.IsMicrosoftSigner(real));
    }

    [Theory]
    [InlineData("CN=Contoso Installer, O=Contoso Ltd, C=GB")]
    [InlineData("CN=Microsoft, O=Definitely Not Microsoft, C=XX")]   // the CN is not the trustworthy part
    [InlineData("")]
    public void AnythingElseIsRejected(string subject)
    {
        Assert.False(DotnetRuntimeInstaller.IsMicrosoftSigner(subject));
    }

    /// <summary>An unsigned file reports no signer, which is what makes the installer refuse to run it.</summary>
    [Fact]
    public void AnUnsignedFileHasNoSigner()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Path.Combine(Path.GetTempPath(), "ota-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllText(path, "not an executable at all");

            Assert.Null(DotnetRuntimeInstaller.SignerName(path));
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort temp cleanup */ }
        }
    }
}
