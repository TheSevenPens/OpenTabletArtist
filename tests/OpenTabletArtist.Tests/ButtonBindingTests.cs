using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ButtonBindingTests
{
    private static AuxBinding Keyboard(string key) =>
        new(AuxKind.Keyboard, new AuxCombo(false, false, false, key), AuxKeyBinding.None, AuxKeyBinding.None);

    private static AuxBinding Mouse(string button) =>
        new(AuxKind.Mouse, AuxCombo.Unbound, button, AuxKeyBinding.None);

    // Edit now goes through a modal editor (an injected callback returning the chosen binding). Saving
    // a different binding applies it exactly once.
    [Fact]
    public async Task Edit_AppliesChosenBinding_Once()
    {
        int applies = 0;
        AuxBinding? last = null;
        var vm = new ButtonBinding(1, Keyboard("E"), isOtherBinding: false, otherLabel: "", canEdit: true,
            applyBinding: (_, b) => { applies++; last = b; return Task.CompletedTask; },
            editBinding: (_, _) => Task.FromResult<AuxBinding?>(Keyboard("F")));

        await vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(1, applies);
        Assert.Equal("F", last!.Combo.Key);
        Assert.Equal("F", vm.Summary);
    }

    // Cancelling the dialog (null result) applies nothing and leaves the summary intact.
    [Fact]
    public async Task Edit_Cancel_AppliesNothing()
    {
        int applies = 0;
        var vm = new ButtonBinding(1, Keyboard("E"), false, "", true,
            (_, _) => { applies++; return Task.CompletedTask; },
            editBinding: (_, _) => Task.FromResult<AuxBinding?>(null));

        await vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(0, applies);
        Assert.Equal("E", vm.Summary);
    }

    // Re-saving the same binding is a no-op (no redundant apply).
    [Fact]
    public async Task Edit_SameBinding_DoesNotReapply()
    {
        int applies = 0;
        var vm = new ButtonBinding(1, Keyboard("E"), false, "", true,
            (_, _) => { applies++; return Task.CompletedTask; },
            editBinding: (current, _) => Task.FromResult<AuxBinding?>(current));

        await vm.EditCommand.ExecuteAsync(null);

        Assert.Equal(0, applies);
    }

    // Clear (Unbound result) clears a bound button.
    [Fact]
    public async Task Edit_Clear_UnbindsButton()
    {
        AuxBinding? last = null;
        var vm = new ButtonBinding(1, Keyboard("E"), false, "", true,
            (_, b) => { last = b; return Task.CompletedTask; },
            editBinding: (_, _) => Task.FromResult<AuxBinding?>(AuxBinding.Unbound));

        await vm.EditCommand.ExecuteAsync(null);

        Assert.False(last!.IsBound);
        Assert.Equal("Do nothing", vm.Summary);
    }

    // Summary renders each binding type for the read-only card.
    [Theory]
    [InlineData("E", "E")]
    [InlineData("D0", "0")]        // digits store as "D0" but read as "0"
    public void Summary_Keyboard(string key, string expected)
    {
        var vm = new ButtonBinding(1, Keyboard(key), false, "", true, applyBinding: null);
        Assert.Equal(expected, vm.Summary);
    }

    [Fact]
    public void Summary_KeyboardWithModifiers()
    {
        var binding = new AuxBinding(AuxKind.Keyboard, new AuxCombo(true, false, true, "Z"),
            AuxKeyBinding.None, AuxKeyBinding.None);
        var vm = new ButtonBinding(1, binding, false, "", true, applyBinding: null);
        Assert.Equal("Ctrl + Alt + Z", vm.Summary);
    }

    [Fact]
    public void Summary_Mouse() =>
        Assert.Equal("Left click",
            new ButtonBinding(1, Mouse("Left"), false, "", true, applyBinding: null).Summary);

    [Fact]
    public void Summary_Unbound() =>
        Assert.Equal("Do nothing",
            new ButtonBinding(1, AuxBinding.Unbound, false, "", true, applyBinding: null).Summary);

    // A binding the editor can't model shows its friendly "other" name until replaced.
    [Fact]
    public void Summary_OtherBinding_ShowsFriendlyName() =>
        Assert.Equal("Windows Ink",
            new ButtonBinding(1, AuxBinding.Unbound, isOtherBinding: true, otherLabel: "Windows Ink",
                canEdit: true, applyBinding: null).Summary);
}
