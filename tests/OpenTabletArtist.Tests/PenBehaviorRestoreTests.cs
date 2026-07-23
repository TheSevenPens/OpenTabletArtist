using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PenBehaviorRestoreTests
{
    [Fact]
    public void ToRecommended_ReenablesEverything_OnWindows()
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(object), true) { Path = "OpenTabletDriver.Desktop.Output.AbsoluteMode" },
        };
        profile.BindingSettings.DisablePressure = true;
        profile.BindingSettings.DisableTilt = true;
        profile.BindingSettings.TipButton = null; // pen tip disabled

        var changed = PenBehaviorRestore.ToRecommended(profile, isWindows: true);

        Assert.True(changed);
        Assert.Contains("WinInk", profile.OutputMode!.Path);
        Assert.False(profile.BindingSettings.DisablePressure);
        Assert.False(profile.BindingSettings.DisableTilt);
        Assert.NotNull(profile.BindingSettings.TipButton?.Path); // tip restored to a binding
    }

    [Fact]
    public void ToRecommended_LeavesOutputModeAlone_OffWindows()
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(object), true) { Path = "OpenTabletDriver.Desktop.Output.AbsoluteMode" },
        };
        profile.BindingSettings.DisableTilt = true;

        var changed = PenBehaviorRestore.ToRecommended(profile, isWindows: false);

        Assert.True(changed);
        Assert.DoesNotContain("WinInk", profile.OutputMode!.Path); // native mode kept off-Windows
        Assert.False(profile.BindingSettings.DisableTilt);
    }

    [Fact]
    public void ToRecommended_NoChange_WhenAlreadyRecommended()
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(object), true) { Path = PenBehaviorRestore.WinInkAbsoluteModePath },
        };
        profile.BindingSettings.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);

        Assert.False(PenBehaviorRestore.ToRecommended(profile, isWindows: true));
    }
}
