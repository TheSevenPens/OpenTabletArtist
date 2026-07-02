using Avalonia.Input;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class HotkeyCaptureViewModelTests
{
    [Fact]
    public void Empty_HasNoComboAndCannotSave()
    {
        var vm = new HotkeyCaptureViewModel();
        Assert.True(vm.ComboEmpty);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void KeyWithoutModifier_CannotSave()
    {
        var vm = new HotkeyCaptureViewModel();
        vm.CapturePhysical(KeyModifiers.None, Key.A); // bare key isn't registerable
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void ModifierPlusKey_CanSave_AndProducesChord()
    {
        var vm = new HotkeyCaptureViewModel { Ctrl = true, Alt = true };
        vm.CapturePhysical(KeyModifiers.Control | KeyModifiers.Alt, Key.D1);

        Assert.True(vm.CanSave);
        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal(Key.D1, vm.Result!.Key);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Alt, vm.Result.Modifiers);
        Assert.True(vm.Result.IsRegisterable);
    }

    [Fact]
    public void CapturePhysical_IgnoresBareModifierPress()
    {
        var vm = new HotkeyCaptureViewModel { Ctrl = true };
        vm.CapturePhysical(KeyModifiers.Control, Key.LeftShift); // just a modifier held; no main key
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void Cancel_YieldsNullResult()
    {
        var vm = new HotkeyCaptureViewModel { Ctrl = true };
        vm.CapturePhysical(KeyModifiers.Control, Key.S);
        vm.CancelDialogCommand.Execute(null);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void Initial_PrefillsModifiersAndKey()
    {
        var initial = new OpenTabletArtist.Services.HotkeyChord(
            KeyModifiers.Control | KeyModifiers.Shift, Key.F5);
        var vm = new HotkeyCaptureViewModel(initial);

        Assert.True(vm.Ctrl);
        Assert.True(vm.Shift);
        Assert.False(vm.Alt);
        Assert.False(vm.Win);
        Assert.NotNull(vm.SelectedKey);
        Assert.Equal(Key.F5, vm.SelectedKey!.Key);
        Assert.True(vm.CanSave);
    }
}
