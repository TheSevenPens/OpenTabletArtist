using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The VMulti archive is extracted and its contents run <b>elevated</b>, so it is verified against the
/// digest recorded when the release was built, before anything is unpacked (#739). Previously the only
/// check was that the download came from the expected URL — and a release asset can be replaced in place.
/// </summary>
public class VMultiArchiveVerificationTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public VMultiArchiveVerificationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ota-vmulti-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "VMulti.Driver.zip");
        File.WriteAllText(_file, "pretend driver package");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void MatchingDigest_Verifies()
    {
        var digest = VMultiInstaller.ComputeSha256(_file);

        Assert.True(VMultiInstaller.VerifyArchive(_file, digest));
    }

    [Fact]
    public void DigestComparison_IsCaseInsensitive()
    {
        var digest = VMultiInstaller.ComputeSha256(_file);

        Assert.True(VMultiInstaller.VerifyArchive(_file, digest.ToLowerInvariant()));
    }

    /// <summary>The case that matters: the bytes are not the ones this build was released with.</summary>
    [Fact]
    public void TamperedArchive_FailsVerification()
    {
        var digest = VMultiInstaller.ComputeSha256(_file);
        File.WriteAllText(_file, "something else entirely");

        Assert.False(VMultiInstaller.VerifyArchive(_file, digest));
    }

    [Fact]
    public void TruncatedArchive_FailsVerification()
    {
        var digest = VMultiInstaller.ComputeSha256(_file);
        File.WriteAllText(_file, "pretend driver pack");   // one byte short of the original

        Assert.False(VMultiInstaller.VerifyArchive(_file, digest));
    }

    [Fact]
    public void MissingArchive_FailsVerification()
    {
        var digest = VMultiInstaller.ComputeSha256(_file);
        File.Delete(_file);

        Assert.False(VMultiInstaller.VerifyArchive(_file, digest));
    }

    [Fact]
    public void ComputeSha256_IsStableAndUppercaseHex()
    {
        var first = VMultiInstaller.ComputeSha256(_file);
        var second = VMultiInstaller.ComputeSha256(_file);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(first.ToUpperInvariant(), first);
    }

    /// <summary>
    /// The change #769 made. A dev build bundles no archive and so records no digest — which used to mean
    /// verification was skipped and the package was extracted and run elevated unchecked. A check that
    /// cannot be performed has not been passed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoRecordedDigest_FailsVerification(string? expected)
    {
        Assert.False(VMultiInstaller.VerifyArchive(_file, expected));
    }

    /// <summary>...and the reason refusing is now affordable: there is always a digest to check against,
    /// so a dev build verifies its download instead of being waved through.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoBundledDigest_TheExpectedDigestIsThePinnedOne(string? bundled)
    {
        Assert.Equal(VMultiInstaller.PinnedSha256, VMultiInstaller.ResolveExpectedDigest(bundled));
    }

    [Fact]
    public void ABundledDigest_TakesPrecedenceOverThePinnedOne()
    {
        // A release records what it was actually packaged with, which is the stronger claim: it covers
        // the bundled copy's trip through a CI job and a zip, not just the upstream asset.
        var packaged = new string('A', 64);

        Assert.Equal(packaged, VMultiInstaller.ResolveExpectedDigest("  " + packaged + "\n"));
    }

    [Fact]
    public void AnUnreadableDigestFile_FallsBackToThePinnedDigest_NotToTrust()
    {
        // Stand-in for a digest file that exists but can't be read: reading a directory as a file throws
        // on every platform. The resolution must not turn that into "nothing to check".
        var unreadable = ReadDigestFileOrNull(_dir);

        Assert.Null(unreadable);
        Assert.Equal(VMultiInstaller.PinnedSha256, VMultiInstaller.ResolveExpectedDigest(unreadable));
        Assert.False(VMultiInstaller.VerifyArchive(_file, VMultiInstaller.ResolveExpectedDigest(unreadable)));
    }

    /// <summary>Mirrors the installer's own read: absent or unreadable both come back as null.</summary>
    private static string? ReadDigestFileOrNull(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch { return null; }
    }

    /// <summary>
    /// The pinned digest exists in two places — this constant and release.yml's VMULTI_SHA256 — because
    /// the workflow verifies the asset before bundling it and the app verifies it before installing it.
    /// Two copies of a security constant drift, and a drifted one fails closed at install time on a
    /// user's machine rather than in CI. So: check them against each other here.
    /// </summary>
    [Fact]
    public void ThePinnedDigest_MatchesTheOneTheReleaseWorkflowVerifiesAgainst()
    {
        var workflow = Path.Combine(RepoRoot(), ".github", "workflows", "release.yml");
        Assert.True(File.Exists(workflow), $"Couldn't find release.yml (looked at {workflow}).");

        var match = Regex.Match(File.ReadAllText(workflow), @"VMULTI_SHA256:\s*""([0-9A-Fa-f]{64})""");
        Assert.True(match.Success, "release.yml no longer pins VMULTI_SHA256 as a 64-character hex literal.");
        Assert.Equal(VMultiInstaller.PinnedSha256, match.Groups[1].Value, ignoreCase: true);
    }

    /// <summary>
    /// Derived from this file's own compile-time path, not from the binary's location: the test binaries
    /// are built to a redirected output directory that has no repository above it.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
