using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;
using OtdInterop;

namespace OpenTabletArtist.Tests;

// The pen page's basics / pressure / buttons split: what the view model reports for it. A pen that presses
// like a mouse (Windows, plain output) takes neither pressure nor tilt, so the toggles for them are inert; and
// a tip with nothing bound is "Disabled", not "changed to a custom action".
// Constructs TabletDetailViewModel (see TabletDetailViewModelTests): shares the display-enumerator seam.
[Collection(DisplayEnumeratorCollection.Name)]
public class PenTabsViewModelTests
{
    private const string WinInkAbsolute = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string NativeAbsolute = "OpenTabletDriver.Desktop.Output.AbsoluteMode";

    private static TabletDetailViewModel Open(string outputMode, bool disablePressure = false)
    {
        var profile = new Profile { Tablet = "T", OutputMode = new PluginSettingStore(outputMode) { Path = outputMode } };
        profile.BindingSettings.DisablePressure = disablePressure;
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        return new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
    }

    // ── Pressing like a mouse ──────────────────────────────────────

    [Fact]
    public void WindowsInk_PressureAndTiltAreAvailable()
    {
        var vm = Open(WinInkAbsolute);

        Assert.False(vm.PressesLikeMouse);
        Assert.True(vm.PressureAndTiltAvailable);
    }

    [Fact]
    public void PlainOutput_OnWindows_PressesLikeAMouse_SoPressureAndTiltAreNot()
    {
        var vm = Open(NativeAbsolute);

        // "Presses like a mouse" is the Windows-only plain output; elsewhere the native pointer takes pressure.
        Assert.Equal(OperatingSystem.IsWindows(), vm.PressesLikeMouse);
        Assert.Equal(!OperatingSystem.IsWindows(), vm.PressureAndTiltAvailable);
    }

    [Fact]
    public void ThePairAlwaysDisagrees()
    {
        foreach (var mode in new[] { WinInkAbsolute, NativeAbsolute })
        {
            var vm = Open(mode);
            Assert.NotEqual(vm.PressesLikeMouse, vm.PressureAndTiltAvailable);
        }
    }

    [Fact]
    public void SwitchingToMouseLike_RaisesTheFlags()
    {
        if (!OperatingSystem.IsWindows()) return; // the switch is the Windows-only "Presses like a mouse" choice
        var vm = Open(WinInkAbsolute);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.DisableWindowsInk = true;

        Assert.Contains(nameof(TabletDetailViewModel.PressesLikeMouse), raised);
        Assert.Contains(nameof(TabletDetailViewModel.PressureAndTiltAvailable), raised);
        Assert.Contains(nameof(TabletDetailViewModel.PressureDisabledNote), raised);
    }

    [Fact]
    public void ThePressureDisabledNote_StepsAsideForTheMouseNote()
    {
        // Pressure disabled under Windows Ink: the existing note. Under a mouse-like pen the stronger
        // "presses like a mouse" note is the one shown, so the two never stack.
        Assert.True(Open(WinInkAbsolute, disablePressure: true).PressureDisabledNote);
        Assert.False(Open(WinInkAbsolute, disablePressure: false).PressureDisabledNote);
        if (OperatingSystem.IsWindows())
            Assert.False(Open(NativeAbsolute, disablePressure: true).PressureDisabledNote);
    }

    [Fact]
    public void TheSavedPressureAndTiltChoices_AreNotTouchedByTheMouseLikeState()
    {
        var vm = Open(NativeAbsolute, disablePressure: true);

        Assert.True(vm.DisablePressure);        // greyed out, not cleared: it comes back as it was
    }

    // ── The tip card ───────────────────────────────────────────────

    private static PenSwitchRowViewModel Row(PenSwitchKind kind, PluginSettingStore? store, bool canEdit = true) =>
        new(kind, 1, store, canEdit, (_, _, _) => Task.CompletedTask);

    [Fact]
    public void TipWithNothingBound_IsDisabled_WithNoFix()
    {
        var row = Row(PenSwitchKind.Tip, null);

        Assert.True(row.IsDisabled);
        Assert.False(row.IsRecommended);
        Assert.False(row.IsNotRecommended);     // not "changed to a custom action"
        Assert.False(row.ShowUseAdaptive);      // the Disable pen tip toggle is the way back
        Assert.False(row.SetAutoCommand.CanExecute(null));
    }

    [Fact]
    public void EraserWithNothingBound_KeepsItsFix()
    {
        // Only the tip has a toggle beside it, so an unbound eraser still needs the Fix.
        var row = Row(PenSwitchKind.Eraser, null);

        Assert.False(row.IsDisabled);
        Assert.True(row.IsNotRecommended);
        Assert.True(row.ShowUseAdaptive);
        Assert.True(row.SetAutoCommand.CanExecute(null));
    }

    [Fact]
    public void TipOnAnotherBinding_IsStillChangedToACustomAction()
    {
        var mouse = AuxKeyBinding.MakeMouseBinding("Left");

        var row = Row(PenSwitchKind.Tip, mouse);

        Assert.False(row.IsDisabled);
        Assert.True(row.IsNotRecommended);
        Assert.True(row.ShowUseAdaptive);
    }

    [Fact]
    public void TipOnAdaptive_IsWorkingNormally()
    {
        var row = Row(PenSwitchKind.Tip, PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip));

        Assert.True(row.IsRecommended);
        Assert.False(row.IsDisabled);
        Assert.False(row.IsNotRecommended);
    }

    [Fact]
    public void ADisabledTipOnAReadOnlyHost_OffersNothing()
    {
        var row = Row(PenSwitchKind.Tip, null, canEdit: false);

        Assert.True(row.IsDisabled);
        Assert.False(row.ShowUseAdaptive);
    }
}
