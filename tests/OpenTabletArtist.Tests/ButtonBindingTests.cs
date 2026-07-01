using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ButtonBindingTests
{
    private static AuxBinding Keyboard(string key) =>
        new(AuxKind.Keyboard, new AuxCombo(false, false, false, key), AuxKeyBinding.None, AuxKeyBinding.None);

    // Regression: the ExpressKeys type dropdown transiently pushes an empty SelectedKind while the
    // cards rebuild. For a *bound* card that made Current() read as None ≠ its binding, so it persisted
    // a clear then re-persisted the binding — an infinite save→reload→rebuild loop that hung the app.
    [Fact]
    public void EmptyKindTransient_DoesNotReapply()
    {
        int applies = 0;
        var vm = new ButtonBinding(1, Keyboard("E"), isOtherBinding: false, otherLabel: "", canEdit: true,
            applyBinding: (_, _) => { applies++; return Task.CompletedTask; });

        vm.SelectedKind = "";          // the picker's transient empty (the bug trigger)
        vm.SelectedKind = "Keyboard";  // resolves back to the real type

        Assert.Equal(0, applies);      // no clear/re-bind oscillation
    }

    // The guard must not break normal editing: changing the key still applies exactly once.
    [Fact]
    public void ChangingKey_AppliesOnce()
    {
        int applies = 0;
        AuxBinding? last = null;
        var vm = new ButtonBinding(1, Keyboard("E"), false, "", true,
            (_, b) => { applies++; last = b; return Task.CompletedTask; });

        vm.SelectedKey = "F";

        Assert.Equal(1, applies);
        Assert.Equal("F", last!.Combo.Key);
    }

    // Selecting the real "None" type (not an empty transient) still clears a bound button.
    [Fact]
    public void SelectingNoneType_ClearsBinding()
    {
        int applies = 0;
        AuxBinding? last = null;
        var vm = new ButtonBinding(1, Keyboard("E"), false, "", true,
            (_, b) => { applies++; last = b; return Task.CompletedTask; });

        vm.SelectedKind = "None";

        Assert.Equal(1, applies);
        Assert.False(last!.IsBound);
    }
}
