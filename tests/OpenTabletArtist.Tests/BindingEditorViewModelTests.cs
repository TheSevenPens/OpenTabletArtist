using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class BindingEditorViewModelTests
{
    [Fact]
    public void Init_Keyboard_OpensKeyboardTabPrefilled()
    {
        var binding = new AuxBinding(AuxKind.Keyboard, new AuxCombo(true, false, false, "Z"),
            AuxKeyBinding.None, AuxKeyBinding.None);
        var vm = new BindingEditorViewModel(binding, "ExpressKey 1");

        Assert.Equal(BindingEditorViewModel.KeyboardTab, vm.SelectedTabIndex);
        Assert.True(vm.Ctrl);
        Assert.Equal("Z", vm.SelectedKeyOption?.Value);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void Init_Mouse_OpensMouseTabPrefilled()
    {
        var binding = new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, "Right", AuxKeyBinding.None);
        var vm = new BindingEditorViewModel(binding, "Wheel CW");

        Assert.Equal(BindingEditorViewModel.MouseTab, vm.SelectedTabIndex);
        Assert.Equal("Right", vm.SelectedMouseButton);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void Save_Mouse_ProducesMouseBinding()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x")
        { SelectedTabIndex = BindingEditorViewModel.MouseTab, SelectedMouseButton = "Left" };

        vm.SaveCommand.Execute(null);

        Assert.Equal(AuxKind.Mouse, vm.Result!.Kind);
        Assert.Equal("Left", vm.Result.MouseButton);
    }

    [Fact]
    public void Init_Unbound_OpensKeyboardTab_CannotSaveYet()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "ExpressKey 1");
        Assert.Equal(BindingEditorViewModel.KeyboardTab, vm.SelectedTabIndex);
        Assert.False(vm.CanSave);           // nothing picked yet
        Assert.Null(vm.Result);
    }

    [Fact]
    public void Save_Keyboard_ProducesBinding()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x") { Shift = true };
        vm.SelectedTabIndex = BindingEditorViewModel.KeyboardTab;
        vm.SelectedKeyOption = vm.KeyOptions.First(o => o.Value == "A");

        Assert.True(vm.CanSave);
        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal(AuxKind.Keyboard, vm.Result!.Kind);
        Assert.True(vm.Result.Combo.Shift);
        Assert.Equal("A", vm.Result.Combo.Key);
    }

    [Fact]
    public void Save_Scroll_ProducesScrollBinding()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x")
        { SelectedTabIndex = BindingEditorViewModel.ScrollTab, SelectedScroll = "Up" };

        vm.SaveCommand.Execute(null);

        Assert.Equal(AuxKind.Scroll, vm.Result!.Kind);
        Assert.Equal("Up", vm.Result.Scroll);
    }

    [Fact]
    public void Clear_ProducesUnbound()
    {
        var vm = new BindingEditorViewModel(
            new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, "Left", AuxKeyBinding.None), "x");

        vm.ClearCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.False(vm.Result!.IsBound);
    }

    [Fact]
    public void Cancel_LeavesNullResult()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x");
        vm.CancelDialogCommand.Execute(null);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void CannotSave_WhenActiveTabIncomplete()
    {
        // Mouse tab selected but no button chosen.
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x")
        { SelectedTabIndex = BindingEditorViewModel.MouseTab };
        Assert.False(vm.CanSave);
    }

    // Clicking the on-screen key twice toggles it off.
    [Fact]
    public void PickKey_TogglesOff_WhenClickedTwice()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x");
        var e = vm.MainRows.SelectMany(r => r).First(c => c.Value == "E");

        e.PickCommand.Execute(e);
        Assert.Equal("E", vm.SelectedKeyOption?.Value);
        e.PickCommand.Execute(e);
        Assert.Null(vm.SelectedKeyOption);
    }

    [Fact]
    public void Combo_Empty_WhenNothingSet()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x");
        Assert.True(vm.ComboEmpty);
        Assert.Empty(vm.ComboParts);
    }

    [Fact]
    public void Combo_SingleKey_NoModifiers()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x");
        vm.SelectedKeyOption = vm.KeyOptions.First(o => o.Value == "A");
        var chips = vm.ComboParts.Where(p => p.IsChip).Select(p => p.Text).ToList();
        Assert.Equal(new[] { "A" }, chips);
    }

    [Fact]
    public void Combo_Modifiers_ThenKey()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x") { Ctrl = true, Alt = true };
        vm.SelectedKeyOption = vm.KeyOptions.First(o => o.Value == "E");
        Assert.False(vm.ComboEmpty);
        var chips = vm.ComboParts.Where(p => p.IsChip).Select(p => p.Text).ToList();
        Assert.Equal(new[] { "CTRL", "ALT", "E" }, chips);
    }

    // A media key is stored as a keyboard binding but must open on the MEDIA tab, not KEYBOARD.
    [Fact]
    public void Init_MediaKey_OpensMediaTabPrefilled()
    {
        var binding = new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, "Mute"),
            AuxKeyBinding.None, AuxKeyBinding.None);
        var vm = new BindingEditorViewModel(binding, "ExpressKey 1");

        Assert.Equal(BindingEditorViewModel.MediaTab, vm.SelectedTabIndex);
        Assert.Equal("Mute", vm.SelectedMediaKey);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void Save_Media_ProducesKeyboardBinding_NoModifiers()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x")
        { SelectedTabIndex = BindingEditorViewModel.MediaTab, SelectedMediaKey = "VolumeUp" };

        vm.SaveCommand.Execute(null);

        Assert.Equal(AuxKind.Keyboard, vm.Result!.Kind);
        Assert.Equal("VolumeUp", vm.Result.Combo.Key);
        Assert.False(vm.Result.Combo.Ctrl || vm.Result.Combo.Shift || vm.Result.Combo.Alt);
    }

    [Fact]
    public void Numpad_IncludesKeypadEqual_LabelledEquals()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "x");
        Assert.Contains(vm.NumpadRows.SelectMany(r => r), c => c.Value == "KeypadEqual" && c.Display == "=");
    }
}
