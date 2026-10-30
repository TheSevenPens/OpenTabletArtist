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
/// The pen page's buttons tab as a whole (#1015): the pen picture on top and a card per button under it. Wide
/// enough, the cards sit side by side with a line from each button to its card; narrower than that they stack and
/// the lines go; a pen with one button gets a half-width card; and a click on one card's choices never lights
/// another card's.
/// </summary>
public class PenButtonsViewTests
{
    private const string WinInk = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private static readonly double SideBySideAt = PenDiagramGeometry.MinCardWidth * 3 + PenDiagramGeometry.CardGap * 2;

    private static TabletDetailViewModel Vm(params PluginSettingStore?[] buttons)
    {
        var profile = new Profile { Tablet = "Test Tablet", OutputMode = new PluginSettingStore(WinInk) { Path = WinInk } };
        profile.BindingSettings.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        profile.BindingSettings.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection();
        foreach (var b in buttons) profile.BindingSettings.PenButtons.Add(b!);
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        return new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
    }

    private static TabletDetailViewModel ThreeDefaults() =>
        Vm(PenButtonBinding.MakeDefault(1), PenButtonBinding.MakeDefault(2), PenButtonBinding.MakeDefault(3));

    private static (Window Window, PenButtonsView View) Show(TabletDetailViewModel vm, double width, double height = 900)
    {
        var view = new PenButtonsView { DataContext = vm, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        Relayout(window, width, height);
        return (window, view);
    }

    private static void Relayout(Window window, double width, double height)
    {
        // Twice: the view decides about its lines on SizeChanged, which fires after the first pass.
        for (int i = 0; i < 2; i++)
        {
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static List<PenButtonCard> Cards(PenButtonsView view) =>
        view.GetVisualDescendants().OfType<PenButtonCard>().ToList();

    private static Point At(Visual card, Visual root) => card.TranslatePoint(new Point(0, 0), root)!.Value;

    private static PenButtonsDiagram Diagram(PenButtonsView view) => view.FindControl<PenButtonsDiagram>("Diagram")!;

    // ── Buttons that do the same thing (#1045) ─────────────────────

    /// <summary>The pen picture is told which buttons match, so it can draw them in the shared colour as the cards
    /// do.</summary>
    [AvaloniaFact]
    public void TheButtonsThatShareAnAction_AreHandedToThePenPicture()
    {
        var (window, view) = Show(ThreeDefaults(), 1280);
        Assert.Equal(new[] { 1, 2, 3 }, Diagram(view).SharedButtons);
        window.Close();

        (window, view) = Show(Vm(PenButtonBinding.MakeDefault(1), null, PenButtonBinding.MakeDefault(3)), 1280);
        Assert.Equal(new[] { 1, 3 }, Diagram(view).SharedButtons);
        window.Close();

        (window, view) = Show(Vm(PenButtonBinding.MakeDefault(1), null, null), 1280);
        Assert.Empty(Diagram(view).SharedButtons!);
        window.Close();
    }

    // ── Side by side, or stacked ───────────────────────────────────

    [AvaloniaFact]
    public void WideEnough_TheCardsSitSideBySide_InOrder_AndTheLinesAreDrawn()
    {
        var (window, view) = Show(ThreeDefaults(), 1280);

        var cards = Cards(view);
        var places = cards.Select(c => At(c, view)).ToList();
        Assert.Equal(3, cards.Count);
        Assert.All(places, p => Assert.Equal(places[0].Y, p.Y));
        Assert.True(places[0].X < places[1].X && places[1].X < places[2].X);
        Assert.Equal(new[] { 1, 2, 3 }, cards.Select(c => ((PenButtonRowViewModel)c.DataContext!).Number));
        Assert.True(Diagram(view).ShowConnectors);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCardsInARow_AreTheSameWidth_AndFillTheRow()
    {
        var (window, view) = Show(ThreeDefaults(), 1280);

        var widths = Cards(view).Select(c => c.Bounds.Width).ToList();
        Assert.All(widths, w => Assert.Equal(widths[0], w, 1));
        Assert.Equal(1280, widths.Sum() + 2 * PenDiagramGeometry.CardGap, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void TooNarrowForARow_TheCardsStack_OneUnderAnother_AndTheLinesGo()
    {
        var (window, view) = Show(ThreeDefaults(), 900);

        var places = Cards(view).Select(c => At(c, view)).ToList();
        Assert.All(places, p => Assert.Equal(places[0].X, p.X));
        Assert.True(places[0].Y < places[1].Y && places[1].Y < places[2].Y);
        Assert.False(Diagram(view).ShowConnectors);
        window.Close();
    }

    [AvaloniaFact]
    public void TheBreakpoint_IsExactlyWhereThreeCardsStopFitting()
    {
        var (atLimit, atView) = Show(ThreeDefaults(), SideBySideAt);
        Assert.True(Diagram(atView).ShowConnectors);
        Assert.Single(Cards(atView).Select(c => At(c, atView).Y).Distinct());
        atLimit.Close();

        var (below, belowView) = Show(ThreeDefaults(), SideBySideAt - 1);
        Assert.False(Diagram(belowView).ShowConnectors);
        Assert.Equal(3, Cards(belowView).Select(c => At(c, belowView).Y).Distinct().Count());
        below.Close();
    }

    [AvaloniaFact]
    public void ResizingTheWindow_SwitchesBetweenTheTwoLayouts()
    {
        var (window, view) = Show(ThreeDefaults(), 1280);
        Assert.True(Diagram(view).ShowConnectors);

        window.Width = 900;
        Relayout(window, 900, 900);
        Assert.False(Diagram(view).ShowConnectors);
        Assert.Equal(3, Cards(view).Select(c => At(c, view).Y).Distinct().Count());

        window.Width = 1280;
        Relayout(window, 1280, 900);
        Assert.True(Diagram(view).ShowConnectors);
        window.Close();
    }

    // ── Fewer buttons ──────────────────────────────────────────────

    [AvaloniaFact]
    public void APenWithOneButton_GetsAHalfWidthCard_NotASlabAcrossThePage()
    {
        var (window, view) = Show(Vm(PenButtonBinding.MakeDefault(1)), 1280);

        var card = Assert.Single(Cards(view));
        Assert.InRange(card.Bounds.Width, 560, 700);
        Assert.Equal(1, Diagram(view).ButtonCount);
        window.Close();
    }

    [AvaloniaFact]
    public void APenWithTwoButtons_FillsTwoColumns()
    {
        var (window, view) = Show(Vm(PenButtonBinding.MakeDefault(1), null), 1280);

        var cards = Cards(view);
        Assert.Equal(2, cards.Count);
        Assert.Equal(At(cards[0], view).Y, At(cards[1], view).Y);
        Assert.Equal(1280, cards.Sum(c => c.Bounds.Width) + PenDiagramGeometry.CardGap, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCardCount_FollowsThePen_WhenItChanges()
    {
        var vm = ThreeDefaults();
        var (window, view) = Show(vm, 1280);

        vm.PenButtonRows = vm.PenButtonRows.Take(2).ToList();
        Relayout(window, 1280, 900);

        Assert.Equal(2, Cards(view).Count);
        Assert.Equal(2, Diagram(view).ButtonCount);
        window.Close();
    }

    // ── Cards beside each other ────────────────────────────────────

    [AvaloniaFact]
    public void ACardWithAWarning_DoesNotLeaveItsNeighboursRagged()
    {
        var (window, view) = Show(Vm(AuxKeyBinding.MakeMouseBinding("Right"), PenButtonBinding.MakeDefault(2),
            PenButtonBinding.MakeDefault(3)), 1280);

        var heights = Cards(view).Select(c => c.Bounds.Height).ToList();
        Assert.All(heights, h => Assert.Equal(heights[0], h, 1));
        window.Close();
    }

    [AvaloniaFact]
    public void AChoiceOnOneCard_NeverLightsAnotherCardsSegment()
    {
        var (window, view) = Show(ThreeDefaults(), 1280);
        var cards = Cards(view);

        PenButtonCardTests.Click(window, PenButtonCardTests.Segments(cards[1])["Disabled"]);

        // A write rebuilds the rows, so these are new cards; look again.
        cards = Cards(view);
        Assert.Equal(3, cards.Count);
        Assert.Equal(new[] { "Default action" }, PenButtonCardTests.Lit(cards[0]));
        Assert.Equal(new[] { "Disabled" }, PenButtonCardTests.Lit(cards[1]));
        Assert.Equal(new[] { "Default action" }, PenButtonCardTests.Lit(cards[2]));
        window.Close();
    }

    [AvaloniaFact]
    public void ACancelledChoice_LeavesEveryCardAsItWas()
    {
        // No editor is wired up here, so the shortcut choice backs out, and backing out writes nothing and
        // rebuilds nothing. That is the case where a click on one card could un-light the others for good.
        var (window, view) = Show(ThreeDefaults(), 1280);
        var cards = Cards(view);

        PenButtonCardTests.Click(window, PenButtonCardTests.Segments(cards[1])["Keyboard shortcut"]);

        Assert.All(cards, c => Assert.Equal(new[] { "Default action" }, PenButtonCardTests.Lit(c)));
        window.Close();
    }

    // ── Size and bindings ──────────────────────────────────────────

    [AvaloniaFact]
    public void TheWholeTab_FitsAComfortableWindow_WithAWarningAndAHint()
    {
        // The reason the cards used to hide their choices in a menu was height. With the cards across the page
        // the tab, warning and all, has to stay well inside a laptop window.
        var vm = Vm(AuxKeyBinding.MakeMouseBinding("Right"), PenButtonBinding.MakeDefault(2), PenButtonBinding.MakeDefault(3));
        var (window, view) = Show(vm, 1280, 1200);

        Assert.True(view.Bounds.Height <= 520, $"the buttons tab is {view.Bounds.Height:0} px tall");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1280)]
    [InlineData(900)]
    public void TheView_BindsCleanly_InBothLayouts(double width)
    {
        using var errors = BindingErrors.Capture();
        var (window, _) = Show(ThreeDefaults(), width);

        errors.AssertNone($"{nameof(PenButtonsView)} at {width}");
        window.Close();
    }
}
