using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

// #1015: an ExpressKey or wheel row says when its mouse click or scroll does not suit Windows Ink, from what
// is saved, and the warning follows the binding as it is edited or cleared. It never changes the binding.
public class ButtonBindingWarningTests
{
    private static AuxBinding Keyboard(string key) =>
        new(AuxKind.Keyboard, new AuxCombo(false, false, false, key), AuxKeyBinding.None, AuxKeyBinding.None);

    private static AuxBinding Mouse(string button) =>
        new(AuxKind.Mouse, AuxCombo.Unbound, button, AuxKeyBinding.None);

    private static AuxBinding Scroll(string direction) =>
        new(AuxKind.Scroll, AuxCombo.Unbound, AuxKeyBinding.None, direction);

    private static ButtonBinding Row(AuxBinding binding, bool ink,
        Func<AuxBinding, string, Task<AuxBinding?>>? edit = null, Func<int, AuxBinding, Task>? apply = null) =>
        new(1, binding, isOtherBinding: false, otherLabel: "", canEdit: true,
            applyBinding: apply ?? ((_, _) => Task.CompletedTask), editBinding: edit, outputIsWindowsInk: ink);

    [Fact]
    public void MouseClick_UnderWindowsInk_Warns()
    {
        var row = Row(Mouse("Left"), ink: true);

        Assert.True(row.HasWarning);
        Assert.Equal(BindingSafety.Message(BindingHazard.MouseClickUnderWindowsInk), row.Warning);
    }

    [Fact]
    public void Scroll_UnderWindowsInk_Warns()
    {
        Assert.Equal(BindingSafety.Message(BindingHazard.ScrollUnderWindowsInk), Row(Scroll("Up"), ink: true).Warning);
    }

    [Fact]
    public void KeyboardUnbound_AndAnythingWithoutWindowsInk_DoNotWarn()
    {
        Assert.False(Row(Keyboard("B"), ink: true).HasWarning);
        Assert.False(Row(AuxBinding.Unbound, ink: true).HasWarning);
        Assert.False(Row(Mouse("Left"), ink: false).HasWarning);
        Assert.False(Row(Scroll("Up"), ink: false).HasWarning);
    }

    [Fact]
    public async Task ReplacingTheBinding_ClearsTheWarning_AndRaisesIt()
    {
        var row = Row(Mouse("Left"), ink: true, edit: (_, _) => Task.FromResult<AuxBinding?>(Keyboard("B")));
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await row.EditCommand.ExecuteAsync(null);

        Assert.False(row.HasWarning);
        Assert.Contains(nameof(ButtonBinding.Warning), raised);
        Assert.Contains(nameof(ButtonBinding.HasWarning), raised);
    }

    [Fact]
    public async Task Clear_RemovesTheWarning()
    {
        var row = Row(Mouse("Left"), ink: true);

        await row.ClearCommand.ExecuteAsync(null);

        Assert.False(row.HasWarning);
    }

    [Fact]
    public async Task Warning_ChangesNothingByItself()
    {
        var applied = 0;
        var row = Row(Mouse("Left"), ink: true, apply: (_, _) => { applied++; return Task.CompletedTask; });

        _ = row.Warning;
        _ = row.Summary;

        Assert.Equal(0, applied);
        Assert.Equal("Left click", row.Summary);   // still describes what is really saved
        await Task.CompletedTask;
    }

    [Fact]
    public void OtherBinding_TakesTheCallersHazard()
    {
        // A binding the editor cannot model (here the Windows Ink plugin's own) is judged by the caller.
        var row = new ButtonBinding(1, AuxBinding.Unbound, isOtherBinding: true, otherLabel: "Windows Ink",
            canEdit: true, applyBinding: (_, _) => Task.CompletedTask,
            outputIsWindowsInk: false, otherHazard: BindingHazard.WindowsInkBindingWithoutWindowsInk);

        Assert.Equal(BindingSafety.Message(BindingHazard.WindowsInkBindingWithoutWindowsInk), row.Warning);
        Assert.Equal("Windows Ink", row.Summary);
    }
}
