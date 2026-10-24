using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The pen button card (#1015) as it is drawn: a title, one line saying what the button does, and a "…"
/// menu with the three choices and a tick against the saved one. A mouse click under Windows Ink shows a
/// warning with a one-click fix, nothing is rewritten by showing it, and the card is no taller than the tip
/// and eraser cards in its ordinary states (the first version stacked radios and was up to four times as tall).
/// </summary>
public class PenButtonCardTests
{
    private sealed class Spy
    {
        public int Applied;
        public PluginSettingStore? Last;
        public AuxBinding? EditorResult;
        public int EditorOpened;
    }

    private static (Window Window, PenButtonCard Card, PenButtonRowViewModel Row, Spy Spy) Show(
        PluginSettingStore? store, bool ink = true, int index = 1)
    {
        var spy = new Spy();
        var row = new PenButtonRowViewModel(index, store, ink, canEdit: true,
            applyAsync: (_, s) => { spy.Applied++; spy.Last = s; return Task.CompletedTask; },
            editShortcut: (_, _, _) => { spy.EditorOpened++; return Task.FromResult(spy.EditorResult); });
        var card = new PenButtonCard { DataContext = row, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = card, Width = 400, Height = 500 };
        window.Show();
        window.Measure(new Size(400, 500));
        window.Arrange(new Rect(0, 0, 400, 500));
        Dispatcher.UIThread.RunJobs();
        return (window, card, row, spy);
    }

    private static IEnumerable<TextBlock> Texts(PenButtonCard card) => card.GetVisualDescendants().OfType<TextBlock>();

    /// <summary>Open the "…" menu and return its entries by header. Opened the way a click opens it, so the
    /// entries get the row as their data context.</summary>
    private static Dictionary<string, MenuItem> OpenMenu(Window window, PenButtonCard card)
    {
        var button = card.GetVisualDescendants().OfType<IconButton>().Single();
        var flyout = (MenuFlyout)button.Flyout!;
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return flyout.Items.OfType<MenuItem>().ToDictionary(m => (string)m.Header!);
    }

    private static bool Ticked(MenuItem item) => item.Icon is Control { IsVisible: true };

    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button? ButtonNamed(PenButtonCard card, string content) =>
        card.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == content);

    // ── What the card says ─────────────────────────────────────────

    [AvaloniaFact]
    public void DefaultAction_SaysBarrelSwitch_AndTicksDefaultAction()
    {
        var (window, card, _, _) = Show(PenButtonBinding.MakeDefault(1));
        var menu = OpenMenu(window, card);

        Assert.Contains(Texts(card), t => t.Text == "Barrel Switch");
        Assert.True(Ticked(menu["Default action"]));
        Assert.False(Ticked(menu["Keyboard shortcut…"]));
        Assert.False(Ticked(menu["Disabled"]));
        window.Close();
    }

    [AvaloniaFact]
    public void DefaultAction_TellsOnHoverThatYourDrawingSoftwareDecides()
    {
        var (window, card, _, _) = Show(PenButtonBinding.MakeDefault(1));

        var line = Texts(card).Single(t => t.Text == "Barrel Switch");

        Assert.Contains("drawing software", (string)ToolTip.GetTip(line)!);
        window.Close();
    }

    [AvaloniaFact]
    public void NoBinding_SaysDisabled_AndTicksDisabled()
    {
        var (window, card, _, _) = Show(null);
        var menu = OpenMenu(window, card);

        Assert.Contains(Texts(card), t => t.Text == "Disabled");
        Assert.True(Ticked(menu["Disabled"]));
        Assert.False(Ticked(menu["Default action"]));
        window.Close();
    }

    [AvaloniaFact]
    public void TheMenu_NamesTheThreeChoices()
    {
        var (window, card, _, _) = Show(null);

        Assert.Equal(new[] { "Default action", "Keyboard shortcut…", "Disabled" }, OpenMenu(window, card).Keys.ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void AFourthButton_HasNoDefaultActionEntry()
    {
        var (window, card, _, _) = Show(null, index: 4);

        Assert.False(OpenMenu(window, card)["Default action"].IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void UnderMouseOutput_TheShortcutEntryIsNamedForClicksToo()
    {
        var (window, card, _, _) = Show(null, ink: false);

        Assert.Contains("Shortcut or mouse click…", OpenMenu(window, card).Keys);
        window.Close();
    }

    // ── A binding set somewhere else ───────────────────────────────

    [AvaloniaFact]
    public void ARightClickUnderWindowsInk_TicksNothing_SaysWhatItIs_AndWarnsWithAFix()
    {
        var (window, card, _, spy) = Show(AuxKeyBinding.MakeMouseBinding("Right"));
        var menu = OpenMenu(window, card);

        Assert.All(menu.Values, m => Assert.False(Ticked(m)));
        Assert.Contains(Texts(card), t => t.Text == "Currently: Right click");
        Assert.Contains(Texts(card), t => t.Text == BindingSafety.Message(BindingHazard.MouseClickUnderWindowsInk));
        var fix = ButtonNamed(card, "Switch to Barrel Switch");
        Assert.NotNull(fix);
        Assert.True(fix!.IsEffectivelyVisible);
        Assert.Equal(0, spy.Applied);           // showing it changed nothing
        window.Close();
    }

    [AvaloniaFact]
    public void AMiddleClickUnderWindowsInk_WarnsWithoutAFix()
    {
        var (window, card, _, _) = Show(AuxKeyBinding.MakeMouseBinding("Middle"));

        Assert.Contains(Texts(card), t => t.Text == BindingSafety.Message(BindingHazard.MouseClickUnderWindowsInk));
        Assert.True(ButtonNamed(card, "Switch to Barrel Switch") is not { IsEffectivelyVisible: true });
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingTheFix_WritesTheBarrelSwitch_AndTheWarningGoes()
    {
        var (window, card, row, spy) = Show(AuxKeyBinding.MakeMouseBinding("Right"));

        Click(window, ButtonNamed(card, "Switch to Barrel Switch")!);

        Assert.Equal(1, spy.Applied);
        Assert.Equal(PenButtonAction.Default, PenButtonBinding.Classify(spy.Last, 1, outputIsWindowsInk: true));
        Assert.Contains(Texts(card), t => t.Text == "Barrel Switch");
        Assert.False(row.HasWarning);
        window.Close();
    }

    // ── Choosing from the menu ─────────────────────────────────────

    [AvaloniaFact]
    public void ChoosingDisabled_WritesNothingBound_AndMovesTheTick()
    {
        var (window, card, _, spy) = Show(PenButtonBinding.MakeDefault(1));
        var menu = OpenMenu(window, card);

        menu["Disabled"].Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, spy.Applied);
        Assert.Null(spy.Last);
        Assert.Contains(Texts(card), t => t.Text == "Disabled");
        Assert.True(Ticked(menu["Disabled"]));
        Assert.False(Ticked(menu["Default action"]));
        window.Close();
    }

    [AvaloniaFact]
    public void BackingOutOfTheShortcutEditor_ChangesNothing()
    {
        var (window, card, _, spy) = Show(PenButtonBinding.MakeDefault(1));
        spy.EditorResult = null;                // the user cancels the editor
        var menu = OpenMenu(window, card);

        menu["Keyboard shortcut…"].Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, spy.EditorOpened);
        Assert.Equal(0, spy.Applied);
        Assert.Contains(Texts(card), t => t.Text == "Barrel Switch");
        Assert.True(Ticked(menu["Default action"]));
        Assert.False(Ticked(menu["Keyboard shortcut…"]));
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingAShortcut_ShowsItAndTicksIt()
    {
        var (window, card, _, spy) = Show(null);
        spy.EditorResult = new AuxBinding(AuxKind.Keyboard, new AuxCombo(true, false, false, "Z"),
            AuxKeyBinding.None, AuxKeyBinding.None);
        var menu = OpenMenu(window, card);

        menu["Keyboard shortcut…"].Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, spy.Applied);
        Assert.Contains(Texts(card), t => t.Text == "Ctrl + Z");
        Assert.True(Ticked(menu["Keyboard shortcut…"]));
        window.Close();
    }

    [AvaloniaFact]
    public void WhenTheHostCannotEdit_TheMenuEntriesAreDisabled()
    {
        var row = new PenButtonRowViewModel(1, PenButtonBinding.MakeDefault(1), true, canEdit: false,
            (_, _) => Task.CompletedTask, (_, _, _) => Task.FromResult<AuxBinding?>(null));
        var card = new PenButtonCard { DataContext = row };
        var window = new Window { Content = card, Width = 400, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var menu = OpenMenu(window, card);

        Assert.All(menu.Values, m => Assert.False(m.Command!.CanExecute(null)));
        window.Close();
    }

    // ── Hint and size ──────────────────────────────────────────────

    [AvaloniaFact]
    public void TheSharedBarrelHint_ShowsWhenSet()
    {
        var (window, card, row, _) = Show(PenButtonBinding.MakeDefault(1));
        Assert.DoesNotContain(Texts(card), t => t.Text?.Contains("same thing") == true && t.IsEffectivelyVisible);

        row.SharedBarrelHint = "Does the same thing as button 2 in Windows Ink mode.";
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(Texts(card), t => t.Text?.Contains("same thing") == true && t.IsEffectivelyVisible);
        window.Close();
    }

    private static double HeightOf(Control card)
    {
        var host = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        host.Children.Add(card);
        var window = new Window { Content = host, Width = 600, Height = 1200 };
        window.Show();
        window.Measure(new Size(600, 1200));
        window.Arrange(new Rect(0, 0, 600, 1200));
        Dispatcher.UIThread.RunJobs();
        var h = card.Bounds.Height;
        window.Close();
        return h;
    }

    private static PenButtonCard CardFor(PluginSettingStore? store, int index = 1) =>
        new()
        {
            DataContext = new PenButtonRowViewModel(index, store, true, true, (_, _) => Task.CompletedTask,
            (_, _, _) => Task.FromResult<AuxBinding?>(null))
        };

    [AvaloniaFact]
    public void AButtonCard_IsAboutAsTallAsTheTipCard_InItsOrdinaryStates()
    {
        // The reason the card is a menu and not radios (#1015 review): three Default action cards used to
        // make the switches column about 850 px, taller than the window. In its ordinary states a button card
        // must stay close to the 55 px a tip or eraser card takes.
        var tip = HeightOf(new PenSwitchCard
        {
            DataContext = new PenSwitchRowViewModel(
            PenSwitchKind.Tip, 1, PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip), true, (_, _, _) => Task.CompletedTask)
        });

        foreach (var (name, store) in new (string, PluginSettingStore?)[]
                 {
                     ("default action", PenButtonBinding.MakeDefault(1)),
                     ("disabled", null),
                     ("shortcut", AuxKeyBinding.MakeKeyBinding("B")),
                 })
        {
            var h = HeightOf(CardFor(store));
            Assert.True(h <= tip + 25, $"{name}: {h:0} px against {tip:0} px for a tip card");
        }
    }

    [AvaloniaFact]
    public void TheWholeSwitchesColumn_FitsAComfortableWindow()
    {
        // Tip, eraser and three buttons all on the Barrel Switch (the tallest ordinary case, with the hint).
        var tip = new PenSwitchRowViewModel(PenSwitchKind.Tip, 1, PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip), true, (_, _, _) => Task.CompletedTask);
        var eraser = new PenSwitchRowViewModel(PenSwitchKind.Eraser, 1, PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser), true, (_, _, _) => Task.CompletedTask);
        var buttons = Enumerable.Range(1, 3).Select(i => new PenButtonRowViewModel(i, PenButtonBinding.MakeDefault(i), true, true,
            (_, _) => Task.CompletedTask, (_, _, _) => Task.FromResult<AuxBinding?>(null))).ToList();
        TabletDetailViewModel.NoteSharedBarrel(buttons, true);

        var column = new StackPanel { Spacing = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        column.Children.Add(new PenSwitchCard { DataContext = tip });
        column.Children.Add(new PenSwitchCard { DataContext = eraser });
        foreach (var b in buttons) column.Children.Add(new PenButtonCard { DataContext = b });
        var window = new Window { Content = column, Width = 600, Height = 2000 };
        window.Show();
        window.Measure(new Size(600, 2000));
        window.Arrange(new Rect(0, 0, 600, 2000));
        Dispatcher.UIThread.RunJobs();

        var height = column.Bounds.Height;
        window.Close();
        Assert.True(height <= 700, $"the switches column is {height:0} px tall");
    }
}
