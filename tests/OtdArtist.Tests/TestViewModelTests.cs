using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdArtist.Domain;
using OtdArtist.Services;
using OtdArtist.ViewModels;
using Xunit;

namespace OtdArtist.Tests;

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
        => ProfileWithDynamics(tablet, PenDynamicsSettings.Default, enabled);

    private static Profile ProfileWithDynamics(string tablet, PenDynamicsSettings dyn, bool enabled)
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };
        PressureCurveProfile.Write(settings, tablet, dyn, enable: enabled);
        return settings.Profiles.First();
    }

    private static FakeDeviceData DetectedWith(Profile profile) => new()
    {
        Profiles = new List<ProfileItem> { new(profile, IsDetected: true, LastSeen: null) }
    };

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

    // #133: the Test "Dynamics" button opens the focused dynamics-only editor for the detected tablet.
    [Fact]
    public async Task OpenDynamics_OpensDynamicsOnlyDialog_ForDetectedProfile()
    {
        var profile = new Profile { Tablet = "Wacom" };
        var data = new FakeDeviceData { Profiles = new List<ProfileItem> { new(profile, IsDetected: true, LastSeen: null) } };
        var dialogs = new FakeDialogService();
        using var vm = new TestViewModel(new NoopDebugSession(), data, dialogs);

        await vm.OpenDynamicsCommand.ExecuteAsync(null);

        Assert.Same(profile, dialogs.ShownProfile);
        Assert.True(dialogs.ShownDynamicsOnly);
    }

    // --- #184: spell out which dynamics aspects are altering the pen ---

    [Fact]
    public void EnabledDynamics_WithCurveAndPressureSmoothing_FlagsExactlyThose()
    {
        var dyn = PenDynamicsSettings.Default with
        {
            Curve = PressureCurveSettings.Default with { Softness = 0.4 },
            PressureSmoothing = 0.5,
        };
        var data = DetectedWith(ProfileWithDynamics("Wacom", dyn, enabled: true));
        using var vm = NewVm(data);
        data.RaiseDataLoaded();

        Assert.True(vm.DynamicsActive);
        Assert.True(vm.CurveActive);
        Assert.True(vm.PressureSmoothingActive);
        Assert.False(vm.PositionSmoothingActive);
        Assert.False(vm.DynamicsNoOp);
    }

    [Fact]
    public void EnabledDynamics_AtDefaults_IsNoOp_WithNoAspectFlags()
    {
        var data = DetectedWith(DetectedProfileWithDynamics("Wacom", enabled: true));
        using var vm = NewVm(data);
        data.RaiseDataLoaded();

        Assert.True(vm.DynamicsActive);
        Assert.True(vm.DynamicsNoOp);
        Assert.False(vm.CurveActive);
        Assert.False(vm.PressureSmoothingActive);
        Assert.False(vm.PositionSmoothingActive);
    }

    [Fact]
    public void DisabledDynamics_ClearsAllAspectFlags()
    {
        var dyn = PenDynamicsSettings.Default with { Curve = PressureCurveSettings.Default with { Softness = 0.4 } };
        var data = DetectedWith(ProfileWithDynamics("Wacom", dyn, enabled: false));
        using var vm = NewVm(data);
        data.RaiseDataLoaded();

        Assert.False(vm.DynamicsActive);
        Assert.False(vm.CurveActive);
        Assert.False(vm.DynamicsNoOp);
    }

    // --- #183: pointer-only mode hides dynamics ---

    [Fact]
    public void PointerOnlyWithDynamics_TrueOnlyInPointerOnlyModeWithDynamicsActive()
    {
        var data = DetectedWith(DetectedProfileWithDynamics("Wacom", enabled: true));
        using var vm = NewVm(data);
        data.RaiseDataLoaded();
        Assert.True(vm.DynamicsActive);

        vm.BrushMode = PenBrushMode.PressureToSize;
        Assert.False(vm.PointerOnlyWithDynamics);

        vm.BrushMode = PenBrushMode.PointerOnly;
        Assert.True(vm.PointerOnlyWithDynamics);
    }

    [Fact]
    public async Task OpenDynamics_FromPointerOnly_SwitchesToPressureMode()
    {
        var profile = new Profile { Tablet = "Wacom" };
        var dialogs = new FakeDialogService();
        using var vm = new TestViewModel(new NoopDebugSession(), DetectedWith(profile), dialogs)
        {
            BrushMode = PenBrushMode.PointerOnly,
        };

        await vm.OpenDynamicsCommand.ExecuteAsync(null);

        Assert.Equal(PenBrushMode.PressureToSize, vm.BrushMode);
    }

    // --- #190 phase 3: active-tablet picker + targeting ---

    [Fact]
    public void Picker_HiddenWithOneTablet_ShownWithMore()
    {
        var one = new FakeDeviceData { DetectedTablets = new List<DetectedTablet> { new("A", "", "", "") } };
        using (var vm = NewVm(one)) { one.RaiseDataLoaded(); Assert.False(vm.ShowTabletPicker); }

        var two = new FakeDeviceData
        {
            DetectedTablets = new List<DetectedTablet> { new("A", "", "", ""), new("B", "", "", "") },
            ActiveTabletName = "A",
        };
        using var vm2 = NewVm(two);
        two.RaiseDataLoaded();
        Assert.True(vm2.ShowTabletPicker);
        Assert.Equal(new[] { "A", "B" }, vm2.TabletNames);
        Assert.Equal("A", vm2.SelectedTablet);
    }

    [Fact]
    public void SettingSelectedTablet_UpdatesActiveTablet()
    {
        var data = new FakeDeviceData
        {
            DetectedTablets = new List<DetectedTablet> { new("A", "", "", ""), new("B", "", "", "") },
            ActiveTabletName = "A",
        };
        using var vm = NewVm(data);

        vm.SelectedTablet = "B";

        Assert.Equal("B", data.ActiveTabletName);
    }

    [Fact]
    public async Task OpenDynamics_TargetsActiveTablet_NotJustTheFirstDetected()
    {
        var first = new Profile { Tablet = "A" };
        var second = new Profile { Tablet = "B" };
        var data = new FakeDeviceData
        {
            Profiles = new List<ProfileItem> { new(first, IsDetected: true, LastSeen: null), new(second, IsDetected: true, LastSeen: null) },
            DetectedTablets = new List<DetectedTablet> { new("A", "", "", ""), new("B", "", "", "") },
            ActiveTabletName = "B",
        };
        var dialogs = new FakeDialogService();
        using var vm = new TestViewModel(new NoopDebugSession(), data, dialogs);

        await vm.OpenDynamicsCommand.ExecuteAsync(null);

        Assert.Same(second, dialogs.ShownProfile);
    }
}
