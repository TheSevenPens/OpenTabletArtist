using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// A way back to a setting's default (#1048) and a way from the Scribble canvas to the pressure settings (#1044).
/// </summary>
public class SliderResetTests
{
    // --- The control ------------------------------------------------------------------------------------

    private static (Window Window, LabeledSlider Slider) Show(double value, double? defaultValue,
        double min = 0, double max = 1, string label = "Softness")
    {
        var slider = new LabeledSlider
        {
            Label = label,
            Minimum = min,
            Maximum = max,
            Value = value,
            DefaultValue = defaultValue,
            ValueFormat = "0.00",
        };
        return (HarnessTests.Show(slider), slider);
    }

    private static Button Reset(LabeledSlider slider) =>
        slider.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Reset");

    /// <summary>A slider with no default draws no reset, so the Theme sliders look exactly as they did.</summary>
    [AvaloniaFact]
    public void WithNoDefault_ThereIsNoResetButton()
    {
        var (window, slider) = Show(0.4, null);

        Assert.False(Reset(slider).IsEffectivelyVisible);
        window.Close();
    }

    /// <summary>The button is there before you have moved the slider and after, and it is only enabled once the value
    /// is off the default. Something that appears under your hand as you drag is worse than an icon that is
    /// quietly unavailable.</summary>
    [AvaloniaFact]
    public void WithADefault_TheButtonIsAlwaysThere_AndOnlyEnabledOffTheDefault()
    {
        var (window, slider) = Show(0, defaultValue: 0);
        var button = Reset(slider);

        Assert.True(button.IsEffectivelyVisible);
        Assert.False(button.IsEnabled);

        slider.Value = 0.4;
        Dispatcher.UIThread.RunJobs();

        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEnabled);

        slider.Value = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.True(button.IsEffectivelyVisible);
        Assert.False(button.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingIt_PutsTheValueBackToTheDefault()
    {
        var (window, slider) = Show(0.6, defaultValue: 0.1);

        PenButtonCardTests.Click(window, Reset(slider));

        Assert.Equal(0.1, slider.Value, 6);
        Assert.False(Reset(slider).IsEnabled);
        window.Close();
    }

    /// <summary>A default outside the slider's range is clamped to it, rather than set and then clamped back by the slider.</summary>
    [AvaloniaFact]
    public void ADefaultOutsideTheRange_IsClampedToIt()
    {
        var (window, slider) = Show(5, defaultValue: 200, min: 1, max: 10);

        PenButtonCardTests.Click(window, Reset(slider));

        Assert.Equal(10, slider.Value, 6);
        window.Close();
    }

    /// <summary>The tip and the screen-reader name say what it will do: which setting, and to what.</summary>
    [AvaloniaFact]
    public void TheButton_SaysWhatItResets_AndToWhat()
    {
        var (window, slider) = Show(0.4, defaultValue: 0.25, label: "Pressure smoothing");

        Assert.Equal("Reset pressure smoothing to 0.25", slider.ResetTip);
        Assert.Equal("Reset pressure smoothing to 0.25", AutomationProperties.GetName(Reset(slider)));
        window.Close();
    }

    // --- On the real pages ------------------------------------------------------------------------------

    private static TabletDetailViewModel Vm(double softness = 0, double pressureSmoothing = 0, double positionSmoothing = 0)
    {
        var profile = new Profile
        {
            Tablet = "Test Tablet",
            OutputMode = new PluginSettingStore("OpenTabletDriver.Desktop.Output.Windows.WindowsInk")
            { Path = "VoiDPlugins.OutputMode.WindowsInkAbsoluteMode" },
        };
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        PressureCurveProfile.Write(settings, "Test Tablet",
            new PenDynamicsSettings(new PressureCurveSettings(softness, 0, 1, 0, 1, PressureMinApproach.Clamp),
                pressureSmoothing, positionSmoothing, true), enable: true);
        return new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
    }

    private static LabeledSlider Named(Control root, string label) =>
        root.GetVisualDescendants().OfType<LabeledSlider>().Single(s => s.Label == label);

    /// <summary>The pressure tab's three sliders each reset to a no-op, through the view model, so what the curve and
    /// the saved settings hold follows the click.</summary>
    [AvaloniaFact]
    public void ThePressureSliders_EachResetThroughTheViewModel()
    {
        var vm = Vm(softness: 0.5, pressureSmoothing: 0.3);
        var view = new DynamicsView { DataContext = vm };
        var window = HarnessTests.Show(view);

        foreach (var label in new[] { "Softness", "Pressure smoothing" })
        {
            var slider = Named(view, label);
            Assert.True(slider.HasDefault, $"{label} has no reset");
            Assert.True(Reset(slider).IsEffectivelyVisible);
            Assert.True(Reset(slider).IsEnabled, $"{label} is not off its default");
        }

        PenButtonCardTests.Click(window, Reset(Named(view, "Softness")));
        PenButtonCardTests.Click(window, Reset(Named(view, "Pressure smoothing")));

        Assert.Equal(0, vm.Softness, 6);
        Assert.Equal(0, vm.PressureSmoothing, 6);
        Assert.False(Reset(Named(view, "Softness")).IsEnabled);
        Assert.True(Named(view, "Deadzone").HasDefault);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePositionSmoothingSlider_ResetsToo()
    {
        var vm = Vm(positionSmoothing: 0.2);
        var view = new PenDetailView { DataContext = vm };
        var window = HarnessTests.Show(view);
        vm.RequestTab(TabletDetailTab.PenBehavior);
        Dispatcher.UIThread.RunJobs();

        var slider = Named(view, "Position smoothing");
        Assert.True(Reset(slider).IsEnabled);

        PenButtonCardTests.Click(window, Reset(slider));

        Assert.Equal(0, vm.PositionSmoothing, 6);
        window.Close();
    }

    // --- Scribble's link to the pressure tab ------------------------------------------------------------

    private sealed class NoopDebugSession : IDaemonDebugSession
    {
#pragma warning disable CS0067
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    private static (Window Window, TestView View, List<(string, TabletDetailTab)> Opened) Scribble(bool detected)
    {
        var profile = new Profile { OutputMode = OpenTabletArtist.Tests.TestOtd.Output(), Tablet = "Wacom PTK-670" };
        var data = new FakeDeviceData { Profiles = [new ProfileItem(profile, IsDetected: detected, LastSeen: null)] };
        var opened = new List<(string, TabletDetailTab)>();
        var vm = new TestViewModel(new NoopDebugSession(), data, openPen: (n, t) => opened.Add((n, t)));
        data.RaiseDataLoaded();
        var view = new TestView { DataContext = vm };
        return (HarnessTests.Show(view), view, opened);
    }

    private static Button Link(TestView view) =>
        view.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == "Adjust pressure curve and smoothing");

    /// <summary>It is there with a plain curve and no smoothing, when no chip is drawn: that is when you want to open
    /// the pressure tab and shape one. And it opens the tablet's pressure tab.</summary>
    [AvaloniaFact]
    public void TheScribbleLink_IsThereWithNoChips_AndOpensThePressureTab()
    {
        var (window, view, opened) = Scribble(detected: true);

        Assert.True(Link(view).IsEffectivelyVisible);
        PenButtonCardTests.Click(window, Link(view));

        Assert.Equal([("Wacom PTK-670", TabletDetailTab.PenDynamics)], opened);
        window.Close();
    }

    [AvaloniaFact]
    public void WithNoTablet_TheScribbleLinkIsThereButDoesNothing()
    {
        var (window, view, opened) = Scribble(detected: false);

        Assert.True(Link(view).IsEffectivelyVisible);
        Assert.False(Link(view).IsEffectivelyEnabled);
        Assert.Empty(opened);
        window.Close();
    }
}
