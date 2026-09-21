using System;
using System.Runtime.InteropServices;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The link OTA sends someone to when their machine has no runtime for the daemon (#878).
///
/// These replace the installer's tests. OTA no longer downloads or runs anything here, so there is no
/// signature to verify and no exit code to interpret — what is left to get wrong is the URL, and getting
/// it wrong sends someone to the wrong architecture or to a page listing four things they must choose
/// between. That is what these pin.
/// </summary>
public class DotnetRuntimeDownloadTests
{
    [Theory]
    [InlineData(Architecture.X64, "x64")]
    [InlineData(Architecture.Arm64, "arm64")]
    [InlineData(Architecture.X86, "x86")]
    public void ArchitectureNamesTheProcessArchitecture(Architecture arch, string expected)
    {
        Assert.Equal(expected, DotnetRuntimeDownload.Architecture(arch));
    }

    /// <summary>
    /// An architecture we have no name for must still produce a usable link. x64 is the right guess on
    /// Windows, and a link to the common case beats a malformed one.
    /// </summary>
    [Fact]
    public void AnUnknownArchitectureFallsBackToX64()
    {
        Assert.Equal("x64", DotnetRuntimeDownload.Architecture(Architecture.Wasm));
    }

    /// <summary>
    /// <c>missing_runtime=true</c> is what makes the page offer the runtime rather than the SDK, and the
    /// arch parameters are what make it the right download without the user choosing. Losing either turns
    /// this into the generic download page, where picking "SDK" or "ASP.NET Core Runtime" is an easy
    /// mistake that fails later and looks like OTA's fault.
    /// </summary>
    [Fact]
    public void TheUrlSelectsTheRuntimeForThisArchitecture()
    {
        var url = DotnetRuntimeDownload.UrlFor("arm64", 8);

        Assert.Contains("missing_runtime=true", url);
        Assert.Contains("arch=arm64", url);
        Assert.Contains("rid=win-arm64", url);
        Assert.Contains("apphost_version=8.0.0", url);
    }

    /// <summary>The major version is not hard-coded twice: it follows the daemon's target.</summary>
    [Fact]
    public void TheUrlFollowsTheDaemonsMajorVersion()
    {
        Assert.Contains($"apphost_version={DotnetRuntime.DaemonMajor}.0.0", DotnetRuntimeDownload.Url);
    }

    /// <summary>
    /// PlatformShell.OpenUrl opens https links and refuses everything else, so a link that is not https
    /// is a button that silently does nothing.
    /// </summary>
    [Fact]
    public void TheUrlIsHttpsSoTheShellWillOpenIt()
    {
        Assert.StartsWith("https://", DotnetRuntimeDownload.Url);
        Assert.True(Uri.TryCreate(DotnetRuntimeDownload.Url, UriKind.Absolute, out _));
    }

    /// <summary>
    /// The description is what the user reads before pressing. It has to say that a browser opens and
    /// that they run the installer themselves — the whole point of the change is that OTA no longer does
    /// it for them, and a description implying otherwise recreates the confusion it removed.
    /// </summary>
    [Fact]
    public void TheDescriptionSaysWhatTheUserWillHaveToDo()
    {
        var d = DotnetRuntimeDownload.Description;

        Assert.Contains("browser", d, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Check again", d, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(DotnetRuntime.DaemonMajor.ToString(), d);
    }
}
