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
/// away on a pen with no barrel buttons, that the toggles for pressure and tilt are greyed out (with the
/// reason) while the pen presses like a mouse, and that the tip card says "Disabled". Bindings are checked
/// on every tab, since a tab's content only shows once it is selected.
/// </summary>
public class PenTabsTests
{
    private const string WinInkAbsolute = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string NativeAbsolute = "OpenTabletDriver.Desktop.Output.AbsoluteMode";

    private static TabletDetailViewModel Vm(string outputMode, int penButtons = 3, bool tipBound = true)
    {
        var profile = new Profile { Tablet = "Test Tablet" };
        profile.OutputMode = new PluginSettingStore(outputMode) { Path = outputMode };
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
    public void TheTabs_AreBasicsPressureButtons_InThatOrder_OpeningOnBasics()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.Equal(new[] { "basics", "pressure", "buttons" }, TabNames(view));
        Assert.True(view.FindControl<RadioButton>("MovementTab")!.IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void APenWithNoBarrelButtons_HasNoButtonsTab()
    {
        var (window, view) = Page(Vm(WinInkAbsolute, penButtons: 0));

        Assert.Equal(new[] { "basics", "pressure" }, TabNames(view));
        window.Close();
    }

    [AvaloniaFact]
    public void Basics_HoldsTheTipAndEraser_NotTheButtons()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.Equal(2, Visible<PenSwitchCard>(view).Count());
        Assert.Empty(Visible<PenButtonCard>(view));
        var headings = Visible<TextBlock>(view).Where(t => t.Classes.Contains("section")).Select(t => t.Text).ToList();
        Assert.Contains("tip", headings);
        Assert.Contains("eraser", headings);
        Assert.DoesNotContain("tip and eraser", headings);      // two sections now, one for each
        Assert.Contains("movement", headings);
        Assert.Contains("pen input", headings);
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
    public void TheTipSection_HoldsTheTipCard_AndTheDisablePenTipSwitch_AboveTheEraserSection()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        double tipHeading = Top(view, Heading(view, "tip"));
        double tipCard = Top(view, Card(view, "PEN TIP"));
        double disableTip = Top(view, Visible<ToggleSetting>(view).Single(t => t.Label == "Disable pen tip"));
        double eraserHeading = Top(view, Heading(view, "eraser"));

        Assert.True(tipHeading < tipCard, "the tip card is under the tip heading");
        Assert.True(tipCard < disableTip, "Disable pen tip is under the tip card");
        Assert.True(disableTip < eraserHeading, "Disable pen tip is in the tip section, not the eraser's");
        window.Close();
    }

    [AvaloniaFact]
    public void TheEraserSection_HoldsOnlyTheEraserCard_BeforeThePenInputSwitches()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        double eraserHeading = Top(view, Heading(view, "eraser"));
        double eraserCard = Top(view, Card(view, "ERASER"));
        double penInput = Top(view, Heading(view, "pen input"));

        Assert.True(eraserHeading < eraserCard);
        Assert.True(eraserCard < penInput, "the pen-input switches come after the eraser");
        window.Close();
    }

    [AvaloniaFact]
    public void DisablePenTip_StillDisablesTheTip()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);

        Visible<ToggleSetting>(view).Single(t => t.Label == "Disable pen tip").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.DisablePenTip);
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
        Assert.NotEmpty(Visible<PenSwitchCard>(view));              // basics' content is showing
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("MovementTab")]
    [InlineData("DynamicsTab")]
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

    [AvaloniaFact]
    public void PressesLikeAMouse_GreysOutTilt_AndSaysWhy()
    {
        if (!OperatingSystem.IsWindows()) return; // "presses like a mouse" is the Windows-only plain output
        var (window, view) = Page(Vm(NativeAbsolute));

        Assert.False(Toggle(view, "Disable tilt").IsEffectivelyEnabled);
        Assert.True(Toggle(view, "Disable pen tip").IsEffectivelyEnabled);     // the tip still works as a button
        Assert.Contains(Visible<TextBlock>(view), t => t.Text == "Tilt is off while the pen presses like a mouse.");
        window.Close();
    }

    [AvaloniaFact]
    public void OnThePressureTab_ThePressureSwitchIsGreyedOut_WithTheReasonRightUnderIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (window, view) = Page(Vm(NativeAbsolute));
        Select(view, "DynamicsTab");

        var toggle = Visible<ToggleSetting>(view).Single(t => t.Label == "Disable pressure sensitivity");
        var reason = Visible<TextBlock>(view).Single(t => t.Text == "The pen is set to press like a mouse, so this curve has no effect right now.");

        Assert.False(toggle.IsEffectivelyEnabled);
        Assert.True(Top(view, toggle) < Top(view, reason));
        window.Close();
    }

    [AvaloniaFact]
    public void UnderWindowsInk_PressureAndTiltAreLive_WithNoNote()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.True(Toggle(view, "Disable tilt").IsEffectivelyEnabled);
        Assert.DoesNotContain(Visible<TextBlock>(view),
            t => t.Text == "Tilt is off while the pen presses like a mouse.");

        Select(view, "DynamicsTab");
        Assert.True(Toggle(view, "Disable pressure sensitivity").IsEffectivelyEnabled);
        window.Close();
    }

    // ── Health links open the tab that holds the switch ────────────

    [AvaloniaTheory]
    [InlineData(RemediationArea.TabletPenInputs, "Disable pen tip")]
    [InlineData(RemediationArea.TabletPenTilt, "Disable tilt")]
    [InlineData(RemediationArea.TabletPenPressure, "Disable pressure sensitivity")]
    public void AHealthLink_LandsOnTheTabWhereItsSwitchIs(RemediationArea area, string switchLabel)
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);
        Select(view, "ButtonsTab");                             // start somewhere else

        vm.RequestTab(DashboardViewModel.PenTabFor(area));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(Visible<ToggleSetting>(view), t => t.Label == switchLabel);
        window.Close();
    }

    // ── Disable pressure sensitivity, on the pressure tab ──────────

    [AvaloniaFact]
    public void DisablePressureSensitivity_IsOnThePressureTab_NotOnBasics()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.DoesNotContain(Visible<ToggleSetting>(view), t => t.Label == "Disable pressure sensitivity");
        Assert.Contains(Visible<ToggleSetting>(view), t => t.Label == "Disable tilt");     // tilt stays where it was

        Select(view, "DynamicsTab");

        Assert.Contains(Visible<ToggleSetting>(view), t => t.Label == "Disable pressure sensitivity");
        Assert.DoesNotContain(Visible<ToggleSetting>(view), t => t.Label == "Disable tilt");
        window.Close();
    }

    [AvaloniaFact]
    public void DisablePressureSensitivity_SitsAboveTheCurve_ItOverrides()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));
        Select(view, "DynamicsTab");

        var toggle = Visible<ToggleSetting>(view).Single(t => t.Label == "Disable pressure sensitivity");
        var chart = Visible<PressureCurveChart>(view).Single();

        Assert.True(Top(view, toggle) < Top(view, chart));
        window.Close();
    }

    [AvaloniaFact]
    public void TickingDisablePressureSensitivity_OnThePressureTab_DisablesPressure_AndFlagsTheCurve()
    {
        var vm = Vm(WinInkAbsolute);
        var (window, view) = Page(vm);
        Select(view, "DynamicsTab");
        const string note = "Pressure sensitivity is disabled, so this curve has no effect right now.";
        Assert.DoesNotContain(Visible<TextBlock>(view), t => t.Text == note);

        Visible<ToggleSetting>(view).Single(t => t.Label == "Disable pressure sensitivity").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.DisablePressure);
        Assert.Contains(Visible<TextBlock>(view), t => t.Text == note);
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
    public void ADisabledTip_SaysDisabled_AndOffersNoFix()
    {
        var (window, view) = Page(Vm(WinInkAbsolute, tipBound: false));

        var tip = Visible<PenSwitchCard>(view).Single(c => ((PenSwitchRowViewModel)c.DataContext!).SectionLabel == "PEN TIP");

        Assert.Contains(Visible<TextBlock>(tip), t => t.Text == "Disabled");
        Assert.Empty(Visible<Button>(tip));
        Assert.DoesNotContain(Visible<TextBlock>(tip), t => t.Text?.Contains("custom action") == true);
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
