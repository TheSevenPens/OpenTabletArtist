using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ProfileFingerprintTests
{
    [Fact]
    public void Null_ReturnsEmpty()
    {
        Assert.Equal("", ProfileFingerprint.Compute(null));
    }

    [Fact]
    public void SameValues_ProduceSameFingerprint()
    {
        var a = new Profile { OutputMode = TestOtd.Output(), Tablet = "T" };
        var b = new Profile { OutputMode = TestOtd.Output(), Tablet = "T" };
        Assert.Equal(ProfileFingerprint.Compute(a), ProfileFingerprint.Compute(b));
    }

    [Fact]
    public void DifferentTablet_ProducesDifferentFingerprint()
    {
        var a = new Profile { OutputMode = TestOtd.Output(), Tablet = "T1" };
        var b = new Profile { OutputMode = TestOtd.Output(), Tablet = "T2" };
        Assert.NotEqual(ProfileFingerprint.Compute(a), ProfileFingerprint.Compute(b));
    }

    [Fact]
    public void DifferentMapping_ProducesDifferentFingerprint()
    {
        var a = new Profile
        {
            OutputMode = TestOtd.Output(),
            Tablet = "T",
            AbsoluteModeSettings = new AbsoluteModeSettings { Display = new AreaSettings(), Tablet = new AreaSettings { Width = 50, Height = 30, X = 25, Y = 15 } },
        };
        var b = new Profile
        {
            OutputMode = TestOtd.Output(),
            Tablet = "T",
            AbsoluteModeSettings = new AbsoluteModeSettings { Display = new AreaSettings(), Tablet = new AreaSettings { Width = 80, Height = 30, X = 25, Y = 15 } },
        };
        Assert.NotEqual(ProfileFingerprint.Compute(a), ProfileFingerprint.Compute(b));
    }

    [Fact]
    public void IsStableAcrossCalls()
    {
        var p = new Profile
        {
            OutputMode = TestOtd.Output(),
            Tablet = "T",
            AbsoluteModeSettings = new AbsoluteModeSettings { Display = new AreaSettings(), Tablet = new AreaSettings { Width = 50, Height = 30 } },
        };
        Assert.Equal(ProfileFingerprint.Compute(p), ProfileFingerprint.Compute(p));
    }
}
