using System.Linq;
using System.Numerics;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class CalibrationProfileTests
{
    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    private static Matrix3x2 Sample => new(1.02f, 0.01f, -0.01f, 0.98f, 0.03f, -0.04f);

    [Fact]
    public void Write_ThenRead_RoundTrips()
    {
        var settings = SettingsFor("Tab");
        CalibrationProfile.Write(settings, "Tab", Sample, enable: true, fingerprint: "fp-1");

        var read = CalibrationProfile.Read(settings, "Tab");

        Assert.NotNull(read);
        Assert.True(read!.Enabled);
        Assert.Equal("fp-1", read.Fingerprint);
        Assert.Equal(Sample.M11, read.Transform.M11, 5);
        Assert.Equal(Sample.M22, read.Transform.M22, 5);
        Assert.Equal(Sample.M31, read.Transform.M31, 5);
        Assert.Equal(Sample.M32, read.Transform.M32, 5);
    }

    [Fact]
    public void Read_NoFilter_ReturnsNull()
    {
        Assert.Null(CalibrationProfile.Read(SettingsFor("Tab"), "Tab"));
        Assert.Null(CalibrationProfile.ReadProfile(null));
    }

    [Fact]
    public void Write_Disable_KeepsValuesButReportsDisabled()
    {
        var settings = SettingsFor("Tab");
        CalibrationProfile.Write(settings, "Tab", Sample, enable: false, fingerprint: "fp");
        var read = CalibrationProfile.Read(settings, "Tab");
        Assert.NotNull(read);
        Assert.False(read!.Enabled);
        Assert.Equal(Sample.M11, read.Transform.M11, 5);
    }

    [Fact]
    public void Clear_RemovesStore()
    {
        var settings = SettingsFor("Tab");
        CalibrationProfile.Write(settings, "Tab", Sample, enable: true, fingerprint: "fp");
        CalibrationProfile.Clear(settings, "Tab");
        Assert.Null(CalibrationProfile.Read(settings, "Tab"));
        Assert.DoesNotContain(settings.Profiles.First().Filters, f => f.Path == CalibrationProfile.FilterTypeName);
    }

    [Fact]
    public void Write_OrdersCalibrationBeforeDynamics_WhenDynamicsWrittenFirst()
    {
        var settings = SettingsFor("Tab");
        PressureCurveProfile.Write(settings, "Tab", PenDynamicsSettings.Default, enable: true);
        CalibrationProfile.Write(settings, "Tab", Sample, enable: true, fingerprint: "fp");

        var paths = settings.Profiles.First().Filters.Select(f => f.Path).ToList();
        Assert.True(paths.IndexOf(CalibrationProfile.FilterTypeName) < paths.IndexOf(PressureCurveProfile.FilterTypeName));
    }

    [Fact]
    public void Write_KeepsCalibrationBeforeDynamics_WhenCalibrationWrittenFirst()
    {
        var settings = SettingsFor("Tab");
        CalibrationProfile.Write(settings, "Tab", Sample, enable: true, fingerprint: "fp");
        PressureCurveProfile.Write(settings, "Tab", PenDynamicsSettings.Default, enable: true);

        var paths = settings.Profiles.First().Filters.Select(f => f.Path).ToList();
        Assert.True(paths.IndexOf(CalibrationProfile.FilterTypeName) < paths.IndexOf(PressureCurveProfile.FilterTypeName));
    }

    [Fact]
    public void Fingerprint_ChangesWithMapping()
    {
        var input = new MappingArea(50, 50, 100, 100);
        var output = new MappingArea(960, 540, 1920, 1080);
        var a = CalibrationProfile.Fingerprint(input, output, 1);
        var b = CalibrationProfile.Fingerprint(input with { Width = 90 }, output, 1);
        var c = CalibrationProfile.Fingerprint(input, output, 2);
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a, CalibrationProfile.Fingerprint(input, output, 1)); // stable
    }
}
