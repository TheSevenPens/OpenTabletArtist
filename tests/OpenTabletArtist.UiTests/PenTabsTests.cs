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
        Assert.Contains("tip and eraser", headings);
        Assert.Contains("movement", headings);
        Assert.Contains("pen input", headings);
        if (OperatingSystem.IsWindows()) Assert.Contains("pressing", headings);
        window.Close();
    }

    [AvaloniaFact]
    public void Buttons_HoldsTheBarrelButtonsAndDiagram_NotTheTipOrEraser()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Select(view, "ButtonsTab");

        Assert.Equal(3, Visible<PenButtonCard>(view).Count());
        Assert.Empty(Visible<PenSwitchCard>(view));
        Assert.NotEmpty(Visible<LayoutTransformControl>(view));     // the pen diagram
        Assert.DoesNotContain(Visible<TextBlock>(view), t => t.Text == "movement");
        window.Close();
    }

    [AvaloniaFact]
    public void TheButtonCards_RunThreeTwoOne_TopToBottom()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));
        Select(view, "ButtonsTab");

        var labels = Visible<PenButtonCard>(view)
            .OrderBy(c => c.TranslatePoint(new Avalonia.Point(0, 0), view)!.Value.Y)
            .Select(c => ((PenButtonRowViewModel)c.DataContext!).SectionLabel)
            .ToArray();

        Assert.Equal(new[] { "BUTTON 3", "BUTTON 2", "BUTTON 1" }, labels);
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
    public void PressesLikeAMouse_GreysOutPressureAndTilt_AndSaysWhy()
    {
        if (!OperatingSystem.IsWindows()) return; // "presses like a mouse" is the Windows-only plain output
        var (window, view) = Page(Vm(NativeAbsolute));

        Assert.False(Toggle(view, "Disable pressure sensitivity").IsEffectivelyEnabled);
        Assert.False(Toggle(view, "Disable tilt").IsEffectivelyEnabled);
        Assert.True(Toggle(view, "Disable pen tip").IsEffectivelyEnabled);     // the tip still works as a button
        Assert.Contains(Visible<TextBlock>(view),
            t => t.Text == "Pressure and tilt are off while the pen presses like a mouse.");
        window.Close();
    }

    [AvaloniaFact]
    public void UnderWindowsInk_PressureAndTiltAreLive_WithNoNote()
    {
        var (window, view) = Page(Vm(WinInkAbsolute));

        Assert.True(Toggle(view, "Disable pressure sensitivity").IsEffectivelyEnabled);
        Assert.True(Toggle(view, "Disable tilt").IsEffectivelyEnabled);
        Assert.DoesNotContain(Visible<TextBlock>(view),
            t => t.Text == "Pressure and tilt are off while the pen presses like a mouse.");
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
