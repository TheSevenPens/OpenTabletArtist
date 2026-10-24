using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

// #1015: the three choices on a pen barrel button, read from and written to bindings OTD already ships.
public class PenButtonBindingTests
{
    private const bool Ink = true;
    private const bool MouseOutput = false;

    private static PluginSettingStore Combo(bool ctrl, bool shift, bool alt, string key) =>
        AuxKeyBinding.MakeBinding(new AuxCombo(ctrl, shift, alt, key))!;

    [Fact]
    public void NoBinding_IsDisabled()
    {
        // A null entry is OTD's own "nothing bound", and a never-configured button reads the same way.
        Assert.Equal(PenButtonAction.Disabled, PenButtonBinding.Classify(null, 1, Ink));
        Assert.Null(PenButtonBinding.MakeDisabled());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AdaptiveBindingForThisButton_IsDefaultAction(int button)
    {
        var store = PenButtonBinding.MakeDefault(button);

        Assert.Equal(PenButtonAction.Default, PenButtonBinding.Classify(store, button, Ink));
        Assert.Equal(PenButtonAction.Default, PenButtonBinding.Classify(store, button, MouseOutput));
    }

    [Fact]
    public void AdaptiveBindingForADifferentButton_IsNotThisButtonsDefaultAction()
    {
        // Button 2's adaptive binding sitting on button 1 does something else; the card must not call it "Default".
        Assert.Equal(PenButtonAction.Other, PenButtonBinding.Classify(PenButtonBinding.MakeDefault(2), 1, Ink));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void DefaultAction_IsOnlyOfferedForAdaptiveBindings_Buttons1To3(int button, bool offered)
    {
        Assert.Equal(offered, PenButtonBinding.CanUseDefaultAction(button));
    }

    [Fact]
    public void KeyboardBindings_AreShortcuts_IncludingModifierOnly()
    {
        Assert.Equal(PenButtonAction.Shortcut, PenButtonBinding.Classify(AuxKeyBinding.MakeKeyBinding("B"), 1, Ink));
        Assert.Equal(PenButtonAction.Shortcut, PenButtonBinding.Classify(Combo(true, false, false, "Z"), 1, Ink));
        Assert.Equal(PenButtonAction.Shortcut, PenButtonBinding.Classify(Combo(false, false, true, AuxKeyBinding.None), 1, Ink));
    }

    [Fact]
    public void MouseAndScroll_AreOther_UnderWindowsInk_AndTheShortcutChoice_UnderMouseOutput()
    {
        var click = AuxKeyBinding.MakeMouseBinding("Right");
        var scroll = AuxKeyBinding.MakeScrollBinding("Up");

        Assert.Equal(PenButtonAction.Other, PenButtonBinding.Classify(click, 1, Ink));
        Assert.Equal(PenButtonAction.Other, PenButtonBinding.Classify(scroll, 1, Ink));
        Assert.Equal(PenButtonAction.Shortcut, PenButtonBinding.Classify(click, 1, MouseOutput));
        Assert.Equal(PenButtonAction.Shortcut, PenButtonBinding.Classify(scroll, 1, MouseOutput));
    }

    [Fact]
    public void BindingsTheCardDoesNotModel_AreOther()
    {
        var windowsInk = PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.PenButton);
        var macro = JsonConvert.DeserializeObject<PluginSettingStore>(
            $$"""{"Path":"{{AuxKeyBinding.MultiKeyBindingPath}}","Enable":true,"Settings":[{"Property":"Keys","Value":"A+B"}]}""")!;

        Assert.Equal(PenButtonAction.Other, PenButtonBinding.Classify(windowsInk, 1, Ink));
        Assert.Equal(PenButtonAction.Other, PenButtonBinding.Classify(macro, 1, Ink));
    }

    [Fact]
    public void ReadShortcut_GivesTheEditorWhatIsSaved_OrNothingForAnUnmodelledBinding()
    {
        var saved = PenButtonBinding.ReadShortcut(Combo(true, false, false, "Z"));
        Assert.Equal(AuxKind.Keyboard, saved.Kind);
        Assert.Equal("Z", saved.Combo.Key);

        Assert.Equal(AuxBinding.Unbound, PenButtonBinding.ReadShortcut(null));
        Assert.Equal(AuxBinding.Unbound, PenButtonBinding.ReadShortcut(PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.PenButton)));
    }

    [Fact]
    public void Describe_UsesThePlainWordsAnArtistWouldUse()
    {
        Assert.Equal("Barrel Switch", PenButtonBinding.Describe(PenButtonBinding.MakeDefault(1), 1, Ink, "x"));
        Assert.Equal("Right click", PenButtonBinding.Describe(PenButtonBinding.MakeDefault(1), 1, MouseOutput, "x"));
        Assert.Equal("Middle click", PenButtonBinding.Describe(PenButtonBinding.MakeDefault(2), 2, MouseOutput, "x"));
        Assert.Equal("No action", PenButtonBinding.Describe(PenButtonBinding.MakeDefault(3), 3, MouseOutput, "x"));
        Assert.Equal("Ctrl + Z", PenButtonBinding.Describe(Combo(true, false, false, "Z"), 1, Ink, "x"));
        Assert.Equal("Alt", PenButtonBinding.Describe(Combo(false, false, true, AuxKeyBinding.None), 1, Ink, "x"));
        Assert.Equal("Disabled", PenButtonBinding.Describe(null, 1, Ink, "x"));
        Assert.Equal("Right click", PenButtonBinding.Describe(AuxKeyBinding.MakeMouseBinding("Right"), 1, Ink, "x"));
        Assert.Equal("Some plugin", PenButtonBinding.Describe(
            PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.PenButton), 1, Ink, "Some plugin"));
    }
}
