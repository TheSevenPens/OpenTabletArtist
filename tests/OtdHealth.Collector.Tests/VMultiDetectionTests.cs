namespace OtdHealth.Collector.Tests;

public class VMultiDetectionTests
{
    /// <summary>A minimal observation for tests that only need the verdict.</summary>
    internal static VMultiObservation Observation(bool installed) =>
        new(installed, new VMultiDetails("test", null, [], [], 0, 0, null, null, []));

    // The hardware ID the package OTA installs gives its node (devcon install vmulti.inf "pentablet\hid";
    // the INF lists only that model). Read off a real, working install: ROOT\HIDCLASS\0000, service vmulti,
    // problem code 0. The probe used to look only for djpnewton\vmulti, so this read as "not installed".
    [Fact]
    public void InstalledPackageHardwareId_IsRecognised()
        => Assert.Equal(@"pentablet\hid", VMultiDetector.MatchHardwareId([@"pentablet\hid"]));

    [Fact]
    public void UpstreamHardwareId_IsStillRecognised()
        => Assert.Equal(@"djpnewton\vmulti", VMultiDetector.MatchHardwareId([@"djpnewton\vmulti"]));

    [Fact]
    public void MatchIsCaseInsensitive_AndReturnsTheCanonicalId()
        => Assert.Equal(@"pentablet\hid", VMultiDetector.MatchHardwareId([@"PenTablet\HID"]));

    [Fact]
    public void AnyOfSeveralHardwareIds_Matches()
        => Assert.Equal(@"pentablet\hid", VMultiDetector.MatchHardwareId([@"ACPI\SOMETHING", @"pentablet\hid", @"*other"]));

    // The HID children of a working VMulti node and the stale "PenTablet" HID entries are NOT VMulti nodes:
    // counting them would report a driver for devices that are only its children (or its past self).
    [Theory]
    [InlineData(@"HID\hid&Col01")]
    [InlineData(@"HID\PenTablet&Col05")]
    [InlineData(@"HID\VID_00FF&UP:0001_U:0002")]
    [InlineData(@"pentablet")]
    [InlineData(@"vmulti")]
    [InlineData("")]
    public void UnrelatedIds_DoNotMatch(string id)
        => Assert.Null(VMultiDetector.MatchHardwareId([id]));

    [Fact]
    public void NoHardwareIds_DoNotMatch()
        => Assert.Null(VMultiDetector.MatchHardwareId([]));
}
