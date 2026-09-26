namespace OtdHealth.Tests;

public class OtdVersionTests
{
    [Theory]
    [InlineData("0.6.7", "0.6.7.0", true)]
    [InlineData("0.6.7.9", "0.6.7+build", true)]
    [InlineData(" 0.6.7-beta ", "0.6.7", true)]
    [InlineData("1", "1.0.0", true)]
    [InlineData("1.2", "1.2.0", true)]
    [InlineData("0.6.6", "0.6.7", false)]
    [InlineData("0.6.8", "0.6.7", false)]
    [InlineData("0.7.0", "0.6.7", false)]
    [InlineData("1.6.7", "0.6.7", false)]
    [InlineData("", "0.6.7", false)]
    [InlineData("unknown", "unknown", false)]
    [InlineData("v0.6.7", "0.6.7", false)]
    public void PreservesNumericReleaseComparison(string a, string b, bool same) =>
        Assert.Equal(same, OtdVersion.SameRelease(a, b));
}
