using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class PressureCurveProfileTests
{
    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    [Fact]
    public void Read_NoFilter_ReturnsNull()
        => Assert.Null(PressureCurveProfile.Read(SettingsFor("Tab"), "Tab"));

    [Fact]
    public void Read_UnknownTablet_ReturnsNull()
        => Assert.Null(PressureCurveProfile.Read(SettingsFor("Tab"), "Other"));

    [Fact]
    public void Write_ThenRead_RoundTripsCurveAndEnabled()
    {
        var settings = SettingsFor("Tab");
        var curve = new PressureCurveSettings(0.4, 0.1, 0.9, 0.05, 0.95, PressureMinApproach.Cut);

        PressureCurveProfile.Write(settings, "Tab", curve, enable: true);
        var read = PressureCurveProfile.Read(settings, "Tab");

        Assert.NotNull(read);
        Assert.True(read!.Value.Enabled);
        var c = read.Value.Curve;
        Assert.Equal(0.4, c.Softness, 5);
        Assert.Equal(0.1, c.InputMinimum, 5);
        Assert.Equal(0.9, c.InputMaximum, 5);
        Assert.Equal(0.05, c.Minimum, 5);
        Assert.Equal(0.95, c.Maximum, 5);
        Assert.Equal(PressureMinApproach.Cut, c.MinApproach);
    }

    [Fact]
    public void Write_CreatesSingleStore_AndUpdatesInPlace()
    {
        var settings = SettingsFor("Tab");
        var profile = settings.Profiles.First();

        PressureCurveProfile.Write(settings, "Tab", PressureCurveSettings.Default, enable: true);
        PressureCurveProfile.Write(settings, "Tab", new PressureCurveSettings(0.5, 0, 1, 0, 1, PressureMinApproach.Clamp), enable: true);

        Assert.Single(profile.Filters, f => f.Path == PressureCurveProfile.FilterTypeName);
        Assert.Equal(0.5, PressureCurveProfile.Read(settings, "Tab")!.Value.Curve.Softness, 5);
    }

    [Fact]
    public void Write_Disable_KeepsValuesButReportsDisabled()
    {
        var settings = SettingsFor("Tab");
        PressureCurveProfile.Write(settings, "Tab", new PressureCurveSettings(0.3, 0, 1, 0, 1, PressureMinApproach.Clamp), enable: false);

        var read = PressureCurveProfile.Read(settings, "Tab");
        Assert.NotNull(read);
        Assert.False(read!.Value.Enabled);
        Assert.Equal(0.3, read.Value.Curve.Softness, 5);
    }

    [Fact]
    public void Write_UnknownTablet_IsNoOp()
    {
        var settings = SettingsFor("Tab");
        PressureCurveProfile.Write(settings, "Other", PressureCurveSettings.Default, enable: true);
        Assert.Empty(settings.Profiles.First().Filters);
    }
}
