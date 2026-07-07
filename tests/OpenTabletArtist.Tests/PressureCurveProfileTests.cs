using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PressureCurveProfileTests
{
    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    private static PenDynamicsSettings Dyn(
        double softness = 0, double inMin = 0, double inMax = 1, double outMin = 0, double outMax = 1,
        PressureMinApproach approach = PressureMinApproach.Clamp,
        double pSmooth = 0, double posSmooth = 0, bool smoothAfter = true)
        => new(new PressureCurveSettings(softness, inMin, inMax, outMin, outMax, approach),
               pSmooth, posSmooth, smoothAfter);

    [Fact]
    public void Read_NoFilter_ReturnsNull()
        => Assert.Null(PressureCurveProfile.Read(SettingsFor("Tab"), "Tab"));

    [Fact]
    public void Read_UnknownTablet_ReturnsNull()
        => Assert.Null(PressureCurveProfile.Read(SettingsFor("Tab"), "Other"));

    [Fact]
    public void Write_ThenRead_RoundTripsCurveSmoothingAndEnabled()
    {
        var settings = SettingsFor("Tab");
        var dyn = Dyn(0.4, 0.1, 0.9, 0.05, 0.95, PressureMinApproach.Cut,
                      pSmooth: 0.7, posSmooth: 0.3, smoothAfter: false);

        PressureCurveProfile.Write(settings, "Tab", dyn, enable: true);
        var read = PressureCurveProfile.Read(settings, "Tab");

        Assert.NotNull(read);
        Assert.True(read!.Value.Enabled);
        var d = read.Value.Dynamics;
        Assert.Equal(0.4, d.Curve.Softness, 5);
        Assert.Equal(0.1, d.Curve.InputMinimum, 5);
        Assert.Equal(0.9, d.Curve.InputMaximum, 5);
        Assert.Equal(0.05, d.Curve.Minimum, 5);
        Assert.Equal(0.95, d.Curve.Maximum, 5);
        Assert.Equal(PressureMinApproach.Cut, d.Curve.MinApproach);
        Assert.Equal(0.7, d.PressureSmoothing, 5);
        Assert.Equal(0.3, d.PositionSmoothing, 5);
        Assert.False(d.SmoothAfterCurve);
    }

    [Fact]
    public void Write_CreatesSingleStore_AndUpdatesInPlace()
    {
        var settings = SettingsFor("Tab");
        var profile = settings.Profiles.First();

        PressureCurveProfile.Write(settings, "Tab", PenDynamicsSettings.Default, enable: true);
        PressureCurveProfile.Write(settings, "Tab", Dyn(softness: 0.5, posSmooth: 0.2), enable: true);

        Assert.Single(profile.Filters, f => f.Path == PressureCurveProfile.FilterTypeName);
        var d = PressureCurveProfile.Read(settings, "Tab")!.Value.Dynamics;
        Assert.Equal(0.5, d.Curve.Softness, 5);
        Assert.Equal(0.2, d.PositionSmoothing, 5);
    }

    [Fact]
    public void Read_DefaultsSmoothAfterCurveTrue_WhenAbsent()
    {
        // A store written by the v0.2.0 schema (no smoothing keys) should read back as defaults.
        var settings = SettingsFor("Tab");
        PressureCurveProfile.Write(settings, "Tab", PenDynamicsSettings.Default, enable: true);
        var store = settings.Profiles.First().Filters.First(f => f.Path == PressureCurveProfile.FilterTypeName);
        var trimmed = store.Settings.Where(s => s.Property is not ("SmoothAfterCurve" or "PressureSmoothing" or "PositionSmoothing")).ToList();
        store.Settings = new System.Collections.ObjectModel.ObservableCollection<PluginSetting>(trimmed);

        var d = PressureCurveProfile.Read(settings, "Tab")!.Value.Dynamics;
        Assert.True(d.SmoothAfterCurve);
        Assert.Equal(0, d.PressureSmoothing);
        Assert.Equal(0, d.PositionSmoothing);
    }

    [Fact]
    public void Write_Disable_KeepsValuesButReportsDisabled()
    {
        var settings = SettingsFor("Tab");
        PressureCurveProfile.Write(settings, "Tab", Dyn(softness: 0.3), enable: false);

        var read = PressureCurveProfile.Read(settings, "Tab");
        Assert.NotNull(read);
        Assert.False(read!.Value.Enabled);
        Assert.Equal(0.3, read.Value.Dynamics.Curve.Softness, 5);
    }

    [Fact]
    public void Write_UnknownTablet_IsNoOp()
    {
        var settings = SettingsFor("Tab");
        PressureCurveProfile.Write(settings, "Other", PenDynamicsSettings.Default, enable: true);
        Assert.Empty(settings.Profiles.First().Filters);
    }

    [Fact]
    public void ReadProfile_NullOrNoFilter_ReturnsNull()
    {
        Assert.Null(PressureCurveProfile.ReadProfile(null));
        Assert.Null(PressureCurveProfile.ReadProfile(new Profile { Tablet = "Tab" }));
    }

    [Fact]
    public void ReadProfile_MatchesReadBySettings()
    {
        var settings = SettingsFor("Tab");
        var dyn = Dyn(0.4, 0.1, 0.9, 0.05, 0.95, PressureMinApproach.Cut, pSmooth: 0.7, posSmooth: 0.3, smoothAfter: false);
        PressureCurveProfile.Write(settings, "Tab", dyn, enable: true);

        var viaProfile = PressureCurveProfile.ReadProfile(settings.Profiles.First());

        Assert.NotNull(viaProfile);
        Assert.True(viaProfile!.Value.Enabled);
        var d = viaProfile.Value.Dynamics;
        Assert.Equal(0.4, d.Curve.Softness, 5);
        Assert.Equal(0.7, d.PressureSmoothing, 5);
        Assert.Equal(0.3, d.PositionSmoothing, 5);
        Assert.False(d.SmoothAfterCurve);
    }

    // ── EnsureEnabled: the "always on internally" invariant ──────────────────────

    [Fact]
    public void EnsureEnabled_AddsAnEnabledInertStore_WhenMissing()
    {
        var settings = SettingsFor("Tab");

        Assert.True(PressureCurveProfile.EnsureEnabled(settings)); // changed
        var read = PressureCurveProfile.Read(settings, "Tab");
        Assert.NotNull(read);
        Assert.True(read!.Value.Enabled);
        Assert.True(read.Value.Dynamics.IsNoOp);                    // inert until customized
    }

    [Fact]
    public void EnsureEnabled_ReEnablesADisabledStore_WithoutTouchingSettings()
    {
        var settings = SettingsFor("Tab");
        PressureCurveProfile.Write(settings, "Tab", Dyn(softness: 0.4, posSmooth: 0.2), enable: false);

        Assert.True(PressureCurveProfile.EnsureEnabled(settings)); // changed
        var read = PressureCurveProfile.Read(settings, "Tab")!.Value;
        Assert.True(read.Enabled);
        Assert.Equal(0.4, read.Dynamics.Curve.Softness, 5);        // preserved
        Assert.Equal(0.2, read.Dynamics.PositionSmoothing, 5);
    }

    [Fact]
    public void EnsureEnabled_NoChange_WhenAlreadyPresentAndEnabled()
    {
        var settings = SettingsFor("Tab");
        PressureCurveProfile.Write(settings, "Tab", Dyn(softness: 0.4), enable: true);

        Assert.False(PressureCurveProfile.EnsureEnabled(settings)); // idempotent, no churn
        Assert.Single(settings.Profiles.First().Filters, f => f.Path == PressureCurveProfile.FilterTypeName);
    }
}
