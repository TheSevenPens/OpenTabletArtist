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
}
