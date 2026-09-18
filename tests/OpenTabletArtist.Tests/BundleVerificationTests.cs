using System;
using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The packaged-bundle check the release runs against its own output (#741). It goes through the app's
/// real lookup paths, so these tests are also a guard on those: if the daemon or plugin location
/// changes and this isn't updated, the release stops verifying what it actually ships.
/// </summary>
public class BundleVerificationTests : IDisposable
{
    private readonly string _dir;

    public BundleVerificationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ota-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>A layout with every component present and consistent, built the way a release is.</summary>
    private void WriteCompleteBundle()
    {
        Write(Path.Combine("Daemon", DaemonExePaths.DaemonExeName), "daemon");
        Write(Path.Combine("BundledPlugins", "OpenTabletArtistDynamics", "OpenTabletArtist.Dynamics.dll"), "dll");

        var otd = WindowsInkPluginService.OtdVersion;
        Write(Path.Combine("BundledPlugins", "WindowsInk", "metadata.json"),
            $$"""{"Name":"Windows Ink","PluginVersion":"1.0.0","SupportedDriverVersion":"{{otd.Major}}.{{otd.Minor}}.0"}""");

        Write(Path.Combine("Bundled", "VMulti.Driver.zip"), "pretend archive");
        var digest = VMultiInstaller.ComputeSha256(Path.Combine(_dir, "Bundled", "VMulti.Driver.zip"));
        Write(Path.Combine("Bundled", VMultiInstaller.PackageDigestFileName), digest);
    }

    [Fact]
    public void ACompleteBundle_PassesEveryCheck()
    {
        WriteCompleteBundle();

        var checks = BundleVerification.Run(_dir);

        Assert.NotEmpty(checks);
        Assert.All(checks, c => Assert.True(c.Ok, $"{c.Name} failed: {c.Detail}"));
    }

    [Fact]
    public void AnEmptyDirectory_FailsAndNamesEveryMissingComponent()
    {
        var checks = BundleVerification.Run(_dir);

        Assert.Contains(checks, c => !c.Ok && c.Name.Contains("daemon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, c => !c.Ok && c.Name.Contains("Dynamics", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, c => !c.Ok && c.Name.Contains("Windows Ink", StringComparison.Ordinal));
        Assert.Contains(checks, c => !c.Ok && c.Name.Contains("VMulti", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingDaemon_IsReportedWithWhatItCosts()
    {
        WriteCompleteBundle();
        File.Delete(Path.Combine(_dir, "Daemon", DaemonExePaths.DaemonExeName));

        var daemon = BundleVerification.Run(_dir).Single(c => c.Name.Contains("daemon", StringComparison.OrdinalIgnoreCase));

        Assert.False(daemon.Ok);
        Assert.NotNull(daemon.Detail);
    }

    /// <summary>Presence is not enough: the installer refuses an incompatible plugin, so shipping one
    /// means shipping a dead offline path (#739).</summary>
    [Fact]
    public void AWindowsInkPluginForAnotherDriverLine_Fails()
    {
        WriteCompleteBundle();
        var otd = WindowsInkPluginService.OtdVersion;
        Write(Path.Combine("BundledPlugins", "WindowsInk", "metadata.json"),
            $$"""{"Name":"Windows Ink","PluginVersion":"9.0.0","SupportedDriverVersion":"{{otd.Major}}.{{otd.Minor + 1}}.0"}""");

        var winInk = BundleVerification.Run(_dir).Single(c => c.Name.Contains("Windows Ink", StringComparison.Ordinal));

        Assert.False(winInk.Ok);
        Assert.Contains("does not support", winInk.Detail ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void ACorruptWindowsInkManifest_Fails()
    {
        WriteCompleteBundle();
        Write(Path.Combine("BundledPlugins", "WindowsInk", "metadata.json"), "{ truncated");

        var winInk = BundleVerification.Run(_dir).Single(c => c.Name.Contains("Windows Ink", StringComparison.Ordinal));

        Assert.False(winInk.Ok);
    }

    [Fact]
    public void AVMultiDigestThatDoesNotMatchTheArchive_Fails()
    {
        WriteCompleteBundle();
        Write(Path.Combine("Bundled", "VMulti.Driver.zip"), "different bytes entirely");

        var digest = BundleVerification.Run(_dir).Single(c => c.Name.Contains("digest", StringComparison.OrdinalIgnoreCase));

        Assert.False(digest.Ok);
        Assert.Contains("refused", digest.Detail ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void AVMultiArchiveWithNoRecordedDigest_Fails()
    {
        WriteCompleteBundle();
        File.Delete(Path.Combine(_dir, "Bundled", VMultiInstaller.PackageDigestFileName));

        var digest = BundleVerification.Run(_dir).Single(c => c.Name.Contains("digest", StringComparison.OrdinalIgnoreCase));

        Assert.False(digest.Ok);
    }

    [Fact]
    public void TheReport_NamesEveryProblem_NotJustTheFirst()
    {
        var report = BundleVerification.Report(BundleVerification.Run(_dir));

        Assert.Contains("MISSING", report, StringComparison.Ordinal);
        Assert.Contains("Bundled daemon", report, StringComparison.Ordinal);
        Assert.Contains("Pen Dynamics plugin", report, StringComparison.Ordinal);
        Assert.Contains("missing or inconsistent", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReport_SaysSoWhenEverythingIsPresent()
    {
        WriteCompleteBundle();

        var report = BundleVerification.Report(BundleVerification.Run(_dir));

        Assert.Contains("present and consistent", report, StringComparison.Ordinal);
        Assert.DoesNotContain("MISSING", report, StringComparison.Ordinal);
    }
}
