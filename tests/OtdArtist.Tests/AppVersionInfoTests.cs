using OtdArtist.Domain;
using Xunit;

namespace OtdArtist.Tests;

public class AppVersionInfoTests
{
    [Theory]
    [InlineData("0.6.0", "v0.6.0")]
    [InlineData("v0.6.0", "v0.6.0")]
    [InlineData("0.6.0+abc1234", "v0.6.0")]      // strip +build metadata
    [InlineData("1.2.3-beta+deadbeef", "v1.2.3-beta")]
    [InlineData("  1.2.3  ", "v1.2.3")]
    public void Format_NormalizesToVPrefixedNoMetadata(string input, string expected)
        => Assert.Equal(expected, AppVersionInfo.Format(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+onlymetadata")]
    public void Format_MissingOrEmpty_ReturnsPlaceholder(string? input)
        => Assert.Equal("v?", AppVersionInfo.Format(input));
}
