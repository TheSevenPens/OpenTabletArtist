using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

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
        new(new NoopDebugSession(), data);

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

    // --- Following the app-wide active tablet ---

    /// <summary>
    /// Scribble re-targets when the active tablet changes somewhere else. This used to be one of two
    /// paths — the page also had its own switcher — and it is now the only one: the switcher moved to the
    /// shell (#697), so this subscription is what makes picking a tablet up there change what Scribble
    /// shows. The picker's own tests went with it.
    /// </summary>
    [Fact]
    public void ActiveTabletChangedElsewhere_RetargetsThePage()
    {
        var data = new FakeDeviceData
        {
            Profiles = new List<ProfileItem>
            {
                new(new Profile { Tablet = "Wacom PTH-660" }, IsDetected: true, LastSeen: null),
                new(new Profile { Tablet = "XP-Pen Deco L" }, IsDetected: true, LastSeen: null),
            },
            ActiveTabletName = "Wacom PTH-660",
        };
        using var vm = NewVm(data);
        data.RaiseDataLoaded();
        Assert.Equal("Wacom PTH-660", vm.TabletStatusText);

        // The shell's switcher moves the app-wide selection; the page follows without being told directly.
        data.RaiseActiveTabletChanged("XP-Pen Deco L");

        Assert.Equal("XP-Pen Deco L", vm.TabletStatusText);
        Assert.True(vm.TabletDetected);
    }
}
