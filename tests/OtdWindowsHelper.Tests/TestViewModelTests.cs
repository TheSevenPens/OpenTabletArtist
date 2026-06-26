using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class TestViewModelTests
{
    private sealed class NoopDebugSession : IDaemonDebugSession
    {
#pragma warning disable CS0067 // not exercised by these tests
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    private static TestViewModel NewVm(FakeDeviceData data) =>
        new(new NoopDebugSession(), data, new FakeDialogService());

    private static Profile DetectedProfileWithDynamics(string tablet, bool enabled)
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };
        PressureCurveProfile.Write(settings, tablet, PenDynamicsSettings.Default, enable: enabled);
        return settings.Profiles.First();
    }

    [Fact]
    public void NoProfiles_ReportsNotDetected()
    {
        var data = new FakeDeviceData { Profiles = new List<ProfileItem>() };
        using var vm = NewVm(data);

        data.RaiseDataLoaded();

        Assert.False(vm.TabletDetected);
        Assert.Equal("No tablet detected", vm.TabletStatusText);
        Assert.False(vm.DynamicsActive);
    }

    [Fact]
    public void DetectedProfile_ShowsName_NoDynamicsChipWhenDisabled()
    {
        var data = new FakeDeviceData
        {
            Profiles = new List<ProfileItem> { new(new Profile { Tablet = "Wacom CTL-472" }, IsDetected: true, LastSeen: null) }
        };
        using var vm = NewVm(data);

        data.RaiseDataLoaded();

        Assert.True(vm.TabletDetected);
        Assert.Equal("Wacom CTL-472", vm.TabletStatusText);
        Assert.False(vm.DynamicsActive);
    }

    [Fact]
    public void DetectedProfile_WithEnabledDynamics_ShowsChip()
    {
        var data = new FakeDeviceData
        {
            Profiles = new List<ProfileItem> { new(DetectedProfileWithDynamics("Wacom", enabled: true), IsDetected: true, LastSeen: null) }
        };
        using var vm = NewVm(data);

        data.RaiseDataLoaded();

        Assert.True(vm.DynamicsActive);
    }

    [Fact]
    public void DetectedProfile_WithDisabledDynamicsFilter_NoChip()
    {
        var data = new FakeDeviceData
        {
            Profiles = new List<ProfileItem> { new(DetectedProfileWithDynamics("Wacom", enabled: false), IsDetected: true, LastSeen: null) }
        };
        using var vm = NewVm(data);

        data.RaiseDataLoaded();

        Assert.False(vm.DynamicsActive);
    }

    // Pre-rename profiles still reference the legacy filter type; the chip must still light up (nit #2).
    [Fact]
    public void DetectedProfile_WithEnabledLegacyDynamicsFilter_ShowsChip()
    {
        var profile = new Profile { Tablet = "Wacom" };
        var legacy = JsonConvert.DeserializeObject<PluginSettingStore>("{}")!;
        legacy.Path = PressureCurveProfile.LegacyFilterTypeName;
        legacy.Enable = true;
        profile.Filters.Add(legacy);

        var data = new FakeDeviceData
        {
            Profiles = new List<ProfileItem> { new(profile, IsDetected: true, LastSeen: null) }
        };
        using var vm = NewVm(data);

        data.RaiseDataLoaded();

        Assert.True(vm.DynamicsActive);
    }

    [Fact]
    public void UndetectedProfilesPresent_StillReportsNotDetected()
    {
        var data = new FakeDeviceData
        {
            Profiles = new List<ProfileItem> { new(new Profile { Tablet = "Paired-Only" }, IsDetected: false, LastSeen: null) }
        };
        using var vm = NewVm(data);

        data.RaiseDataLoaded();

        Assert.False(vm.TabletDetected);
        Assert.Equal("No tablet detected", vm.TabletStatusText);
    }

    // #134: split canvas is off by default and drives the UniformGrid column count.
    [Fact]
    public void ShowSplit_DefaultsOff_AndDrivesColumnCount()
    {
        using var vm = NewVm(new FakeDeviceData());

        Assert.False(vm.ShowSplit);
        Assert.Equal(1, vm.SplitColumnCount);

        vm.ShowSplit = true;
        Assert.Equal(2, vm.SplitColumnCount);
    }

    [Fact]
    public void CurrentDynamics_ReflectsDetectedProfilesConfiguredCurve()
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "Wacom" } } };
        PressureCurveProfile.Write(settings, "Wacom",
            new PenDynamicsSettings(
                new PressureCurveSettings(0.5, 0, 1, 0, 1, PressureMinApproach.Clamp),
                PressureSmoothing: 0, PositionSmoothing: 0, SmoothAfterCurve: true),
            enable: false); // configured-but-disabled still previews
        var data = new FakeDeviceData
        {
            Profiles = new List<ProfileItem> { new(settings.Profiles.First(), IsDetected: true, LastSeen: null) }
        };
        using var vm = NewVm(data);

        data.RaiseDataLoaded();

        Assert.Equal(0.5, vm.CurrentDynamics.Curve.Softness, 5);
    }
}
