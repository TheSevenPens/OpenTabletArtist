namespace OtdHealth.Tests;

public class OtdVersionTests
{
    [Theory]
    [InlineData("0.6.7", "0.6.7.0", true)]
    [InlineData("0.6.7.9", "0.6.7.9", true)]
    [InlineData("0.6.8.1", "0.6.8.1+build", true)]
    [InlineData("0.6.8", "0.6.8.0", true)]
    [InlineData(" 0.6.7-beta ", "0.6.7", true)]
    [InlineData("1", "1.0.0", true)]
    [InlineData("1.2", "1.2.0", true)]
    [InlineData("0.6.6", "0.6.7", false)]
    [InlineData("0.6.8", "0.6.7", false)]
    [InlineData("0.6.7.9", "0.6.7+build", false)]  // the fourth part is a real difference
    [InlineData("0.6.8", "0.6.8.1", false)]        // OTD's 0.6.8.1 fixes a crash 0.6.8 has
    [InlineData("0.6.8.0", "0.6.8.1", false)]
    [InlineData("0.7.0", "0.6.7", false)]
    [InlineData("1.6.7", "0.6.7", false)]
    [InlineData("", "0.6.7", false)]
    [InlineData("unknown", "unknown", false)]
    [InlineData("v0.6.7", "0.6.7", false)]
    public void PreservesNumericReleaseComparison(string a, string b, bool same) =>
        Assert.Equal(same, OtdVersion.SameRelease(a, b));

    // The rule from #1042: the release (first three numbers) must match or the app does not attach; a
    // revision difference (the fourth) is tolerated but reported, an older one more strongly than a newer.
    [Theory]
    [InlineData("0.6.8", "0.6.8", DaemonVersionGate.Match)]
    [InlineData("0.6.8", "0.6.8.0", DaemonVersionGate.Match)]          // a missing part is 0
    [InlineData("0.6.8+build", "0.6.8.0", DaemonVersionGate.Match)]    // a suffix is ignored
    [InlineData("0.6.8.1", "0.6.8", DaemonVersionGate.NewerRevision)]
    [InlineData("0.6.8", "0.6.8.1", DaemonVersionGate.OlderRevision)]
    [InlineData("0.6.8.2", "0.6.8.9", DaemonVersionGate.OlderRevision)]
    [InlineData("0.6.7", "0.6.8", DaemonVersionGate.Mismatch)]         // patch differs: a different release
    [InlineData("0.6.9", "0.6.8", DaemonVersionGate.Mismatch)]
    [InlineData("0.7.0", "0.6.8", DaemonVersionGate.Mismatch)]
    [InlineData("1.6.8", "0.6.8", DaemonVersionGate.Mismatch)]
    [InlineData("0.6.7.9", "0.6.8.0", DaemonVersionGate.Mismatch)]     // a high revision does not make up a release
    [InlineData("", "0.6.8", DaemonVersionGate.Unreadable)]
    [InlineData("   ", "0.6.8", DaemonVersionGate.Unreadable)]
    [InlineData("unknown", "0.6.8", DaemonVersionGate.Unreadable)]
    [InlineData("v0.6.8", "0.6.8", DaemonVersionGate.Unreadable)]
    [InlineData("0.6.8", "", DaemonVersionGate.Unreadable)]            // no basis to compare against
    public void GatesADaemonVersionAgainstTheExpectedRelease(string daemon, string expected, DaemonVersionGate gate) =>
        Assert.Equal(gate, OtdVersion.Gate(daemon, expected));

    [Theory]
    [InlineData(DaemonVersionGate.Match, false)]
    [InlineData(DaemonVersionGate.NewerRevision, false)]
    [InlineData(DaemonVersionGate.OlderRevision, false)]
    [InlineData(DaemonVersionGate.Mismatch, true)]
    [InlineData(DaemonVersionGate.Unreadable, true)]
    public void OnlyAReleaseMismatchOrAnUnreadableVersionBlocks(DaemonVersionGate gate, bool blocks) =>
        Assert.Equal(blocks, OtdVersion.Blocks(gate));
}
