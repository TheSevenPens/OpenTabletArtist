using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

// #1015: a pen barrel button as Default action / Keyboard shortcut / Disabled, and the promise that a
// binding set anywhere else is described and warned about but never rewritten.
public class PenButtonRowViewModelTests
{
    private const bool Ink = true;
    private const bool MouseOutput = false;

    private sealed class Recorder
    {
        public List<(int Index, PluginSettingStore? Store)> Applied { get; } = new();
        public Task Apply(int index, PluginSettingStore? store) { Applied.Add((index, store)); return Task.CompletedTask; }

        public List<(AuxBinding Current, BindingEditorOptions Options)> EditorOpened { get; } = new();
        public AuxBinding? EditorResult { get; set; }
        public Task<AuxBinding?> Edit(AuxBinding current, string title, BindingEditorOptions options)
        {
            EditorOpened.Add((current, options));
            return Task.FromResult(EditorResult);
        }
    }

    private static PenButtonRowViewModel Row(Recorder r, PluginSettingStore? store, bool ink = Ink, int index = 1,
        bool canEdit = true) =>
        new(index, store, ink, canEdit, r.Apply, r.Edit);

    private static AuxBinding Shortcut(bool ctrl, bool shift, bool alt, string key) =>
        new(AuxKind.Keyboard, new AuxCombo(ctrl, shift, alt, key), AuxKeyBinding.None, AuxKeyBinding.None);

    // ── What the card shows ────────────────────────────────────────

    [Fact]
    public void NoBinding_ShowsDisabled()
    {
        var row = Row(new Recorder(), null);

        Assert.True(row.IsDisabled);
        Assert.False(row.IsDefaultAction);
        Assert.False(row.IsShortcut);
        Assert.Equal("Disabled", row.Summary);
        Assert.False(row.HasWarning);
        Assert.Equal("BUTTON 1", row.SectionLabel);
    }

    [Fact]
    public void Adaptive_ShowsDefaultAction_WithTheBarrelSwitchUnderWindowsInk()
    {
        var row = Row(new Recorder(), PenButtonBinding.MakeDefault(1));

        Assert.True(row.IsDefaultAction);
        Assert.Contains("Barrel Switch", row.DefaultActionText);
        Assert.True(row.ShowDefaultActionHelp);
        Assert.Contains("drawing software", row.DefaultActionHelp);
    }

    [Fact]
    public void Adaptive_UnderMouseOutput_NamesTheMouseButtonAndSkipsTheInkHelp()
    {
        var one = Row(new Recorder(), PenButtonBinding.MakeDefault(1), MouseOutput, index: 1);
        var two = Row(new Recorder(), PenButtonBinding.MakeDefault(2), MouseOutput, index: 2);

        Assert.Equal("Right click", one.DefaultActionText);
        Assert.Equal("Middle click", two.DefaultActionText);
        Assert.False(one.ShowDefaultActionHelp);
    }

    [Fact]
    public void KeyboardBinding_ShowsShortcut_WithItsName()
    {
        var row = Row(new Recorder(), AuxKeyBinding.MakeBinding(Shortcut(true, false, false, "Z").Combo));

        Assert.True(row.IsShortcut);
        Assert.Equal("Ctrl + Z", row.Summary);
        Assert.True(row.ShowShortcutSummary);
        Assert.Equal("Keyboard shortcut", row.ShortcutLabel);
    }

    [Fact]
    public void UnderMouseOutput_TheShortcutChoice_IsNamedForClicksToo()
    {
        Assert.Equal("Shortcut or mouse click", Row(new Recorder(), null, MouseOutput).ShortcutLabel);
    }

    [Fact]
    public void FourthButton_HasNoDefaultAction()
    {
        Assert.False(Row(new Recorder(), null, index: 4).CanUseDefaultAction);
        Assert.True(Row(new Recorder(), null, index: 3).CanUseDefaultAction);
    }

    // ── A binding set elsewhere: described, warned about, never rewritten ──

    [Fact]
    public void RightClickUnderWindowsInk_IsDescribedAndWarned_NoRadioSelected_NothingWritten()
    {
        var r = new Recorder();
        var row = Row(r, AuxKeyBinding.MakeMouseBinding("Right"));

        Assert.True(row.IsOther);
        Assert.False(row.IsDefaultAction || row.IsShortcut || row.IsDisabled);
        Assert.Equal("Right click", row.Summary);
        Assert.True(row.HasWarning);
        Assert.Equal(BindingSafety.Message(BindingHazard.MouseClickUnderWindowsInk), row.Warning);
        Assert.True(row.CanSwitchToBarrel);
        Assert.Empty(r.Applied);                // building the row changed nothing
    }

    [Theory]
    [InlineData("Left")]
    [InlineData("Middle")]
    [InlineData("Backward")]
    [InlineData("Forward")]
    public void OtherMouseButtonsUnderWindowsInk_AreWarned_WithoutAOneClickFix(string button)
    {
        var row = Row(new Recorder(), AuxKeyBinding.MakeMouseBinding(button));

        Assert.True(row.HasWarning);
        Assert.False(row.CanSwitchToBarrel);    // only a right click has a Windows Ink equivalent
        Assert.False(row.SwitchToBarrelCommand.CanExecute(null));
    }

    [Fact]
    public void ScrollUnderWindowsInk_IsWarned_WithoutAFix()
    {
        var row = Row(new Recorder(), AuxKeyBinding.MakeScrollBinding("Up"));

        Assert.Equal(BindingSafety.Message(BindingHazard.ScrollUnderWindowsInk), row.Warning);
        Assert.False(row.CanSwitchToBarrel);
    }

    [Fact]
    public void MouseClickUnderMouseOutput_IsJustTheShortcutChoice_NoWarning()
    {
        var row = Row(new Recorder(), AuxKeyBinding.MakeMouseBinding("Right"), MouseOutput);

        Assert.True(row.IsShortcut);
        Assert.False(row.HasWarning);
        Assert.False(row.CanSwitchToBarrel);
    }

    [Fact]
    public void WindowsInkBindingWithoutWindowsInk_IsWarned()
    {
        var row = Row(new Recorder(), PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.PenButton), MouseOutput);

        Assert.True(row.IsOther);
        Assert.Equal(BindingSafety.Message(BindingHazard.WindowsInkBindingWithoutWindowsInk), row.Warning);
    }

    [Fact]
    public void RightClickWarning_ButNoDefaultActionForThisButton_OffersNoFix()
    {
        // A fourth button has no Adaptive Binding to switch to.
        var row = Row(new Recorder(), AuxKeyBinding.MakeMouseBinding("Right"), index: 4);

        Assert.True(row.HasWarning);
        Assert.False(row.CanSwitchToBarrel);
    }

    // ── Choosing ───────────────────────────────────────────────────

    [Fact]
    public async Task SwitchToBarrel_WritesTheAdaptiveBindingForThisButton()
    {
        var r = new Recorder();
        var row = Row(r, AuxKeyBinding.MakeMouseBinding("Right"), index: 2);

        await row.SwitchToBarrelCommand.ExecuteAsync(null);

        var (index, store) = Assert.Single(r.Applied);
        Assert.Equal(2, index);
        Assert.Equal(PenButtonAction.Default, PenButtonBinding.Classify(store, 2, Ink));
        Assert.True(row.IsDefaultAction);
        Assert.False(row.HasWarning);
    }

    [Fact]
    public async Task UseDefaultAction_WritesTheAdaptiveBinding()
    {
        var r = new Recorder();
        var row = Row(r, null, index: 3);

        await row.UseDefaultActionCommand.ExecuteAsync(null);

        var (index, store) = Assert.Single(r.Applied);
        Assert.Equal(3, index);
        Assert.Equal(PenButtonAction.Default, PenButtonBinding.Classify(store, 3, Ink));
    }

    [Fact]
    public async Task Disable_WritesANullEntry_AtTheButtonsIndex()
    {
        var r = new Recorder();
        var row = Row(r, PenButtonBinding.MakeDefault(2), index: 2);

        await row.DisableCommand.ExecuteAsync(null);

        var (index, store) = Assert.Single(r.Applied);
        Assert.Equal(2, index);
        Assert.Null(store);
        Assert.True(row.IsDisabled);
    }

    [Fact]
    public async Task UseShortcut_OpensTheEditorOnTheSavedShortcut_AndWritesWhatIsSaved()
    {
        var r = new Recorder { EditorResult = Shortcut(true, false, false, "Z") };
        var row = Row(r, AuxKeyBinding.MakeKeyBinding("B"));

        await row.UseShortcutCommand.ExecuteAsync(null);

        Assert.Equal("B", Assert.Single(r.EditorOpened).Current.Combo.Key);
        var (_, store) = Assert.Single(r.Applied);
        Assert.Equal(AuxKeyBinding.MultiKeyBindingPath, store!.Path);
        Assert.Equal("Ctrl + Z", row.Summary);
        Assert.True(row.IsShortcut);
    }

    [Fact]
    public async Task UseShortcut_ModifierOnly_IsWrittenAsHeldModifiers()
    {
        var r = new Recorder { EditorResult = Shortcut(false, false, true, AuxKeyBinding.None) };
        var row = Row(r, null);

        await row.UseShortcutCommand.ExecuteAsync(null);

        var (_, store) = Assert.Single(r.Applied);
        Assert.Equal(new AuxCombo(false, false, true, AuxKeyBinding.None), AuxKeyBinding.ReadCombo(store));
        Assert.Equal("Alt", row.Summary);
    }

    [Fact]
    public async Task UseShortcut_Cancelled_ChangesNothing_AndSaysSoToTheRadios()
    {
        var r = new Recorder { EditorResult = null };
        var row = Row(r, PenButtonBinding.MakeDefault(1));
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await row.UseShortcutCommand.ExecuteAsync(null);

        Assert.Empty(r.Applied);
        Assert.True(row.IsDefaultAction);       // still what is saved
        // The radio the user clicked has already drawn itself checked, so the row must say again which one is.
        Assert.Contains(nameof(PenButtonRowViewModel.IsShortcut), raised);
        Assert.Contains(nameof(PenButtonRowViewModel.IsDefaultAction), raised);
    }

    [Fact]
    public async Task UseShortcut_ClearedInTheEditor_DisablesTheButton()
    {
        var r = new Recorder { EditorResult = AuxBinding.Unbound };
        var row = Row(r, AuxKeyBinding.MakeKeyBinding("B"));

        await row.UseShortcutCommand.ExecuteAsync(null);

        var (_, store) = Assert.Single(r.Applied);
        Assert.Null(store);
        Assert.True(row.IsDisabled);
    }

    [Theory]
    [InlineData(Ink, false)]
    [InlineData(MouseOutput, true)]
    public async Task UseShortcut_TellsTheEditorWhetherMouseAndScrollAreAllowed(bool ink, bool allowed)
    {
        var r = new Recorder { EditorResult = null };
        var row = Row(r, null, ink);

        await row.UseShortcutCommand.ExecuteAsync(null);

        Assert.Equal(allowed, Assert.Single(r.EditorOpened).Options.AllowMouseAndScroll);
    }

    [Fact]
    public async Task UseShortcut_OnARightClickUnderInk_OpensTheEditorOnTheClick_SoItIsStillSeenAndClearable()
    {
        var r = new Recorder { EditorResult = null };
        var row = Row(r, AuxKeyBinding.MakeMouseBinding("Right"));

        await row.UseShortcutCommand.ExecuteAsync(null);

        Assert.Equal(AuxKind.Mouse, Assert.Single(r.EditorOpened).Current.Kind);
        Assert.Empty(r.Applied);
    }

    // ── Read-only ──────────────────────────────────────────────────

    [Fact]
    public void WhenTheHostCannotEdit_NothingCanBeChosen()
    {
        var row = Row(new Recorder(), AuxKeyBinding.MakeMouseBinding("Right"), canEdit: false);

        Assert.False(row.UseDefaultActionCommand.CanExecute(null));
        Assert.False(row.UseShortcutCommand.CanExecute(null));
        Assert.False(row.DisableCommand.CanExecute(null));
        Assert.False(row.SwitchToBarrelCommand.CanExecute(null));
        Assert.True(row.HasWarning);            // it can still tell you what is wrong
    }

    // ── Shared barrel hint ─────────────────────────────────────────

    private static List<PenButtonRowViewModel> Buttons(bool ink, params bool[] isDefault)
    {
        var rows = new List<PenButtonRowViewModel>();
        for (int i = 0; i < isDefault.Length; i++)
            rows.Add(Row(new Recorder(), isDefault[i] ? PenButtonBinding.MakeDefault(i + 1) : null, ink, i + 1));
        TabletDetailViewModel.NoteSharedBarrel(rows, ink);
        return rows;
    }

    [Fact]
    public void TwoBarrelSwitches_UnderWindowsInk_EachSayTheyDoTheSameThing()
    {
        var rows = Buttons(Ink, true, true);

        Assert.Contains("button 2", rows[0].SharedBarrelHint);
        Assert.Contains("button 1", rows[1].SharedBarrelHint);
        Assert.Contains("keyboard shortcut", rows[0].SharedBarrelHint);
        Assert.True(rows[0].HasSharedBarrelHint);
    }

    [Fact]
    public void ThreeBarrelSwitches_NameTheOtherTwo()
    {
        var rows = Buttons(Ink, true, true, true);

        Assert.Contains("buttons 2, 3", rows[0].SharedBarrelHint);
        Assert.Contains("buttons 1, 3", rows[1].SharedBarrelHint);
    }

    [Fact]
    public void OneBarrelSwitch_OrAShortcutOnTheOther_NeedsNoHint()
    {
        Assert.All(Buttons(Ink, true, false), r => Assert.Null(r.SharedBarrelHint));
        Assert.All(Buttons(Ink, false, false), r => Assert.Null(r.SharedBarrelHint));
    }

    [Fact]
    public void UnderMouseOutput_TheButtonsDoDifferentThings_SoNoHint()
    {
        // Button 1 is a right click and button 2 a middle click there; they are not the same action.
        Assert.All(Buttons(MouseOutput, true, true), r => Assert.Null(r.SharedBarrelHint));
    }

    [Fact]
    public void TheHint_GoesWhenItStopsBeingTrue()
    {
        var rows = Buttons(Ink, true, true);
        Assert.NotNull(rows[0].SharedBarrelHint);

        var after = new List<PenButtonRowViewModel>
        {
            rows[0],
            Row(new Recorder(), AuxKeyBinding.MakeKeyBinding("B"), Ink, 2),
        };
        TabletDetailViewModel.NoteSharedBarrel(after, Ink);

        Assert.Null(after[0].SharedBarrelHint);
        Assert.False(after[0].HasSharedBarrelHint);
    }
}
