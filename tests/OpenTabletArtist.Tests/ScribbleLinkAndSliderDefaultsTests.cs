using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Two small ways back to a default or to a setting (#1044, #1048): the Scribble page's link to the pressure
/// tab, and the value each slider's reset button puts it back to.
/// </summary>
public class ScribbleLinkAndSliderDefaultsTests
{
    // --- Scribble's link to the pressure settings (#1044) -----------------------------------------------

    private sealed class NoopDebugSession : IDaemonDebugSession
    {
#pragma warning disable CS0067 // not exercised by these tests
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    private static ProfileItem Item(string tablet, bool detected) =>
        new(new Profile { OutputMode = TestOtd.Output(), Tablet = tablet }, IsDetected: detected, LastSeen: null);

    private static (TestViewModel Vm, FakeDeviceData Data, List<(string Tablet, TabletDetailTab Tab)> Opened) Scribble(
        bool withOpener = true, params ProfileItem[] profiles)
    {
        var data = new FakeDeviceData { Profiles = profiles.ToList() };
        var opened = new List<(string, TabletDetailTab)>();
        var vm = new TestViewModel(new NoopDebugSession(), data,
            openPen: withOpener ? (name, tab) => opened.Add((name, tab)) : null);
        data.RaiseDataLoaded();
        return (vm, data, opened);
    }

    /// <summary>The link goes to the pressure tab of the tablet the page is following.</summary>
    [Fact]
    public void TheLink_OpensThePressureTab_OfTheDetectedTablet()
    {
        var (vm, _, opened) = Scribble(true, Item("Wacom PTK-670", true));
        using var _ = vm;

        Assert.True(vm.OpenPressureCommand.CanExecute(null));
        vm.OpenPressureCommand.Execute(null);

        Assert.Equal([("Wacom PTK-670", TabletDetailTab.PenDynamics)], opened);
    }

    /// <summary>With two tablets plugged in, it is the one this page is on, which follows the active tablet.</summary>
    [Fact]
    public void TheLink_FollowsTheActiveTablet()
    {
        var data = new FakeDeviceData
        {
            Profiles = [Item("Wacom PTK-670", true), Item("Huion HS611", true)],
            ActiveTabletName = "Huion HS611",
        };
        var opened = new List<string>();
        using var vm = new TestViewModel(new NoopDebugSession(), data, openPen: (name, _) => opened.Add(name));
        data.RaiseDataLoaded();

        vm.OpenPressureCommand.Execute(null);

        Assert.Equal(["Huion HS611"], opened);
    }

    /// <summary>Nothing to open without a tablet: there is no Pen page for it.</summary>
    [Fact]
    public void WithNoTabletDetected_TheLinkDoesNothing()
    {
        var (vm, _, opened) = Scribble(true, Item("Wacom PTK-670", false));
        using var _ = vm;

        Assert.False(vm.OpenPressureCommand.CanExecute(null));
        vm.OpenPressureCommand.Execute(null);

        Assert.Empty(opened);
    }

    [Fact]
    public void TheLink_BecomesAvailable_WhenATabletIsPluggedIn()
    {
        var (vm, data, opened) = Scribble(true, Item("Wacom PTK-670", false));
        using var _ = vm;
        var changes = 0;
        vm.OpenPressureCommand.CanExecuteChanged += (_, _) => changes++;

        data.Profiles = [Item("Wacom PTK-670", true)];
        data.RaiseDataLoaded();

        Assert.True(changes > 0, "the command did not say its availability changed");
        Assert.True(vm.OpenPressureCommand.CanExecute(null));
        Assert.Empty(opened);
    }

    [Fact]
    public void WithoutAPlaceToGo_TheLinkIsNotAvailable()
    {
        var (vm, _, _) = Scribble(false, Item("Wacom PTK-670", true));
        using var _ = vm;

        Assert.False(vm.OpenPressureCommand.CanExecute(null));
    }

    // --- The values the sliders reset to (#1048) --------------------------------------------------------

    /// <summary>For the dynamics sliders OTA's recommendation and OTD's default are the same: a linear curve and no
    /// smoothing, which is a true no-op. A reset must land on exactly that, not on something that merely looks
    /// close.</summary>
    [Fact]
    public void TheDynamicsSliders_ResetToANoOp()
    {
        var vm = Detail();

        Assert.Equal(0, vm.SoftnessDefault);
        Assert.Equal(0, vm.DeadzoneDefault);
        Assert.Equal(0, vm.PressureSmoothingDefault);
        Assert.Equal(0, vm.PositionSmoothingDefault);
        Assert.True(PenDynamicsSettings.Default.IsNoOp);
    }

    /// <summary>The hover limit is the one slider where OTA has a recommendation and OTD has nothing: its own
    /// mid-range value, the one the setting starts on when first enabled.</summary>
    [Fact]
    public void TheHoverLimit_ResetsToOtasOwnRecommendation()
    {
        Assert.Equal(TabletDetailViewModel.DefaultMaxHoverDistance, Detail().MaxHoverDistanceDefault);
        Assert.Equal(127, Detail().MaxHoverDistanceDefault);
    }

    /// <summary>A default outside its slider would be clamped and written back. Every default sits inside the range
    /// its slider has.</summary>
    [Fact]
    public void EveryDefault_IsInsideItsSlidersRange()
    {
        var vm = Detail();

        Assert.InRange(vm.SoftnessDefault, -PressureCurveSettings.MaxSoftness, PressureCurveSettings.MaxSoftness);
        Assert.InRange(vm.DeadzoneDefault, 0, 1);
        Assert.InRange(vm.PressureSmoothingDefault, 0, vm.PressureSmoothingMax);
        Assert.InRange(vm.PositionSmoothingDefault, 0, vm.PositionSmoothingMax);
        Assert.InRange(vm.MaxHoverDistanceDefault, 1, vm.HoverDistanceMax);
    }

    private static TabletDetailViewModel Detail()
    {
        var profile = new Profile { Tablet = "Test Tablet", OutputMode = TestOtd.Output() };
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        return new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
    }
}
