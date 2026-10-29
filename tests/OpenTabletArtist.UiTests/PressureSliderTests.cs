using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
}
