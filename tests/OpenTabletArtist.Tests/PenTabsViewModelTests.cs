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

    // ── The tip and eraser card ───────────────────────────────────────────────

    private static PenSwitchRowViewModel Row(PenSwitchKind kind, PluginSettingStore? store, bool canEdit = true) =>
        new(kind, 1, store, canEdit, (_, _, _) => Task.CompletedTask);

    [Fact]
    public void TipWithNothingBound_IsDisabled_WithNoFix_ButDefaultActionPutsItBack()
    {
        var row = Row(PenSwitchKind.Tip, null);

        Assert.True(row.IsDisabled);
        Assert.False(row.IsRecommended);
        Assert.False(row.IsNotRecommended);     // not "changed to a custom action"
        Assert.True(row.SetAutoCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheTipsDisableCommand_ClearsTheBinding_ThroughTheDelegate()
    {
        var disabled = 0;
        var row = new PenSwitchRowViewModel(PenSwitchKind.Tip, 1, PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip),
            true, (_, _, _) => Task.CompletedTask, () => { disabled++; return Task.CompletedTask; });

        Assert.True(row.DisableCommand.CanExecute(null));
        await row.DisableCommand.ExecuteAsync(null);

        Assert.Equal(1, disabled);
    }

    [Fact]
    public async Task TheErasersDisableCommand_ClearsTheBinding_ThroughTheDelegate()
    {
        var disabled = 0;
        var row = new PenSwitchRowViewModel(PenSwitchKind.Eraser, 1, PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser),
            true, (_, _, _) => Task.CompletedTask, () => { disabled++; return Task.CompletedTask; });

        await row.DisableCommand.ExecuteAsync(null);

        Assert.Equal(1, disabled);
    }

    [Fact]
    public void ARowGivenNothingToDisableWith_OffersNoDisable()
    {
        var row = Row(PenSwitchKind.Eraser, PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser));

        Assert.False(row.DisableCommand.CanExecute(null));
    }

    [Fact]
    public void EraserWithNothingBound_IsDisabled_ButDefaultActionPutsItBack()
    {
        var row = Row(PenSwitchKind.Eraser, null);

        Assert.True(row.IsDisabled);
        Assert.False(row.IsNotRecommended);
        Assert.True(row.SetAutoCommand.CanExecute(null));
    }

    [Fact]
    public void TipOnAnotherBinding_IsStillChangedToACustomAction()
    {
        var mouse = AuxKeyBinding.MakeMouseBinding("Left");

        var row = Row(PenSwitchKind.Tip, mouse);

        Assert.False(row.IsDisabled);
        Assert.True(row.IsNotRecommended);
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
        Assert.False(row.SetAutoCommand.CanExecute(null));
        Assert.False(row.DisableCommand.CanExecute(null));
    }

    // ── Live tilt, for the tilt tab's diagrams ─────────────────────

    private static PenSample Sample(double tiltX, double tiltY, bool hasTilt = true) =>
        new(0.5, 0.5, 0, 0, 0, tiltX, tiltY, 0, false, HasTilt: hasTilt);

    [Fact]
    public void BeforeAnyReport_ThereIsNoLiveTilt()
    {
        var vm = Open(WinInkAbsolute);

        Assert.Null(vm.LiveAzimuth);
        Assert.Null(vm.LiveAltitude);
        Assert.Equal("—", vm.LiveAzimuthText);
        Assert.Equal("—", vm.LiveAltitudeText);
    }

    [Fact]
    public void AReportWithTilt_SetsTheAzimuthAndAltitude_AsTheTiltMathDoes()
    {
        var vm = Open(WinInkAbsolute);

        vm.SetLiveTilt(Sample(30, 30));

        Assert.Equal(TiltMath.Azimuth(30, 30), vm.LiveAzimuth!.Value, 3);
        Assert.Equal(TiltMath.Altitude(30, 30), vm.LiveAltitude!.Value, 3);
        Assert.Equal("45°", vm.LiveAzimuthText);
        Assert.Equal("51°", vm.LiveAltitudeText);
    }

    [Theory]
    [InlineData(false)]     // hovering
    [InlineData(true)]      // touching
    public void ThePenStream_FeedsTheLiveTilt_WhetherThePenHoversOrTouches(bool touching)
    {
        var vm = Open(WinInkAbsolute);

        vm.OnPenSample(new PenSample(0.5, 0.5, 0, 0, touching ? 0.4 : 0, 30, 30, 0, touching, HasTilt: true));

        Assert.Equal("45°", vm.LiveAzimuthText);
    }

    [Fact]
    public void AnUprightPen_ReadsNinetyDegreesAltitude()
    {
        var vm = Open(WinInkAbsolute);

        vm.SetLiveTilt(Sample(0, 0));

        Assert.Equal("90°", vm.LiveAltitudeText);
    }

    [Fact]
    public void AReportWithoutTilt_ClearsTheLiveTilt_RatherThanReadingItAsUpright()
    {
        var vm = Open(WinInkAbsolute);
        vm.SetLiveTilt(Sample(30, 0));

        vm.SetLiveTilt(Sample(0, 0, hasTilt: false));

        Assert.Null(vm.LiveAzimuth);
        Assert.Null(vm.LiveAltitude);
    }

    [Fact]
    public void StoppingTheLiveStream_ClearsTheLiveTilt()
    {
        var vm = Open(WinInkAbsolute);
        vm.SetLiveTilt(Sample(30, 0));

        vm.StopLiveInput();

        Assert.Null(vm.LiveAzimuth);
        Assert.Null(vm.LiveAltitude);
    }

    [Fact]
    public void TheLiveTilt_IsAnnouncedToTheTextsThatShowIt()
    {
        var vm = Open(WinInkAbsolute);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SetLiveTilt(Sample(30, 0));

        Assert.Contains(nameof(TabletDetailViewModel.LiveAzimuthText), raised);
        Assert.Contains(nameof(TabletDetailViewModel.LiveAltitudeText), raised);
    }

    // ── The deadzone: the lower-left node's horizontal position, as a slider ──

    [Fact]
    public void TheDeadzone_IsTheCurvesInputMinimum()
    {
        var vm = Open(WinInkAbsolute);

        vm.Curve = vm.Curve with { InputMinimum = 0.3 };

        Assert.Equal(0.3, vm.Deadzone);
    }

    [Fact]
    public void SettingTheDeadzone_WritesTheInputMinimum_AndLeavesTheRestOfTheCurveAlone()
    {
        var vm = Open(WinInkAbsolute);
        vm.Curve = vm.Curve with { Softness = 0.4, Minimum = 0.2, Maximum = 0.9, InputMaximum = 0.8 };
        var before = vm.Curve;

        vm.Deadzone = 0.3;

        Assert.Equal(before with { InputMinimum = 0.3 }, vm.Curve);
    }

    [Fact]
    public void TheNodeMovingTheDeadzone_IsAnnouncedToTheSlider()
    {
        var vm = Open(WinInkAbsolute);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Curve = vm.Curve with { InputMinimum = 0.25 };   // what dragging the node sideways does

        Assert.Contains(nameof(TabletDetailViewModel.Deadzone), raised);
    }

    [Theory]
    [InlineData(0.123, 0.12)]     // two decimals, as the node rounds
    [InlineData(-0.2, 0)]
    [InlineData(1, 0.99)]         // a hundredth short of the input maximum, as the node stops
    [InlineData(5, 0.99)]
    public void TheDeadzone_FollowsTheRuleTheNodeFollows(double asked, double stored)
    {
        var vm = Open(WinInkAbsolute);

        vm.Deadzone = asked;

        Assert.Equal(stored, vm.Deadzone, 6);
        Assert.Equal(stored, vm.Curve.InputMinimum, 6);
    }

    [Fact]
    public void TheDeadzone_StopsAHundredthShortOfAnInputMaximumThatWasPulledIn()
    {
        var vm = Open(WinInkAbsolute);
        vm.Curve = vm.Curve with { InputMaximum = 0.6 };

        vm.Deadzone = 0.9;

        Assert.Equal(0.59, vm.Deadzone, 6);
    }

    [Fact]
    public void AClampedDeadzone_IsAnnouncedSoTheSliderCanComeBackToIt()
    {
        var vm = Open(WinInkAbsolute);
        vm.Deadzone = 0.99;
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Deadzone = 1;      // clamps to what it already is: no curve change, but the slider is showing 1

        Assert.Contains(nameof(TabletDetailViewModel.Deadzone), raised);
    }
}
