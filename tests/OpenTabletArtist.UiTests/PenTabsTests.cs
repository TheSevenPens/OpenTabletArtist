using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The pen page as three tabs, basics · pressure · buttons: what each one holds, that buttons is last and goes
/// away on a pen with no barrel buttons, that the pressure switch and the tilt choice are greyed out (with the
/// reason) while the pen presses like a mouse, and that the tip card says "Disabled". Bindings are checked
/// on every tab, since a tab's content only shows once it is selected.
/// </summary>
public class PenTabsTests
{
    private const string WinInkAbsolute = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string NativeAbsolute = "OpenTabletDriver.Desktop.Output.AbsoluteMode";

    private static TabletDetailViewModel Vm(string outputMode, int penButtons = 3, bool tipBound = true)
    {
        var profile = new Profile { Tablet = "Test Tablet", OutputMode = new PluginSettingStore(outputMode) { Path = outputMode } };
        profile.BindingSettings.TipButton = tipBound ? PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip) : null!;
        profile.BindingSettings.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection();
        for (int i = 1; i <= penButtons; i++)
            profile.BindingSettings.PenButtons.Add(PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, i));
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        return new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
    }

    private static (Window Window, PenDetailView View) Page(TabletDetailViewModel vm)
    {
        var view = new PenDetailView { DataContext = vm };
        return (HarnessTests.Show(view), view);
    }

    private static void Select(PenDetailView view, string tab)
    {
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<T> Visible<T>(Control c) where T : Control =>
        c.GetVisualDescendants().OfType<T>().Where(x => x.IsEffectivelyVisible);

    private static string?[] TabNames(PenDetailView view) =>
        view.VisibleTabButtons().Select(r => r.Content as string).ToArray();

    // ── The tabs ───────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheTabs_AreBasicsPressureTiltButtons_InThatOrder_OpeningOnBasics()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.Equal(new[] { "basics", "pressure", "tilt", "buttons" }, TabNames(view));
        Assert.True(view.FindControl<RadioButton>("MovementTab")!.IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void APenWithNoBarrelButtons_HasNoButtonsTab()
    {
        var (window, view) = Page(Vm(WinInkAbsolute, penButtons: 0));

        Assert.Equal(new[] { "basics", "pressure", "tilt" }, TabNames(view));
        window.Close();
    }

    [AvaloniaFact]
    public void Basics_HoldsTheTipAndEraser_NotTheButtons()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.Equal(2, Visible<PenSwitchCard>(view).Count());   // the tip's and the eraser's
        Assert.Empty(Visible<PenButtonCard>(view));
        var headings = Visible<TextBlock>(view).Where(t => t.Classes.Contains("section")).Select(t => t.Text).ToList();
        Assert.Contains("tip", headings);
        Assert.Contains("eraser", headings);
        Assert.DoesNotContain("tip and eraser", headings);      // two sections now, one for each
        Assert.Contains("movement", headings);
        Assert.DoesNotContain("tilt", headings);                // tilt has its own tab
        Assert.DoesNotContain("pen input", headings);
        if (OperatingSystem.IsWindows()) Assert.Contains("pressing", headings);
        window.Close();
    }

    // ── The tip and the eraser, a section each ─────────────────────

    private static double Top(PenDetailView view, Visual control) => control.TranslatePoint(new Avalonia.Point(0, 0), view)!.Value.Y;

    private static TextBlock Heading(PenDetailView view, string text) =>
        Visible<TextBlock>(view).Single(t => t.Classes.Contains("section") && t.Text == text);

    private static PenSwitchCard Card(PenDetailView view, string label) =>
        Visible<PenSwitchCard>(view).Single(c => ((PenSwitchRowViewModel)c.DataContext!).SectionLabel == label);

    [AvaloniaFact]
    public void TheTipSection_IsOneCard_OfDefaultActionAndDisabled_WithNoSeparateSwitch()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        var tip = Card(view, "PEN TIP");

        Assert.True(Top(view, Heading(view, "tip")) < Top(view, tip), "the tip card is under the tip heading");
        Assert.Equal(new[] { "Default action", "Disabled" }, PenButtonCardTests.Segments(tip).Keys.ToArray());
        Assert.Equal(new[] { "Default action" }, PenButtonCardTests.Lit(tip));
        Assert.DoesNotContain(Visible<ToggleSetting>(view), t => t.Label == "Disable pen tip");
        window.Close();
    }

    [AvaloniaFact]
    public void Basics_LaysOutTipThenMovementThenEraser_AsThreeColumns()
    {
        var view = new PenDetailView { DataContext = Vm(WinInkAbsolute) };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        window.Measure(new Size(1000, 700));
        window.Arrange(new Rect(0, 0, 1000, 700));
        Dispatcher.UIThread.RunJobs();

        double Left(Visual c) => c.TranslatePoint(new Avalonia.Point(0, 0), view)!.Value.X;

        double tip = Left(Heading(view, "tip"));
        double movement = Left(Heading(view, "movement"));
        double eraser = Left(Heading(view, "eraser"));

        Assert.True(tip < movement, "movement is the second column, right of the tip");
        Assert.True(movement < eraser, "the eraser is the third column, right of movement");
        Assert.Equal(Top(view, Heading(view, "tip")), Top(view, Heading(view, "eraser")), 1);
        window.Close();
    }

    [AvaloniaFact]
    public void TheEraserSection_IsOneCard_OfDefaultActionAndDisabled()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        var eraser = Card(view, "ERASER");

        Assert.Equal(new[] { "Default action", "Disabled" }, PenButtonCardTests.Segments(eraser).Keys.ToArray());
        Assert.Equal(new[] { "Default action" }, PenButtonCardTests.Lit(eraser));
        Assert.Contains(Visible<TextBlock>(eraser), t => t.Text == "Erases");
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingDisabledOnTheEraser_DisablesIt_AndLeavesTheTipAlone()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);

        PenButtonCardTests.Click(window, PenButtonCardTests.Segments(Card(view, "ERASER"))["Disabled"]);

        Assert.True(vm.PenEraserRow!.IsDisabled);
        Assert.True(vm.PenTipRow!.IsRecommended);
        Assert.Equal(new[] { "Disabled" }, PenButtonCardTests.Lit(Card(view, "ERASER")));
        Assert.Contains(Visible<TextBlock>(Card(view, "ERASER")), t => t.Text == "Does nothing");

        PenButtonCardTests.Click(window, PenButtonCardTests.Segments(Card(view, "ERASER"))["Default action"]);

        Assert.True(vm.PenEraserRow!.IsRecommended);
        window.Close();
    }

    [AvaloniaFact]
    public void TheEraserSection_HoldsTheEraserCard_UnderItsHeading()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.True(Top(view, Heading(view, "eraser")) < Top(view, Card(view, "ERASER")));
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingDisabled_DisablesTheTip_AndDefaultActionPutsItBack()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);
        var tip = Card(view, "PEN TIP");

        PenButtonCardTests.Click(window, PenButtonCardTests.Segments(tip)["Disabled"]);

        Assert.True(vm.PenTipRow!.IsDisabled);
        Assert.Equal(new[] { "Disabled" }, PenButtonCardTests.Lit(Card(view, "PEN TIP")));

        PenButtonCardTests.Click(window, PenButtonCardTests.Segments(Card(view, "PEN TIP"))["Default action"]);

        Assert.True(vm.PenTipRow!.IsRecommended);
        Assert.Equal(new[] { "Default action" }, PenButtonCardTests.Lit(Card(view, "PEN TIP")));
        window.Close();
    }

    [AvaloniaFact]
    public void Buttons_HoldsTheBarrelButtonsAndDiagram_NotTheTipOrEraser()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Select(view, "ButtonsTab");

        Assert.Equal(3, Visible<PenButtonCard>(view).Count());
        Assert.Empty(Visible<PenSwitchCard>(view));
        Assert.NotEmpty(Visible<PenButtonsDiagram>(view));          // the pen picture
        Assert.DoesNotContain(Visible<TextBlock>(view), t => t.Text == "movement");
        window.Close();
    }

    [AvaloniaFact]
    public void TheButtonCards_RunOneTwoThree_FromTheTip()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));
        Select(view, "ButtonsTab");

        // Reading order, so it holds whether the cards sit side by side or stack at this width.
        var labels = Visible<PenButtonCard>(view)
            .Select(c => (Card: c, At: c.TranslatePoint(new Avalonia.Point(0, 0), view)!.Value))
            .OrderBy(x => x.At.Y).ThenBy(x => x.At.X)
            .Select(x => ((PenButtonRowViewModel)x.Card.DataContext!).SectionLabel)
            .ToArray();

        Assert.Equal(new[] { "BUTTON 1", "BUTTON 2", "BUTTON 3" }, labels);
        window.Close();
    }

    [AvaloniaFact]
    public void LosingTheButtons_WhileOnTheButtonsTab_FallsBackToBasics()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);
        Select(view, "ButtonsTab");
        Assert.True(view.FindControl<RadioButton>("ButtonsTab")!.IsChecked);

        // As if the page switched to a pen with no barrel buttons while this tab was open.
        vm.PenButton1Row = null;
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<RadioButton>("ButtonsTab")!.IsVisible);
        Assert.True(view.FindControl<RadioButton>("MovementTab")!.IsChecked);
        Assert.NotEmpty(Visible<PenSwitchCard>(view));                 // basics' content is showing
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("MovementTab")]
    [InlineData("DynamicsTab")]
    [InlineData("TiltTab")]
    [InlineData("ButtonsTab")]
    public void EveryTab_BindsAgainstItsViewModel(string tab)
    {
        var vm = Vm(WinInkAbsolute);

        using var errors = BindingErrors.Capture();
        var (window, view) = Page(vm);
        Select(view, tab);

        errors.AssertNone($"{nameof(PenDetailView)}.{tab}");
        window.Close();
    }

    // ── A pen that presses like a mouse ────────────────────────────

    private static ToggleSetting Toggle(PenDetailView view, string label) =>
        view.GetVisualDescendants().OfType<ToggleSetting>().Single(t => t.Label == label);

    // The tilt tab's Enabled / Disabled bar. Only that tab is showing when these are read, so the visible
    // segments are its two.
    private static Dictionary<string, RadioButton> TiltSegments(PenDetailView view)
    {
        Select(view, "TiltTab");
        return Visible<RadioButton>(view).Where(r => r.Classes.Contains("segment"))
            .ToDictionary(r => (string)r.Content!);
    }

    [AvaloniaFact]
    public void PressesLikeAMouse_GreysOutTilt_AndSaysWhy()
    {
        if (!OperatingSystem.IsWindows()) return; // "presses like a mouse" is the Windows-only plain output
        var (window, view) = Page(Vm(NativeAbsolute));

        Assert.All(PenButtonCardTests.Segments(Card(view, "PEN TIP")).Values,
            r => Assert.True(r.IsEffectivelyEnabled));                          // the tip still works as a button
        Assert.All(TiltSegments(view).Values, r => Assert.False(r.IsEffectivelyEnabled));
        Assert.Contains(Visible<TextBlock>(view), t => t.Text == "Tilt is off while the pen presses like a mouse.");
        window.Close();
    }

    [AvaloniaFact]
    public void TheTiltTab_IsOneBar_OfEnabledAndDisabled_OpeningOnEnabled()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        var segments = TiltSegments(view);

        Assert.Equal(new[] { "Enabled", "Disabled" }, segments.Keys.ToArray());
        Assert.Equal(new[] { "Enabled" }, segments.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray());
        Assert.Contains(Visible<TextBlock>(view), t => t.Text == "tilt");
        Assert.DoesNotContain(Visible<ToggleSetting>(view), t => t.Label == "Disable tilt");
        window.Close();
    }

    [AvaloniaFact]
    public void TheTiltTab_ShowsTheTiltFromAboveAndFromTheSide_AndTheyFollowTheLiveTilt()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);
        Select(view, "TiltTab");

        var headings = Visible<TextBlock>(view).Where(t => t.Classes.Contains("section")).Select(t => t.Text).ToList();
        Assert.Contains("azimuth", headings);
        Assert.Contains("altitude", headings);
        var above = Visible<TiltAzimuthDiagram>(view).Single();
        var side = Visible<TiltAltitudeDiagram>(view).Single();
        Assert.Null(above.Azimuth);                             // no pen yet: nothing drawn but the dials
        Assert.Null(side.Altitude);

        vm.SetLiveTilt(new PenSample(0.5, 0.5, 0, 0, 0, 25, -30, 0, false, HasTilt: true));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(vm.LiveAzimuth, above.Azimuth);
        Assert.Equal(vm.LiveAltitude, above.Altitude);
        Assert.Equal(vm.LiveAltitude, side.Altitude);
        Assert.Contains(Visible<TextBlock>(view), t => t.Text == vm.LiveAzimuthText && t.Text != "—");
        Assert.Contains(Visible<TextBlock>(view), t => t.Text == vm.LiveAltitudeText && t.Text != "—");
        window.Close();
    }

    [AvaloniaFact]
    public void TheDiagrams_AreNotOnBasics()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.Empty(Visible<TiltAzimuthDiagram>(view));
        Assert.Empty(Visible<TiltAltitudeDiagram>(view));
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingDisabledOnTheTiltTab_DisablesTilt_AndEnabledPutsItBack()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);

        PenButtonCardTests.Click(window, TiltSegments(view)["Disabled"]);

        Assert.True(vm.DisableTilt);
        Assert.Equal(new[] { "Disabled" }, TiltSegments(view).Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray());
        Assert.Contains(Visible<TextBlock>(view), t => t.Text == "Apps receive no tilt from this tablet.");

        PenButtonCardTests.Click(window, TiltSegments(view)["Enabled"]);

        Assert.False(vm.DisableTilt);
        Assert.Equal(new[] { "Enabled" }, TiltSegments(view).Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray());
        window.Close();
    }

    // The pressure tab's Enabled / Disabled bar. Only that tab is showing when these are read.
    private static Dictionary<string, RadioButton> PressureSegments(PenDetailView view)
    {
        Select(view, "DynamicsTab");
        view.UpdateLayout();
        return Visible<RadioButton>(view).Where(r => r.Classes.Contains("segment"))
            .ToDictionary(r => (string)r.Content!);
    }

    [AvaloniaFact]
    public void OnThePressureTab_ThePressureBarIsGreyedOut_WithTheReasonRightUnderIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (window, view) = Page(Vm(NativeAbsolute));

        var bar = PressureSegments(view)["Enabled"];
        var reason = Visible<TextBlock>(view).Single(t => t.Text == "The pen is set to press like a mouse, so this curve has no effect right now.");

        Assert.All(PressureSegments(view).Values, r => Assert.False(r.IsEffectivelyEnabled));
        Assert.True(Top(view, bar) < Top(view, reason));
        window.Close();
    }

    [AvaloniaFact]
    public void ThePressureTab_IsOneBar_OfEnabledAndDisabled_OpeningOnEnabled()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        var segments = PressureSegments(view);

        Assert.Equal(new[] { "Enabled", "Disabled" }, segments.Keys.ToArray());
        Assert.Equal(new[] { "Enabled" }, segments.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray());
        Assert.DoesNotContain(Visible<ToggleSetting>(view), t => t.Label == "Disable pressure sensitivity");
        window.Close();
    }

    [AvaloniaFact]
    public void UnderWindowsInk_PressureAndTiltAreLive_WithNoNote()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.All(TiltSegments(view).Values, r => Assert.True(r.IsEffectivelyEnabled));
        Assert.DoesNotContain(Visible<TextBlock>(view),
            t => t.Text == "Tilt is off while the pen presses like a mouse.");

        Assert.All(PressureSegments(view).Values, r => Assert.True(r.IsEffectivelyEnabled));
        window.Close();
    }

    // ── Health links open the tab that holds the switch ────────────

    [AvaloniaFact]
    public void ThePressureHealthLink_LandsOnThePressureTab()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);
        Select(view, "ButtonsTab");                             // start somewhere else

        vm.RequestTab(DashboardViewModel.PenTabFor(RemediationArea.TabletPenPressure));
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<RadioButton>("DynamicsTab")!.IsChecked);
        Assert.Contains(Visible<RadioButton>(view), r => r.Classes.Contains("segment") && (string?)r.Content == "Enabled");
        window.Close();
    }

    [AvaloniaFact]
    public void TheTiltHealthLink_LandsOnTheTiltTab()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);

        vm.RequestTab(DashboardViewModel.PenTabFor(RemediationArea.TabletPenTilt));
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<RadioButton>("TiltTab")!.IsChecked);
        Assert.Contains(Visible<RadioButton>(view), r => r.Classes.Contains("segment") && (string?)r.Content == "Enabled");
        window.Close();
    }

    [AvaloniaFact]
    public void ThePenTipHealthLink_LandsOnBasics_WhereTheTipCardIs()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);
        Select(view, "ButtonsTab");                             // start somewhere else

        vm.RequestTab(DashboardViewModel.PenTabFor(RemediationArea.TabletPenInputs));
        Dispatcher.UIThread.RunJobs();

        Assert.NotEmpty(Visible<PenSwitchCard>(view));
        window.Close();
    }

    // ── Pressure sensitivity, on the pressure tab ──────────────────

    [AvaloniaFact]
    public void ThePressureBar_IsOnThePressureTab_NotOnBasics()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.DoesNotContain(Visible<RadioButton>(view), r => (string?)r.Content == "Enabled");

        Select(view, "DynamicsTab");

        Assert.Contains(Visible<RadioButton>(view), r => r.Classes.Contains("segment") && (string?)r.Content == "Enabled");
        window.Close();
    }

    [AvaloniaFact]
    public void ThePressureTab_OpensAtTheTop_NotScrolledDownToTheDrawingCanvas()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Select(view, "DynamicsTab");
        view.UpdateLayout();

        var showing = view.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => sv.IsEffectivelyVisible);
        Assert.True(showing.Extent.Height > showing.Viewport.Height, "the tab is taller than the window, so it can scroll");
        Assert.Equal(0, showing.Offset.Y);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePressureBar_SitsAboveTheCurve_ItOverrides()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        var bar = PressureSegments(view)["Enabled"];
        var chart = Visible<PressureCurveChart>(view).Single();

        Assert.True(Top(view, bar) < Top(view, chart));
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingDisabledOnThePressureTab_DisablesPressure_FlagsTheCurve_AndEnabledPutsItBack()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);
        const string note = "Pressure sensitivity is disabled, so this curve has no effect right now.";
        Assert.DoesNotContain(Visible<TextBlock>(view), t => t.Text == note);

        PenButtonCardTests.Click(window, PressureSegments(view)["Disabled"]);

        Assert.True(vm.DisablePressure);
        Assert.Equal(new[] { "Disabled" }, PressureSegments(view).Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray());
        Assert.Contains(Visible<TextBlock>(view), t => t.Text == note);

        PenButtonCardTests.Click(window, PressureSegments(view)["Enabled"]);

        Assert.False(vm.DisablePressure);
        Assert.DoesNotContain(Visible<TextBlock>(view), t => t.Text == note);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePressureTab_SaysTheCurveDoesNothing_WhenThePenPressesLikeAMouse()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (window, view) = Page(Vm(NativeAbsolute));
        Select(view, "DynamicsTab");

        Assert.Contains(Visible<TextBlock>(view),
            t => t.Text == "The pen is set to press like a mouse, so this curve has no effect right now.");
        window.Close();
    }

    [AvaloniaFact]
    public void ThePressureTab_HasNoMouseNote_UnderWindowsInk()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));
        Select(view, "DynamicsTab");

        Assert.DoesNotContain(Visible<TextBlock>(view),
            t => t.Text == "The pen is set to press like a mouse, so this curve has no effect right now.");
        window.Close();
    }

    // ── The tip card ───────────────────────────────────────────────

    [AvaloniaFact]
    public void ADisabledTip_LightsDisabled_AndSaysItDoesNothing()
    {
        var (window, view) = Page(Vm(WinInkAbsolute, tipBound: false));

        var tip = Card(view, "PEN TIP");

        Assert.Equal(new[] { "Disabled" }, PenButtonCardTests.Lit(tip));
        Assert.Contains(Visible<TextBlock>(tip), t => t.Text == "Does nothing");
        Assert.DoesNotContain(Visible<TextBlock>(tip), t => t.Text?.Contains("custom action") == true);
        window.Close();
    }

    [AvaloniaFact]
    public void ATipSetElsewhere_LightsNeitherChoice_AndSaysWhatItDoesNow()
    {
        var vm = Vm(WinInkAbsolute);
        vm.PenTipRow = new PenSwitchRowViewModel(PenSwitchKind.Tip, 1, AuxKeyBinding.MakeMouseBinding("Left"), true,
            (_, _, _) => Task.CompletedTask, () => Task.CompletedTask);
        var (window, view) = Page(vm);

        var tip = Card(view, "PEN TIP");

        Assert.Empty(PenButtonCardTests.Lit(tip));
        Assert.Contains(Visible<TextBlock>(tip), t => t.Text == "Currently: Mouse button");
        Assert.Contains(Visible<TextBlock>(tip), t => t.Text?.Contains("custom action") == true);
        window.Close();
    }

    [AvaloniaFact]
    public void TheWindowsInkNotice_SitsWithPressing_NotWithMovement()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        var notice = view.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "Output mode needs to use Windows Ink for pressure and tilt to work");
        var pressing = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "pressing");

        // Same panel as the Pressing heading, which is inside the column that holds the Pressing dropdown.
        Assert.Same(pressing.GetVisualParent(), notice.GetVisualParent());
        window.Close();
    }
}
