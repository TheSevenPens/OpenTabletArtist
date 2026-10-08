using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The window a finished recording is reviewed and saved in. The Scribble page keeps nothing of a recording, so this
/// is the one place its results, its form and its file name appear, and the one place a recording can be thrown away
/// by accident: a click on the window's edge, Esc, or Discard. Each of those has to ask first.
/// </summary>
public class StrokeRecordingDialogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ota-dialog-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static readonly StrokeRecordingContext Context = new(
        "Wacom PTK-470", "OpenTabletDriver 0.6.7", 1023, new TabletSpace(15200, 9500, 152.0, 95.0));

    private static (PenSample Sample, JObject Json) Report(double x, double pressure)
    {
        var sample = new PenSample(0, 0, x, x * 2, pressure / 1023, 4, -3, 0, pressure > 0, pressure > 0 ? 0 : 9,
            Timestamp: 1_000 + (long)x, RawPressure: pressure, HasTilt: true);

        var json = new JObject
        {
            ["Tablet"] = new JObject
            {
                ["Properties"] = new JObject
                {
                    ["Name"] = Context.Tablet,
                    ["Specifications"] = new JObject
                    {
                        ["Digitizer"] = new JObject { ["MaxX"] = 15200, ["MaxY"] = 9500, ["Width"] = 152.0, ["Height"] = 95.0 },
                        ["Pen"] = new JObject { ["MaxPressure"] = 1023 },
                    },
                },
            },
            ["Data"] = new JObject
            {
                ["Position"] = new JObject { ["X"] = x, ["Y"] = x * 2 },
                ["Pressure"] = pressure,
                ["Tilt"] = new JObject { ["X"] = 4, ["Y"] = -3 },
                ["HoverDistance"] = pressure > 0 ? 0 : 9,
            },
        };

        return (sample, json);
    }

    /// <summary>
    /// Records a stroke and presses Stop, the way the Scribble page does: the view model's own hook opens the dialog,
    /// so closing the dialog dismisses the recording exactly as it does in the application.
    /// </summary>
    private Opened Record(bool agreeToDiscard, out StrokeRecordingViewModel recording)
    {
        var owner = new Window { Width = 800, Height = 600 };
        owner.Show();

        var opened = new Opened { Owner = owner };
        Action<JObject, PenSample>? tap = null;

        recording = new StrokeRecordingViewModel(
            () => Context,
            t => tap = t,
            showReview: vm => opened.Closed = StrokeRecordingDialog.ShowAsync(owner, vm, () =>
            {
                opened.Asked++;
                return Task.FromResult(agreeToDiscard);
            }),
            folder: () => _folder);

        recording.StartCommand.Execute(null);
        foreach (var (x, p) in new (double, double)[] { (1, 0), (2, 0), (3, 200), (4, 300), (5, 250), (6, 0), (7, 0) })
        {
            var (sample, json) = Report(x, p);
            tap!(json, sample);
        }

        recording.StopCommand.Execute(null);   // opens the dialog
        Dispatcher.UIThread.RunJobs();

        opened.Dialog = owner.OwnedWindows.OfType<StrokeRecordingDialog>().Single();
        return opened;
    }

    private sealed class Opened
    {
        public Window Owner = null!;
        public StrokeRecordingDialog Dialog = null!;
        public Task Closed = null!;

        /// <summary>How many times the dialog asked whether to throw the recording away.</summary>
        public int Asked;
    }

    private static Button ButtonNamed(Window dialog, string content) =>
        dialog.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == content);

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ShowsTheResultsAndTheFormAndNotTheSavedScreen()
    {
        using var errors = BindingErrors.Capture();
        var o = Record(agreeToDiscard: true, out var vm);
        vm.Firmware = "9.9.9";
        Dispatcher.UIThread.RunJobs();

        Assert.True(o.Dialog.FindControl<StackPanel>("ReviewPanel")!.IsEffectivelyVisible);
        Assert.False(o.Dialog.FindControl<StackPanel>("SavedPanel")!.IsEffectivelyVisible);

        var texts = o.Dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
        Assert.Contains(texts, t => t.StartsWith("1 stroke over"));
        Assert.Contains(texts, t => t.Contains("reports in total"));
        Assert.Contains(o.Dialog.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "9.9.9");
        errors.AssertNone("the recording dialog");
    }

    [AvaloniaFact]
    public void SavingSwitchesToTheSavedScreenWhichIsTheOnlyPlaceTheFileNameAppears()
    {
        using var errors = BindingErrors.Capture();
        var o = Record(agreeToDiscard: true, out var vm);

        vm.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(o.Dialog.FindControl<StackPanel>("ReviewPanel")!.IsEffectivelyVisible);
        Assert.True(o.Dialog.FindControl<StackPanel>("SavedPanel")!.IsEffectivelyVisible);
        Assert.Contains(o.Dialog.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text == vm.SavedPath);
        Assert.True(File.Exists(vm.SavedPath));
        errors.AssertNone("the saved screen");
    }

    [AvaloniaFact]
    public void DoneAfterSavingClosesWithoutAskingAndLeavesNothingBehind()
    {
        var o = Record(agreeToDiscard: false, out var vm);   // would refuse, if asked
        vm.SaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Click(ButtonNamed(o.Dialog, "Done"));

        Assert.True(o.Closed.IsCompleted);
        Assert.Equal(0, o.Asked);
        Assert.True(vm.IsIdle);
        Assert.False(vm.HasSaved);
    }

    [AvaloniaFact]
    public void ClosingBeforeSavingAsksAndAnswerNoKeepsTheRecording()
    {
        var o = Record(agreeToDiscard: false, out var vm);

        o.Dialog.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, o.Asked);
        Assert.False(o.Closed.IsCompleted);   // still open
        Assert.True(vm.IsReview);             // and the recording is still there to save
        Assert.True(o.Dialog.FindControl<StackPanel>("ReviewPanel")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void ClosingBeforeSavingAndAnswerYesDiscardsItAndClosesTheWindow()
    {
        var o = Record(agreeToDiscard: true, out var vm);

        o.Dialog.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, o.Asked);
        Assert.True(o.Closed.IsCompleted);
        Assert.True(vm.IsIdle);
        Assert.False(Directory.Exists(_folder));
    }

    [AvaloniaFact]
    public void DiscardAsksJustLikeClosingTheWindowDoes()
    {
        var o = Record(agreeToDiscard: false, out var vm);

        Click(ButtonNamed(o.Dialog, "Discard"));

        Assert.Equal(1, o.Asked);
        Assert.True(vm.IsReview);
        Assert.False(o.Closed.IsCompleted);
    }

    [AvaloniaFact]
    public void EscapeAsksToo()
    {
        var o = Record(agreeToDiscard: false, out var vm);

        o.Dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, o.Asked);
        Assert.True(vm.IsReview);
    }

    [AvaloniaFact]
    public void ASaveThatFailsStaysOnTheFormWithTheReasonShown()
    {
        File.WriteAllText(_folder, "a file where the folder should be");
        try
        {
            var o = Record(agreeToDiscard: true, out var vm);

            vm.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(o.Dialog.FindControl<StackPanel>("ReviewPanel")!.IsEffectivelyVisible);
            Assert.Contains(o.Dialog.GetVisualDescendants().OfType<TextBlock>(),
                t => (t.Text ?? "").StartsWith("Couldn't save the recording"));
            Assert.False(o.Closed.IsCompleted);
        }
        finally { File.Delete(_folder); }
    }
}

