using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class VMultiDetectorTests
{
    private const uint Disabled = 0x16; // CM_PROB_DISABLED (22)
    private const uint NoDriver = 28;   // CM_PROB_FAILED_INSTALL

    private static DeviceInfo Dev(uint problem, bool enabled = true)
        => new("djpnewton\\vmulti", "VMulti", enabled, problem);

    [Fact]
    public void NoDevices_NotInstalled()
        => Assert.False(VMultiDetector.ClassifySetupApi(System.Array.Empty<DeviceInfo>()).Installed);

    [Fact]
    public void AllFunctional_InstalledAndEnabled()
    {
        var r = VMultiDetector.ClassifySetupApi(new[] { Dev(0), Dev(0) });
        Assert.True(r.Installed);
        Assert.True(r.Enabled);
    }

    [Fact]
    public void AllDisabled_InstalledButDisabled()
    {
        var r = VMultiDetector.ClassifySetupApi(new[] { Dev(Disabled, enabled: false) });
        Assert.True(r.Installed);
        Assert.False(r.Enabled);
    }

    [Fact]
    public void OnlyDriverlessLeftovers_NotInstalled()
    {
        // The reported bug: after uninstall+reboot, 2 Code-28 nodes persist. They must NOT count as installed.
        var r = VMultiDetector.ClassifySetupApi(new[] { Dev(NoDriver), Dev(NoDriver) });
        Assert.False(r.Installed);
        Assert.False(r.Enabled);
        Assert.Contains("leftover", r.Message);
    }

    [Fact]
    public void FunctionalPlusLeftover_StillInstalled()
    {
        var r = VMultiDetector.ClassifySetupApi(new[] { Dev(0), Dev(NoDriver) });
        Assert.True(r.Installed);
    }
}
