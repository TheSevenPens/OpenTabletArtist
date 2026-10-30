using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The pen button card (#1015) as it is drawn: a numbered header, the three choices always in view as one
/// segmented bar with the saved one lit, and a detail area that says what the choice means. A mouse click under
/// Windows Ink shows a warning with a one-click fix, nothing is rewritten by showing it, and a choice that is
/// backed out of leaves the bar as it was.
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
        PluginSettingStore? store, bool ink = true, int index = 1, bool canEdit = true)
    {
        var spy = new Spy();
        var row = new PenButtonRowViewModel(index, store, ink, canEdit,
            applyAsync: (_, s) => { spy.Applied++; spy.Last = s; return Task.CompletedTask; },
            editShortcut: (_, _, _) => { spy.EditorOpened++; return Task.FromResult(spy.EditorResult); });
        var card = new PenButtonCard { DataContext = row, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = card, Width = 420, Height = 500 };
        window.Show();
        window.Measure(new Size(420, 500));
        window.Arrange(new Rect(0, 0, 420, 500));
        Dispatcher.UIThread.RunJobs();
        return (window, card, row, spy);
    }

    private static IEnumerable<TextBlock> Texts(Control root) => root.GetVisualDescendants().OfType<TextBlock>();

    private static bool Shown(Control root, string text) =>
        Texts(root).Any(t => t.Text == text && t.IsEffectivelyVisible);

    private static bool ShownContaining(Control root, string text) =>
        Texts(root).Any(t => t.Text?.Contains(text) == true && t.IsEffectivelyVisible);

    /// <summary>The segments of the choice bar, left to right, by their label.</summary>
    internal static Dictionary<string, RadioButton> Segments(Control root) =>
        root.GetVisualDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("segment"))
            .Where(r => r.Content is string)
            .ToDictionary(r => (string)r.Content!);

    internal static string[] Lit(Control root) =>
        Segments(root).Where(s => s.Value.IsChecked == true && s.Value.IsEffectivelyVisible).Select(s => s.Key).ToArray();

    internal static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button? ButtonNamed(Control root, string content) =>
        root.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == content);

    // ── What the card says ─────────────────────────────────────────

    [AvaloniaFact]
    public void TheCard_NamesItsButton_AndWhereItSits()
    {
        var (window, card, _, _) = Show(null, index: 2);

        Assert.True(Shown(card, "BUTTON 2"));
        Assert.True(Shown(card, "2"));                          // the number on the pen diagram
        Assert.True(Shown(card, "middle"));                     // button 2 of a pen's three
        window.Close();
    }

    [AvaloniaFact]
    public void DefaultAction_SaysBarrelSwitch_AndLightsOnlyDefaultAction()
    {
        var (window, card, _, _) = Show(PenButtonBinding.MakeDefault(1));

        Assert.False(Shown(card, "Barrel Switch"), "the lit segment already says it (#1046)");
        Assert.True(ShownContaining(card, "drawing software"));
        Assert.Equal(new[] { "Default action" }, Lit(card));
        window.Close();
    }

    [AvaloniaFact]
    public void DefaultAction_SaysOutLoud_ThatYourDrawingSoftwareDecides()
    {
        var (window, card, _, _) = Show(PenButtonBinding.MakeDefault(1));

        Assert.Contains(Texts(card), t => t.IsEffectivelyVisible && t.Text?.Contains("drawing software") == true);
        window.Close();
    }

    [AvaloniaFact]
    public void NoBinding_HasNoTitle_SaysItIsIgnored_AndLightsDisabled()
    {
        var (window, card, _, _) = Show(null);

        Assert.False(Shown(card, "Does nothing"), "the lit segment already says Disabled (#1046)");
        Assert.True(Shown(card, "The button is ignored."));
        Assert.Equal(new[] { "Disabled" }, Lit(card));
        window.Close();
    }

    [AvaloniaFact]
    public void AShortcut_IsDrawnAsKeycaps_WithAChangeButton_AndHowLongItIsHeld()
    {
        var (window, card, _, _) = Show(AuxKeyBinding.MakeBinding(new AuxCombo(true, false, false, "Z")));

        var caps = card.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("keycap") && b.IsEffectivelyVisible)
            .Select(b => ((TextBlock)b.Child!).Text).ToArray();
        Assert.Equal(new[] { "Ctrl", "Z" }, caps);
        Assert.True(ButtonNamed(card, "Change…")!.IsEffectivelyVisible);
        Assert.True(Shown(card, "Held while you hold the button."));
        Assert.Equal(new[] { "Keyboard shortcut" }, Lit(card));
        window.Close();
    }

    [AvaloniaFact]
    public void TheDetailForTheOtherChoices_IsNotShown()
    {
        var (window, card, _, _) = Show(PenButtonBinding.MakeDefault(1));

        Assert.False(Shown(card, "The button is ignored."));
        Assert.False(Shown(card, "Held while you hold the button."));
        Assert.True(ButtonNamed(card, "Change…") is not { IsEffectivelyVisible: true });
        window.Close();
    }

    // ── The three choices are always in view ───────────────────────

    [AvaloniaFact]
    public void TheThreeChoices_AreInViewAtOnce_NotBehindAMenu()
    {
        var (window, card, _, _) = Show(null);

        Assert.Equal(new[] { "Default action", "Keyboard shortcut", "Disabled" }, Segments(card).Keys.ToArray());
        Assert.All(Segments(card).Values, s => Assert.True(s.IsEffectivelyVisible));
        Assert.Empty(card.GetVisualDescendants().OfType<MenuFlyoutPresenter>());
        window.Close();
    }

    [AvaloniaFact]
    public void AFourthButton_HasNoDefaultActionSegment()
    {
        var (window, card, _, _) = Show(null, index: 4);

        Assert.False(Segments(card)["Default action"].IsEffectivelyVisible);
        Assert.True(Segments(card)["Keyboard shortcut"].IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void UnderMouseOutput_TheShortcutSegmentIsNamedForClicksToo()
    {
        var (window, card, _, _) = Show(null, ink: false);

        Assert.Contains("Shortcut or mouse click", Segments(card).Keys);
        window.Close();
    }

    // ── A binding set somewhere else ───────────────────────────────

    [AvaloniaFact]
    public void ARightClickUnderWindowsInk_LightsNothing_SaysWhatItIs_AndWarnsWithAFix()
    {
        var (window, card, _, spy) = Show(AuxKeyBinding.MakeMouseBinding("Right"));

        Assert.Empty(Lit(card));
        Assert.True(Shown(card, "Currently: Right click"));
        Assert.True(Shown(card, BindingSafety.Message(BindingHazard.MouseClickUnderWindowsInk)!));
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

        Assert.True(Shown(card, BindingSafety.Message(BindingHazard.MouseClickUnderWindowsInk)!));
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
        Assert.False(Shown(card, "Barrel Switch"), "the lit segment already says it (#1046)");
        Assert.True(ShownContaining(card, "drawing software"));
        Assert.Equal(new[] { "Default action" }, Lit(card));
        Assert.False(row.HasWarning);
        window.Close();
    }

    // ── Choosing ───────────────────────────────────────────────────

    [AvaloniaFact]
    public void ClickingDisabled_WritesNothingBound_AndLightsIt()
    {
        var (window, card, _, spy) = Show(PenButtonBinding.MakeDefault(1));

        Click(window, Segments(card)["Disabled"]);

        Assert.Equal(1, spy.Applied);
        Assert.Null(spy.Last);
        Assert.False(Shown(card, "Does nothing"), "the lit segment already says Disabled (#1046)");
        Assert.True(Shown(card, "The button is ignored."));
        Assert.Equal(new[] { "Disabled" }, Lit(card));
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingDefaultAction_WritesTheAdaptiveBinding_AndLightsIt()
    {
        var (window, card, _, spy) = Show(null);

        Click(window, Segments(card)["Default action"]);

        Assert.Equal(1, spy.Applied);
        Assert.Equal(PenButtonAction.Default, PenButtonBinding.Classify(spy.Last, 1, outputIsWindowsInk: true));
        Assert.Equal(new[] { "Default action" }, Lit(card));
        window.Close();
    }

    [AvaloniaFact]
    public void BackingOutOfTheShortcutEditor_PutsTheBarBack_AndChangesNothing()
    {
        var (window, card, _, spy) = Show(PenButtonBinding.MakeDefault(1));
        spy.EditorResult = null;                // the user cancels the editor

        Click(window, Segments(card)["Keyboard shortcut"]);

        Assert.Equal(1, spy.EditorOpened);
        Assert.Equal(0, spy.Applied);
        Assert.False(Shown(card, "Barrel Switch"), "the lit segment already says it (#1046)");
        Assert.True(ShownContaining(card, "drawing software"));
        // The segment drew itself lit when clicked; backing out must not leave it that way.
        Assert.Equal(new[] { "Default action" }, Lit(card));
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingAShortcut_ShowsItsKeys_AndLightsTheSegment()
    {
        var (window, card, _, spy) = Show(null);
        spy.EditorResult = new AuxBinding(AuxKind.Keyboard, new AuxCombo(true, false, false, "Z"),
            AuxKeyBinding.None, AuxKeyBinding.None);

        Click(window, Segments(card)["Keyboard shortcut"]);

        Assert.Equal(1, spy.Applied);
        Assert.True(Shown(card, "Z"));
        Assert.Equal(new[] { "Keyboard shortcut" }, Lit(card));
        window.Close();
    }

    [AvaloniaFact]
    public void Change_ReopensTheEditor_AndReplacesTheShortcut()
    {
        var (window, card, _, spy) = Show(AuxKeyBinding.MakeBinding(new AuxCombo(true, false, false, "Z")));
        spy.EditorResult = new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, true, false, "X"),
            AuxKeyBinding.None, AuxKeyBinding.None);

        Click(window, ButtonNamed(card, "Change…")!);

        Assert.Equal(1, spy.EditorOpened);
        Assert.Equal(1, spy.Applied);
        Assert.True(Shown(card, "X"));
        Assert.False(Shown(card, "Z"));
        window.Close();
    }

    [AvaloniaFact]
    public void WhenTheHostCannotEdit_TheChoicesAreGreyedOut()
    {
        var (window, card, _, _) = Show(PenButtonBinding.MakeDefault(1), canEdit: false);

        Assert.All(Segments(card).Values, s => Assert.False(s.IsEffectivelyEnabled));
        window.Close();
    }

    // ── Hint ───────────────────────────────────────────────────────

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

    // ── Titles (#1046) ─────────────────────────────────────────────

    /// <summary>The title stays where it says something the lit segment does not: a button set somewhere else
    /// has none lit, and the Default action outside Windows Ink is a particular mouse click.</summary>
    [AvaloniaFact]
    public void ABindingSetElsewhere_StillSaysWhatItDoesNow()
    {
        var (window, card, _, _) = Show(AuxKeyBinding.MakeMouseBinding("Right"));

        Assert.True(Shown(card, "Currently: Right click"));
        window.Close();
    }

    [AvaloniaFact]
    public void TheDefaultActionOutsideWindowsInk_KeepsItsTitle()
    {
        var (window, card, _, _) = Show(PenButtonBinding.MakeDefault(1), ink: false);

        Assert.True(Shown(card, "Right click"));
        window.Close();
    }

    // ── Buttons that do the same thing (#1045) ─────────────────────

    /// <summary>The number takes the shared colour and a tag says which buttons it matches, so the likeness is
    /// not something to work out from a sentence. Without it the number is the accent and there is no tag.</summary>
    [AvaloniaFact]
    public void AButtonThatSharesItsAction_WearsTheSharedColour_AndSaysWhoItMatches()
    {
        var (window, card, row, _) = Show(PenButtonBinding.MakeDefault(1));
        var shared = Color.Parse("#0F766E");

        Assert.False(Shown(card, "same as 2"));
        var before = NumberColor(card);
        Assert.NotEqual(shared, before);

        row.SameAsLabel = "same as 2";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Shown(card, "same as 2"));
        Assert.Equal(shared, NumberColor(card));
        window.Close();
    }

    private static Color NumberColor(Control card) =>
        ((ISolidColorBrush)card.GetVisualDescendants().OfType<Ellipse>().Single(e => e.IsEffectivelyVisible).Fill!).Color;
}
