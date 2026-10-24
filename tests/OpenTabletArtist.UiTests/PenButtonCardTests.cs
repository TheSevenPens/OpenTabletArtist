using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The pen button card (#1015) as it is drawn: the saved binding decides which radio is checked, a warning
/// appears for a mouse click under Windows Ink with a one-click fix, and a choice the user backs out of
/// leaves the card showing what is really saved. The last one is a property of how the radios are bound
/// (one-way, with a click command), which only a real control can show.
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
        var card = new PenButtonCard { DataContext = row };
        var window = new Window { Content = card, Width = 400, Height = 500 };
        window.Show();
        window.Measure(new Size(400, 500));
        window.Arrange(new Rect(0, 0, 400, 500));
        Dispatcher.UIThread.RunJobs();
        return (window, card, row, spy);
    }

    private static RadioButton Radio(PenButtonCard card, string content) =>
        card.GetVisualDescendants().OfType<RadioButton>().Single(r => (r.Content as string) == content);

    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button? ButtonNamed(PenButtonCard card, string content) =>
        card.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == content);

    [AvaloniaFact]
    public void DefaultAction_IsTheCheckedRadio_AndNamesTheBarrelSwitch()
    {
        var (window, card, _, _) = Show(PenButtonBinding.MakeDefault(1));

        Assert.True(Radio(card, "Default action").IsChecked);
        Assert.False(Radio(card, "Keyboard shortcut").IsChecked);
        Assert.False(Radio(card, "Disabled").IsChecked);
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Barrel Switch") == true);
        window.Close();
    }

    [AvaloniaFact]
    public void NoBinding_ChecksDisabled()
    {
        var (window, card, _, _) = Show(null);

        Assert.True(Radio(card, "Disabled").IsChecked);
        Assert.False(Radio(card, "Default action").IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void ARightClickUnderWindowsInk_ChecksNoRadio_AndWarnsWithAFix()
    {
        var (window, card, _, spy) = Show(AuxKeyBinding.MakeMouseBinding("Right"));

        Assert.All(new[] { "Default action", "Keyboard shortcut", "Disabled" }, c => Assert.False(Radio(card, c).IsChecked));
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == BindingSafety.Message(BindingHazard.MouseClickUnderWindowsInk));
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

        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == BindingSafety.Message(BindingHazard.MouseClickUnderWindowsInk));
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
        Assert.True(Radio(card, "Default action").IsChecked);
        Assert.False(row.HasWarning);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingDisabled_WritesNothingBound()
    {
        var (window, card, _, spy) = Show(PenButtonBinding.MakeDefault(1));

        Click(window, Radio(card, "Disabled"));

        Assert.Equal(1, spy.Applied);
        Assert.Null(spy.Last);
        Assert.True(Radio(card, "Disabled").IsChecked);
        Assert.False(Radio(card, "Default action").IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void BackingOutOfTheShortcutEditor_PutsTheCheckBackOnWhatIsSaved()
    {
        var (window, card, _, spy) = Show(PenButtonBinding.MakeDefault(1));
        spy.EditorResult = null;                // the user cancels the editor

        Click(window, Radio(card, "Keyboard shortcut"));

        Assert.Equal(1, spy.EditorOpened);
        Assert.Equal(0, spy.Applied);
        // The radio drew itself checked when clicked; the card has to say again which one is really saved.
        Assert.True(Radio(card, "Default action").IsChecked);
        Assert.False(Radio(card, "Keyboard shortcut").IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingAShortcut_ChecksIt_AndShowsWhatItIs()
    {
        var (window, card, _, spy) = Show(null);
        spy.EditorResult = new AuxBinding(AuxKind.Keyboard, new AuxCombo(true, false, false, "Z"),
            AuxKeyBinding.None, AuxKeyBinding.None);

        Click(window, Radio(card, "Keyboard shortcut"));

        Assert.Equal(1, spy.Applied);
        Assert.True(Radio(card, "Keyboard shortcut").IsChecked);
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Ctrl + Z");
        window.Close();
    }

    [AvaloniaFact]
    public void TheRadiosOfTwoCards_DoNotUncheckEachOther()
    {
        // Each card's radios share a parent and no group name, so cards are independent.
        var (w1, c1, _, _) = Show(PenButtonBinding.MakeDefault(1));
        var (w2, c2, _, _) = Show(null, index: 2);

        Assert.True(Radio(c1, "Default action").IsChecked);
        Assert.True(Radio(c2, "Disabled").IsChecked);
        w1.Close();
        w2.Close();
    }

    [AvaloniaFact]
    public void TheSharedBarrelHint_ShowsWhenSet()
    {
        var (window, card, row, _) = Show(PenButtonBinding.MakeDefault(1));
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text?.Contains("same thing") == true && t.IsEffectivelyVisible);

        row.SharedBarrelHint = "Does the same thing as button 2 in Windows Ink mode.";
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text?.Contains("same thing") == true && t.IsEffectivelyVisible);
        window.Close();
    }
}
