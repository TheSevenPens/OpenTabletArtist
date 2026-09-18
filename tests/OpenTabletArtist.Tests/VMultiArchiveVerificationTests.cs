using System;
using System.IO;
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

    /// <summary>A dev build bundles no archive and so records no digest. There is nothing to verify
    /// against, and refusing every install in that case would break local testing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoRecordedDigest_SkipsVerification(string? expected)
    {
        Assert.True(VMultiInstaller.VerifyArchive(_file, expected));
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
}
