using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

// The Softness slider's range is negative (firmer) to positive (lighter). Its minimum was a binding, and
// when the page was swapped out the binding went first: the slider's minimum fell back to its default of 0,
// the slider clamped a negative softness up to 0, and the two-way Value binding wrote that 0 into the
// view-model before it let go. The curve was then applied to the daemon as linear, a moment after the
// artist had saved something else, so the Scribble page lost its "Pressure curve" chip and the window
// said "Unsaved changes". The range is now fixed in the view, so nothing can move it.
public class PressureSliderTests
{
    private static TabletDetailViewModel Vm(double softness)
    {
        var profile = new Profile
        {
            Tablet = "Test Tablet",
            OutputMode = new PluginSettingStore("OpenTabletDriver.Desktop.Output.Windows.WindowsInk")
            { Path = "VoiDPlugins.OutputMode.WindowsInkAbsoluteMode" },
        };
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        PressureCurveProfile.Write(settings, "Test Tablet",
            new PenDynamicsSettings(new PressureCurveSettings(softness, 0, 1, 0, 1, PressureMinApproach.Clamp), 0, 0, true),
            enable: true);
        return new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
    }

    [AvaloniaTheory]
    [InlineData(-0.9)]
    [InlineData(-0.5)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void SwappingThePenPageOut_LeavesTheSavedSoftnessAlone(double softness)
    {
        var vm = Vm(softness);
        var host = new ContentControl
        {
            Content = vm,
            ContentTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TabletDetailViewModel>((_, _) => new PenDetailView(), true),
        };
        var window = HarnessTests.Show(host);
        Dispatcher.UIThread.RunJobs();
        var view = host.GetVisualDescendants().OfType<PenDetailView>().Single();
        view.FindControl<RadioButton>("DynamicsTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(softness, vm.Softness, 5);                       // the slider loaded it as it was (stored as a float)

        host.Content = new TextBlock { Text = "another page" };       // navigate away, as the shell does
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(softness, vm.Softness, 5);
        window.Close();
    }

    private static (Window Window, PenDetailView View, LabeledSlider Slider) DeadzonePage(TabletDetailViewModel vm)
    {
        var view = new PenDetailView { DataContext = vm };
        var window = HarnessTests.Show(view);
        view.FindControl<RadioButton>("DynamicsTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var slider = view.GetVisualDescendants().OfType<LabeledSlider>().Single(l => l.Label == "Deadzone" && l.IsEffectivelyVisible);
        return (window, view, slider);
    }

    [AvaloniaFact]
    public void TheDeadzoneSlider_MovesTheCurvesInputMinimum_AndNothingElse()
    {
        var vm = Vm(0.4);
        var (window, _, slider) = DeadzonePage(vm);
        var before = vm.Curve;

        slider.Value = 0.3;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before with { InputMinimum = 0.3 }, vm.Curve);
        window.Close();
    }

    [AvaloniaFact]
    public void MovingTheLowerLeftNode_MovesTheDeadzoneSlider()
    {
        var vm = Vm(0);
        var (window, _, slider) = DeadzonePage(vm);
        Assert.Equal(0, slider.Value);

        vm.Curve = vm.Curve with { InputMinimum = 0.4, Minimum = 0.1 };   // a drag of the node, sideways and up
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.4, slider.Value, 5);
        window.Close();
    }

    [AvaloniaFact]
    public void TheDeadzoneSlider_SnapsToTwoDecimals_LikeTheNode()
    {
        var vm = Vm(0);
        var (window, _, slider) = DeadzonePage(vm);

        slider.Value = 0.1234;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.12, vm.Deadzone, 6);
        Assert.Equal(0.12, slider.Value, 6);
        window.Close();
    }

    [AvaloniaFact]
    public void AtTheTopOfItsRange_TheDeadzoneSlider_ShowsWhereTheValueActuallyIs()
    {
        var vm = Vm(0);
        var (window, _, slider) = DeadzonePage(vm);

        slider.Value = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.99, vm.Deadzone, 6);
        Assert.Equal(0.99, slider.Value, 6);       // the slider is not left saying 1
        window.Close();
    }

    [AvaloniaFact]
    public void SwappingThePenPageOut_LeavesTheDeadzoneAlone()
    {
        var vm = Vm(0);
        vm.Curve = vm.Curve with { InputMinimum = 0.3 };
        var host = new ContentControl
        {
            Content = vm,
            ContentTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TabletDetailViewModel>((_, _) => new PenDetailView(), true),
        };
        var window = HarnessTests.Show(host);
        Dispatcher.UIThread.RunJobs();
        host.GetVisualDescendants().OfType<PenDetailView>().Single().FindControl<RadioButton>("DynamicsTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        host.Content = new TextBlock { Text = "another page" };
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.3, vm.Deadzone, 5);
        window.Close();
    }

    [AvaloniaFact]
    public void TheDeadzone_IsShownByItsSlider_NotAlsoAsAReadout()
    {
        var (window, view, _) = DeadzonePage(Vm(0));

        Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("deadzone") == true);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(-1)]   // dragged to the top: as light as the curve goes
    [InlineData(1)]    // dragged to the bottom: as firm as it goes
    public void DraggingTheBendNodePastTheEnd_StopsWhereTheSliderStops(int direction)
    {
        var vm = Vm(0);
        // Subscribed before the view binds, so this sees each value before the slider can answer it.
        var seen = new System.Collections.Generic.List<double>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Softness)) seen.Add(vm.Softness); };
        var view = new PenDetailView { DataContext = vm };
        var window = HarnessTests.Show(view);
        view.FindControl<RadioButton>("DynamicsTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var chart = view.GetVisualDescendants().OfType<PressureCurveChart>().Single(c => c.IsEffectivelyVisible);

        // A linear curve's bend node sits in the middle of the plot; the drag goes on past its edge.
        var bend = chart.TranslatePoint(new Point(chart.Bounds.Width / 2, chart.Bounds.Height / 2), window)!.Value;
        var end = chart.TranslatePoint(new Point(chart.Bounds.Width / 2, direction < 0 ? -20 : chart.Bounds.Height + 20), window)!.Value;
        window.MouseDown(bend, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // The node used to reach 0.95, which the slider then clamped to 0.9 and wrote back: the curve went out
        // to 0.95 and back on every move, and was applied to the daemon both times.
        Assert.Equal(-direction * PressureCurveSettings.MaxSoftness, vm.Softness, 6);
        Assert.All(seen, v => Assert.InRange(Math.Abs(v), 0, PressureCurveSettings.MaxSoftness + 1e-9));
        window.Close();
    }
}
