using OpenTabletDriver.Desktop.Reflection;
using OtdArtist.Domain;
using Xunit;

namespace OtdArtist.Tests;

public class PenSwitchBindingTests
{
    [Fact]
    public void DetectMode_AdaptiveTip_IsAuto()
    {
        var store = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        Assert.Equal(PenSwitchBindingMode.Auto, PenSwitchBinding.DetectMode(store, PenSwitchKind.Tip));
        Assert.Equal("Adaptive Binding (Tip)", PenSwitchBinding.GetDisplayLabel(store, PenSwitchKind.Tip));
    }

    [Fact]
    public void DetectMode_WindowsInkPenTip_IsLegacy()
    {
        var store = PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.Tip);
        Assert.Equal(PenSwitchBindingMode.Legacy, PenSwitchBinding.DetectMode(store, PenSwitchKind.Tip));
        Assert.Equal("Windows Ink, Pen Tip", PenSwitchBinding.GetDisplayLabel(store, PenSwitchKind.Tip));
    }

    [Fact]
    public void DetectMode_MouseBinding_IsOther()
    {
        var store = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        store.Path = PenSwitchBinding.MouseBindingPath;
        store.Settings.Clear();
        store.Settings.Add(new PluginSetting("Button", "Left"));

        Assert.Equal(PenSwitchBindingMode.Other, PenSwitchBinding.DetectMode(store, PenSwitchKind.Tip));
        Assert.Equal("Mouse Button Binding, Left", PenSwitchBinding.GetDisplayLabel(store, PenSwitchKind.Tip));
    }

    [Fact]
    public void MakeAdaptiveBinding_PenButton_UsesIndex()
    {
        var store = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, 2);
        Assert.Equal(PenSwitchBindingMode.Auto, PenSwitchBinding.DetectMode(store, PenSwitchKind.PenButton, 2));
        Assert.Equal("Adaptive Binding (Button 2)", PenSwitchBinding.GetDisplayLabel(store, PenSwitchKind.PenButton, 2));
    }

    [Fact]
    public void MakeLegacyBinding_Eraser_AcceptsHoldAndToggle()
    {
        var hold = PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.Eraser);
        Assert.Equal(PenSwitchBindingMode.Legacy, PenSwitchBinding.DetectMode(hold, PenSwitchKind.Eraser));

        var toggle = PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.Eraser);
        toggle.Settings.First(s => s.Property == "Button").SetValue("Eraser (Toggle)");
        Assert.Equal(PenSwitchBindingMode.Legacy, PenSwitchBinding.DetectMode(toggle, PenSwitchKind.Eraser));
        Assert.Equal("Windows Ink, Eraser", PenSwitchBinding.GetDisplayLabel(toggle, PenSwitchKind.Eraser));
    }
}
