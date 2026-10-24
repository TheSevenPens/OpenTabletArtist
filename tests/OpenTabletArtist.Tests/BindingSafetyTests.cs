using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

// #1015: a saved binding is judged against the output mode from what is saved now, so it does not matter
// whether OTA or OTD's own UI wrote it. Nothing here ever changes a binding.
public class BindingSafetyTests
{
    private static PluginSettingStore Key(string key) => AuxKeyBinding.MakeKeyBinding(key)!;

    [Theory]
    [InlineData("Left")]
    [InlineData("Right")]
    [InlineData("Middle")]
    [InlineData("Backward")]
    [InlineData("Forward")]
    public void MouseButton_UnderWindowsInk_IsAHazard(string button)
    {
        var store = AuxKeyBinding.MakeMouseBinding(button);

        Assert.Equal(BindingHazard.MouseClickUnderWindowsInk, BindingSafety.Assess(store, outputIsWindowsInk: true));
    }

    [Theory]
    [InlineData("Up")]
    [InlineData("Down")]
    [InlineData("Left")]
    [InlineData("Right")]
    public void Scroll_UnderWindowsInk_IsAHazard(string direction)
    {
        var store = AuxKeyBinding.MakeScrollBinding(direction);

        Assert.Equal(BindingHazard.ScrollUnderWindowsInk, BindingSafety.Assess(store, outputIsWindowsInk: true));
    }

    [Fact]
    public void MouseAndScroll_WithoutWindowsInk_AreFine()
    {
        Assert.Equal(BindingHazard.None, BindingSafety.Assess(AuxKeyBinding.MakeMouseBinding("Left"), false));
        Assert.Equal(BindingHazard.None, BindingSafety.Assess(AuxKeyBinding.MakeScrollBinding("Up"), false));
    }

    [Fact]
    public void WindowsInkBinding_WithoutWindowsInk_IsAHazard_AndFineWithIt()
    {
        var store = PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.PenButton);

        Assert.Equal(BindingHazard.WindowsInkBindingWithoutWindowsInk, BindingSafety.Assess(store, outputIsWindowsInk: false));
        Assert.Equal(BindingHazard.None, BindingSafety.Assess(store, outputIsWindowsInk: true));
    }

    [Fact]
    public void KeyboardAdaptiveAndUnbound_AreNeverAHazard()
    {
        foreach (var ink in new[] { true, false })
        {
            Assert.Equal(BindingHazard.None, BindingSafety.Assess(Key("A"), ink));
            Assert.Equal(BindingHazard.None, BindingSafety.Assess(AuxKeyBinding.MakeBinding(new AuxCombo(true, false, false, "Z")), ink));
            Assert.Equal(BindingHazard.None, BindingSafety.Assess(PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, 1), ink));
            Assert.Equal(BindingHazard.None, BindingSafety.Assess(null, ink));
        }
    }

    [Theory]
    [InlineData("VoiDPlugins.OutputMode.WinInkAbsoluteMode", true)]
    [InlineData("VoiDPlugins.OutputMode.WinInkRelativeMode", true)]
    [InlineData("OpenTabletDriver.Desktop.Output.AbsoluteMode", false)]
    [InlineData("OpenTabletDriver.Desktop.Output.RelativeMode", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWindowsInkOutput_ReadsTheModePath(string? path, bool expected)
    {
        Assert.Equal(expected, BindingSafety.IsWindowsInkOutput(path));
    }

    [Fact]
    public void Message_ExistsForEveryHazard_AndSaysNothingForNone()
    {
        Assert.Null(BindingSafety.Message(BindingHazard.None));
        foreach (var hazard in new[]
                 {
                     BindingHazard.MouseClickUnderWindowsInk, BindingHazard.ScrollUnderWindowsInk,
                     BindingHazard.WindowsInkBindingWithoutWindowsInk,
                 })
        {
            var message = BindingSafety.Message(hazard);
            Assert.False(string.IsNullOrWhiteSpace(message));
            // Plain language (#495): no plugin or handler names.
            Assert.DoesNotContain("WindowsInkButtonHandler", message);
            Assert.DoesNotContain("VMulti", message);
            Assert.DoesNotContain("Adaptive", message);
        }
    }

    [Fact]
    public void IsRightClick_OnlyForARightMouseBinding()
    {
        Assert.True(BindingSafety.IsRightClick(AuxKeyBinding.MakeMouseBinding("Right")));
        Assert.False(BindingSafety.IsRightClick(AuxKeyBinding.MakeMouseBinding("Left")));
        Assert.False(BindingSafety.IsRightClick(AuxKeyBinding.MakeMouseBinding("Middle")));
        Assert.False(BindingSafety.IsRightClick(Key("A")));
        Assert.False(BindingSafety.IsRightClick(null));
    }
}
