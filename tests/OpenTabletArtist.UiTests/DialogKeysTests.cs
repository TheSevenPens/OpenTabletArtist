using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.UiTests;

// The code-built dialogs answer the keyboard: Enter confirms where confirming is harmless, Escape backs out.
public class DialogKeysTests
{
    private static Window Owned(Window owner) => owner.OwnedWindows.Single();

    private static void Press(Window w, Key key, PhysicalKey physical)
    {
        w.KeyPress(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TheInputDialog_OpensWithTheTextSelected_AndEnterIsOk()
    {
        var owner = HarnessTests.Show(new TextBlock());
        var answer = Dialogs.ShowInputAsync("Rename Preset", "Enter a new name for this preset:", "Preset", owner);
        Dispatcher.UIThread.RunJobs();
        var dialog = Owned(owner);
        var box = dialog.GetVisualDescendants().OfType<TextBox>().Single();

        Assert.True(box.IsFocused);
        Assert.Equal("Preset", box.SelectedText);

        dialog.KeyTextInput("Inking");                 // types over the selection
        Press(dialog, Key.Enter, PhysicalKey.Enter);

        Assert.True(answer.IsCompletedSuccessfully);
        Assert.Equal("Inking", answer.Result);
        owner.Close();
    }

    [AvaloniaFact]
    public void TheInputDialog_EscapeIsCancel()
    {
        var owner = HarnessTests.Show(new TextBlock());
        var answer = Dialogs.ShowInputAsync("Rename Preset", "Enter a new name for this preset:", "Preset", owner);
        Dispatcher.UIThread.RunJobs();

        Press(Owned(owner), Key.Escape, PhysicalKey.Escape);

        Assert.True(answer.IsCompletedSuccessfully);
        Assert.Null(answer.Result);
        owner.Close();
    }

    [AvaloniaFact]
    public void TheConfirmDialog_EscapeIsNo_AndEnterIsNotYes()
    {
        var owner = HarnessTests.Show(new TextBlock());
        var answer = Dialogs.ShowConfirmAsync("Delete Preset", "Delete the profile \"Preset\"?", owner);
        Dispatcher.UIThread.RunJobs();
        var dialog = Owned(owner);

        Press(dialog, Key.Enter, PhysicalKey.Enter);
        Assert.False(answer.IsCompleted);              // a stray Enter deletes nothing

        Press(dialog, Key.Escape, PhysicalKey.Escape);
        Assert.True(answer.IsCompletedSuccessfully);
        Assert.False(answer.Result);
        owner.Close();
    }

    [AvaloniaFact]
    public void TheMessageDialog_ClosesOnEnter()
    {
        var owner = HarnessTests.Show(new TextBlock());
        var shown = Dialogs.ShowMessageAsync("Rename", "A profile named \"Other\" already exists.", owner);
        Dispatcher.UIThread.RunJobs();

        Press(Owned(owner), Key.Enter, PhysicalKey.Enter);

        Assert.True(shown.IsCompletedSuccessfully);
        owner.Close();
    }
}
