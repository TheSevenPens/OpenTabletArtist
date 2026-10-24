using Avalonia.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

// #1015: the editor's Record button, modifier-only shortcuts, and the Mouse / Scroll tabs that go away
// under Windows Ink. Physical keys are fed straight to the view model, as the dialog does.
public class BindingEditorRecordingTests
{
    private static BindingEditorViewModel Fresh(BindingEditorOptions? options = null) =>
        new(AuxBinding.Unbound, "Button 1", options);

    private static AuxBinding Saved(BindingEditorViewModel vm)
    {
        vm.SaveCommand.Execute(null);
        return vm.Result!;
    }

    // ── Record ─────────────────────────────────────────────────────

    [Fact]
    public void Record_AMainKeyAloneEndsTheRecording()
    {
        var vm = Fresh();
        vm.StartRecording();

        Assert.True(vm.IsRecording);
        Assert.True(vm.CaptureKeyDown(Key.B));

        Assert.False(vm.IsRecording);
        var saved = Saved(vm);
        Assert.Equal(new AuxCombo(false, false, false, "B"), saved.Combo);
    }

    [Fact]
    public void Record_ModifiersHeldWithAKey_AreTheOnesThatBelongWithIt()
    {
        var vm = Fresh();
        vm.StartRecording();

        vm.CaptureKeyDown(Key.LeftCtrl);
        vm.CaptureKeyDown(Key.LeftShift);
        vm.CaptureKeyDown(Key.Z);

        Assert.False(vm.IsRecording);
        Assert.Equal(new AuxCombo(true, true, false, "Z"), Saved(vm).Combo);
    }

    [Fact]
    public void Record_LettingGoOfTheOnlyModifier_EndsTheRecordingAsThatModifier()
    {
        var vm = Fresh();
        vm.StartRecording();

        vm.CaptureKeyDown(Key.LeftCtrl);
        vm.CaptureKeyUp(Key.LeftCtrl);          // let go before pressing a key

        // That is a modifier-only recording; a key pressed afterwards is a new gesture and is ignored.
        Assert.False(vm.IsRecording);
        Assert.False(vm.CaptureKeyDown(Key.Z));
        Assert.Equal(new AuxCombo(true, false, false, AuxKeyBinding.None), Saved(vm).Combo);
    }

    [Fact]
    public void Record_OneModifierLetGoWhileAnotherIsHeld_KeepsListeningForTheKey()
    {
        var vm = Fresh();
        vm.StartRecording();

        vm.CaptureKeyDown(Key.LeftCtrl);
        vm.CaptureKeyDown(Key.LeftShift);
        vm.CaptureKeyUp(Key.LeftCtrl);          // Shift is still down, so the gesture is not over
        vm.CaptureKeyDown(Key.Z);

        // Only the modifier still held belongs with the key.
        Assert.Equal(new AuxCombo(false, true, false, "Z"), Saved(vm).Combo);
    }

    [Fact]
    public void Record_AModifierOnItsOwn_IsRecordedWhenItIsReleased()
    {
        var vm = Fresh();
        vm.StartRecording();

        vm.CaptureKeyDown(Key.LeftAlt);
        Assert.True(vm.IsRecording);            // still waiting: the artist may be about to press a key
        vm.CaptureKeyUp(Key.LeftAlt);

        Assert.False(vm.IsRecording);
        Assert.True(vm.CanSave);
        var saved = Saved(vm);
        Assert.Equal(new AuxCombo(false, false, true, AuxKeyBinding.None), saved.Combo);
        Assert.Equal(AuxKeyBinding.MultiKeyBindingPath, AuxKeyBinding.MakeBinding(saved)!.Path);
    }

    [Fact]
    public void Record_SeveralModifiersAlone_AreRecordedOnceTheLastIsReleased()
    {
        var vm = Fresh();
        vm.StartRecording();

        vm.CaptureKeyDown(Key.LeftCtrl);
        vm.CaptureKeyDown(Key.LeftShift);
        vm.CaptureKeyUp(Key.LeftCtrl);
        Assert.True(vm.IsRecording);            // Shift is still down
        vm.CaptureKeyUp(Key.LeftShift);

        Assert.False(vm.IsRecording);
        Assert.Equal(new AuxCombo(true, true, false, AuxKeyBinding.None), Saved(vm).Combo);
    }

    [Fact]
    public void Record_ReleasesAfterTheKey_DoNothing()
    {
        var vm = Fresh();
        vm.StartRecording();
        vm.CaptureKeyDown(Key.LeftCtrl);
        vm.CaptureKeyDown(Key.Z);               // ends the recording

        Assert.False(vm.CaptureKeyUp(Key.Z));
        Assert.False(vm.CaptureKeyUp(Key.LeftCtrl));
        Assert.Equal(new AuxCombo(true, false, false, "Z"), Saved(vm).Combo);
    }

    [Fact]
    public void Record_AKeyOtdCannotSend_IsExplained_AndRecordingContinues()
    {
        var vm = Fresh();
        vm.StartRecording();

        Assert.True(vm.CaptureKeyDown(Key.LWin));

        Assert.True(vm.IsRecording);
        Assert.Contains("can't be used", vm.RecordHint);
        Assert.False(vm.CanSave);
        Assert.True(vm.CaptureKeyDown(Key.Q));  // a usable key still works afterwards
        Assert.Equal("Q", Saved(vm).Combo.Key);
    }

    [Fact]
    public void Record_Cancelled_PutsTheOldShortcutBack()
    {
        var before = new AuxBinding(AuxKind.Keyboard, new AuxCombo(true, false, false, "Z"),
            AuxKeyBinding.None, AuxKeyBinding.None);
        var vm = new BindingEditorViewModel(before, "Button 1");
        vm.StartRecording();
        Assert.False(vm.CanSave);               // recording starts from a clean slate

        vm.CancelRecording();

        Assert.False(vm.IsRecording);
        Assert.True(vm.CanSave);
        Assert.Equal(new AuxCombo(true, false, false, "Z"), Saved(vm).Combo);
    }

    [Fact]
    public void Keys_AreNotCaptured_WhenNotRecording()
    {
        var vm = Fresh();

        Assert.False(vm.CaptureKeyDown(Key.A));
        Assert.False(vm.CaptureKeyUp(Key.A));
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void ToggleRecording_StartsAndStops()
    {
        var vm = Fresh();

        vm.ToggleRecordingCommand.Execute(null);
        Assert.True(vm.IsRecording);
        Assert.Equal("Stop", vm.RecordButtonText);

        vm.ToggleRecordingCommand.Execute(null);
        Assert.False(vm.IsRecording);
        Assert.Equal("Record", vm.RecordButtonText);
    }

    // ── Modifier-only, by hand ─────────────────────────────────────

    [Fact]
    public void CanSave_WithAModifierAlone_ClickedOnTheKeyboard()
    {
        var vm = Fresh();
        Assert.False(vm.CanSave);

        vm.Alt = true;                          // toggled on the on-screen keyboard, no key picked

        Assert.True(vm.CanSave);
        Assert.Equal(new AuxCombo(false, false, true, AuxKeyBinding.None), Saved(vm).Combo);
    }

    [Fact]
    public void ModifierOnly_SavedBefore_OpensWithItsModifiersOn()
    {
        var before = new AuxBinding(AuxKind.Keyboard, new AuxCombo(true, false, false, AuxKeyBinding.None),
            AuxKeyBinding.None, AuxKeyBinding.None);

        var vm = new BindingEditorViewModel(before, "Button 1");

        Assert.True(vm.Ctrl);
        Assert.Null(vm.SelectedKeyOption);
        Assert.True(vm.CanSave);
    }

    // ── Mouse and Scroll under Windows Ink ─────────────────────────

    [Fact]
    public void Options_DefaultOffersMouseAndScroll()
    {
        Assert.True(Fresh().AllowMouseAndScroll);
        Assert.True(Fresh(new BindingEditorOptions()).AllowMouseAndScroll);
    }

    [Fact]
    public void UnderWindowsInk_MouseAndScrollTabsCannotBeSaved()
    {
        var vm = Fresh(new BindingEditorOptions(AllowMouseAndScroll: false));
        Assert.False(vm.AllowMouseAndScroll);

        vm.SelectedTabIndex = BindingEditorViewModel.MouseTab;
        vm.SelectedMouseButton = "Left";
        Assert.False(vm.CanSave);

        vm.SelectedTabIndex = BindingEditorViewModel.ScrollTab;
        vm.SelectedScroll = "Up";
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void UnderWindowsInk_AnExistingMouseBindingOpensOnKeyboard_AndIsNotChangedByOpening()
    {
        var click = new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, "Right", AuxKeyBinding.None);

        var vm = new BindingEditorViewModel(click, "Button 1", new BindingEditorOptions(AllowMouseAndScroll: false));

        Assert.Equal(BindingEditorViewModel.KeyboardTab, vm.SelectedTabIndex);
        Assert.False(vm.CanSave);               // needs a keyboard choice
        Assert.Null(vm.Result);                 // opening it produced nothing
        vm.CancelDialogCommand.Execute(null);
        Assert.Null(vm.Result);                 // cancel keeps the saved binding
    }

    [Fact]
    public void UnderWindowsInk_AnExistingMouseBindingCanStillBeCleared()
    {
        var click = new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, "Right", AuxKeyBinding.None);
        var vm = new BindingEditorViewModel(click, "Button 1", new BindingEditorOptions(AllowMouseAndScroll: false));

        vm.ClearCommand.Execute(null);

        Assert.Equal(AuxBinding.Unbound, vm.Result);
    }

    [Fact]
    public void WithoutWindowsInk_AnExistingMouseBindingStillOpensOnMouse()
    {
        var click = new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, "Right", AuxKeyBinding.None);

        var vm = new BindingEditorViewModel(click, "Button 1", new BindingEditorOptions(AllowMouseAndScroll: true));

        Assert.Equal(BindingEditorViewModel.MouseTab, vm.SelectedTabIndex);
        Assert.True(vm.CanSave);
    }
}
