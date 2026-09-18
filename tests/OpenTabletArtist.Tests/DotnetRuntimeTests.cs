using System;
using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The .NET runtime prerequisite (#786, D2). OTA is self-contained and needs no runtime; OpenTabletDriver's
/// official Windows release is framework-dependent, so driving it makes one a prerequisite.
///
/// Only the rules are covered here, not the machine. That is the point: the interesting cases are "no .NET
/// at all" and "the wrong .NET", and this machine — like every CI runner — has the right one. Anything
/// asserted against the local install would pass for the wrong reason.
/// </summary>
public class DotnetRuntimeTests
{
    // --- What the host's exit code means ---------------------------------------------------

    /// <summary>
    /// Verified empirically rather than read off a table: a framework-dependent daemon pointed at an
    /// empty <c>DOTNET_ROOT</c> prints "You must install .NET to run this application" and exits
    /// <c>0x80008083</c>. That is the no-.NET-at-all case.
    /// </summary>
    [Fact]
    public void TheHostsMissingRuntimeExitCodesAreRecognised()
    {
        Assert.True(DotnetRuntime.IsMissingRuntimeExit(unchecked((int)0x80008083)));  // no host library
        Assert.True(DotnetRuntime.IsMissingRuntimeExit(unchecked((int)0x80008096)));  // no matching framework
    }

    /// <summary>Unix reports only the low byte of an exit status, so the same two failures arrive as 131
    /// and 150. Missing these would make the check silently Windows-only.</summary>
    [Fact]
    public void TheTruncatedUnixFormsAreRecognisedToo()
    {
        Assert.True(DotnetRuntime.IsMissingRuntimeExit(131));
        Assert.True(DotnetRuntime.IsMissingRuntimeExit(150));
    }

    [Theory]
    [InlineData(0)]     // healthy
    [InlineData(1)]     // ordinary failure
    [InlineData(-1)]
    [InlineData(139)]   // SIGSEGV — a crash, not a missing runtime
    public void OtherExitCodesAreNotReadAsAMissingRuntime(int exitCode)
    {
        Assert.False(DotnetRuntime.IsMissingRuntimeExit(exitCode));
    }

    // --- Which installed runtimes actually satisfy the daemon -------------------------------

    /// <summary>
    /// The correction that matters, and the one most likely to be got wrong: .NET's default roll-forward
    /// moves forward <em>within</em> a major version. A net8.0 app is not satisfied by .NET 9 or 10, so a
    /// machine kept scrupulously up to date can fail this while looking perfectly healthy.
    /// </summary>
    [Fact]
    public void ANewerMajorDoesNotSatisfyTheDaemon()
    {
        var installed = new[] { new Version(9, 0, 0), new Version(10, 0, 1) };

        Assert.False(DotnetRuntime.Satisfies(installed, DotnetRuntime.DaemonMajor));
    }

    [Fact]
    public void AnOlderMajorDoesNotSatisfyItEither()
    {
        Assert.False(DotnetRuntime.Satisfies([new Version(6, 0, 36)], DotnetRuntime.DaemonMajor));
    }

    [Fact]
    public void AnyPatchOfTheRightMajorSatisfiesIt()
    {
        // Roll-forward does cover patch and minor within the major, so 8.0.0 is as good as 8.0.29.
        Assert.True(DotnetRuntime.Satisfies([new Version(8, 0, 0)], DotnetRuntime.DaemonMajor));
        Assert.True(DotnetRuntime.Satisfies([new Version(8, 0, 29)], DotnetRuntime.DaemonMajor));
    }

    [Fact]
    public void TheRightMajorAlongsideOthersIsEnough()
    {
        var installed = new[] { new Version(6, 0, 36), new Version(8, 0, 14), new Version(10, 0, 0) };

        Assert.True(DotnetRuntime.Satisfies(installed, DotnetRuntime.DaemonMajor));
    }

    [Fact]
    public void NothingInstalledSatisfiesNothing()
    {
        Assert.False(DotnetRuntime.Satisfies([], DotnetRuntime.DaemonMajor));
    }

    // --- Reading the versions off disk ------------------------------------------------------

    [Fact]
    public void VersionFoldersAreParsed()
    {
        var versions = DotnetRuntime.ParseVersions(["8.0.14", "6.0.36", "10.0.0"]);

        Assert.Equal([new Version(8, 0, 14), new Version(6, 0, 36), new Version(10, 0, 0)], versions);
    }

    [Fact]
    public void PreviewFoldersKeepTheirNumericVersion()
    {
        // "8.0.0-rc.1" is an installed 8.0 runtime, and refusing to read it would tell someone to install
        // what they already have.
        Assert.Equal([new Version(8, 0, 0)], DotnetRuntime.ParseVersions(["8.0.0-rc.1"]));
    }

    [Fact]
    public void UnparseableNamesAreSkippedRatherThanThrowing()
    {
        // A stray file in the shared-framework directory is not a reason to report no .NET.
        var versions = DotnetRuntime.ParseVersions(["readme.txt", "8.0.14", ""]);

        Assert.Equal([new Version(8, 0, 14)], versions);
    }

    [Fact]
    public void AMissingRootReportsNothingRatherThanThrowing()
    {
        var absent = Path.Combine(Path.GetTempPath(), "ota-no-dotnet-" + Guid.NewGuid().ToString("N"));

        Assert.Empty(DotnetRuntime.Installed([absent]));
    }

    /// <summary>
    /// Reads a real directory layout, built here rather than borrowed from the machine — the local
    /// install is the one shape that cannot exercise the interesting cases.
    /// </summary>
    [Fact]
    public void RuntimesAreReadFromTheSharedFrameworkFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ota-dotnet-" + Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(root, "shared", DotnetRuntime.SharedFramework);
        try
        {
            Directory.CreateDirectory(Path.Combine(shared, "8.0.14"));
            Directory.CreateDirectory(Path.Combine(shared, "10.0.0"));

            var installed = DotnetRuntime.Installed([root]);

            Assert.Contains(new Version(8, 0, 14), installed);
            Assert.True(DotnetRuntime.Satisfies(installed, DotnetRuntime.DaemonMajor));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    [Fact]
    public void ARootWithOnlyTheWrongMajorDoesNotSatisfyTheDaemon()
    {
        var root = Path.Combine(Path.GetTempPath(), "ota-dotnet-" + Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(root, "shared", DotnetRuntime.SharedFramework);
        try
        {
            Directory.CreateDirectory(Path.Combine(shared, "10.0.0"));

            Assert.False(DotnetRuntime.Satisfies(DotnetRuntime.Installed([root]), DotnetRuntime.DaemonMajor));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
