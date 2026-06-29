using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdArtist.Domain;
using OtdArtist.Services;
using OtdArtist.ViewModels;
using Xunit;

namespace OtdArtist.Tests;

public class CalibrationViewModelTests
{
    private sealed class NoopDebugSession : IDaemonDebugSession
    {
#pragma warning disable CS0067
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    private static readonly TabletDigitizerSpec Digi = new(100, 100, 1000, 1000);
    private static readonly MappingArea Input = new(50, 50, 100, 100);
    private static readonly MappingArea Output = new(960, 540, 1920, 1080);
    private static readonly DisplayInfo Display =
        new(Number: 1, Name: "", Width: 1920, Height: 1080, X: 0, Y: 0, IsPrimary: true);

    private static (CalibrationViewModel vm, Settings settings, Action<int> applyCount) NewVm()
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "T" } } };
        int applies = 0;
        var ctx = new CalibrationViewModel.Context(
            "T", Digi, Input, Output, Display, settings,
            _ => { applies++; return Task.CompletedTask; }, new NoopDebugSession());
        return (new CalibrationViewModel(ctx), settings, _ => { });
    }

    // Feed a tap (several down-samples + a lift) whose raw maps exactly onto target index i.
    private static void Tap(CalibrationViewModel vm, int targetIndex)
    {
        var t = vm.Targets[targetIndex];
        var desktop = new Vector2((float)(Display.X + t.X * Display.Width), (float)(Display.Y + t.Y * Display.Height));
        var raw = AbsolutePositionMapper.MapFromDesktop(desktop, Digi, Input, Output)!.Value;
        for (int k = 0; k < 5; k++)
            vm.OnSample(new PenSample(0, 0, raw.X, raw.Y, 0.5, 0, 0, 0, IsDown: true));
        vm.OnSample(new PenSample(0, 0, raw.X, raw.Y, 0, 0, 0, 0, IsDown: false)); // lift commits the tap
    }

    [Fact]
    public void Capture_AdvancesThroughTargets_ThenWritesCalibration()
    {
        var (vm, settings, _) = NewVm();

        Assert.True(vm.IsCapturing);
        Assert.Equal(0, vm.CurrentTarget);

        Tap(vm, 0);
        Assert.Equal(1, vm.CapturedCount);
        Assert.Equal(1, vm.CurrentTarget);

        Tap(vm, 1);
        Tap(vm, 2);
        Assert.Equal(3, vm.CapturedCount);

        Tap(vm, 3);

        // All four captured → solved, applied, and now confirming.
        Assert.Equal(4, vm.CapturedCount);
        Assert.True(vm.IsConfirming);
        Assert.True(vm.ShowApply);
        var cal = CalibrationProfile.Read(settings, "T");
        Assert.NotNull(cal);
        Assert.True(cal!.Enabled);
    }

    [Fact]
    public void Redo_ResetsCaptureToFirstTarget()
    {
        var (vm, _, _) = NewVm();
        Tap(vm, 0);
        Tap(vm, 1);
        Assert.Equal(2, vm.CapturedCount);

        vm.RedoCommand.Execute(null);

        Assert.Equal(0, vm.CapturedCount);
        Assert.Equal(0, vm.CurrentTarget);
        Assert.True(vm.IsCapturing);
    }

    // Cursor review: after the preview is applied, Redo must clear that correction so the next
    // capture is uncorrected — even when there was no calibration at open.
    [Fact]
    public void RedoAfterPreview_DisablesTheLivePreviewCalibration()
    {
        var (vm, settings, _) = NewVm();
        Tap(vm, 0); Tap(vm, 1); Tap(vm, 2); Tap(vm, 3);
        Assert.True(vm.IsConfirming);
        Assert.True(CalibrationProfile.Read(settings, "T")!.Enabled); // preview is live

        vm.RedoCommand.Execute(null);

        // The preview correction must be disabled (or gone) before recapture.
        var cal = CalibrationProfile.Read(settings, "T");
        Assert.True(cal is null || !cal.Enabled);
        Assert.True(vm.IsCapturing);
        Assert.Equal(0, vm.CapturedCount);
    }

    [Fact]
    public void TapsAwayFromTarget_AreIgnored()
    {
        var (vm, _, _) = NewVm();
        // A down/lift far from the active target (display centre, but target 0 is the corner) → no capture.
        var center = AbsolutePositionMapper.MapFromDesktop(new Vector2(960, 540), Digi, Input, Output)!.Value;
        for (int k = 0; k < 6; k++)
            vm.OnSample(new PenSample(0, 0, center.X, center.Y, 0.5, 0, 0, 0, IsDown: true));
        vm.OnSample(new PenSample(0, 0, center.X, center.Y, 0, 0, 0, 0, IsDown: false));

        Assert.Equal(0, vm.CapturedCount);
        Assert.True(vm.IsCapturing);
    }

    [Fact]
    public void Clear_RemovesCalibration()
    {
        var (vm, settings, _) = NewVm();
        CalibrationProfile.Write(settings, "T", new Matrix3x2(1.05f, 0, 0, 1.05f, 0.01f, 0.01f), enable: true, "fp");

        vm.ClearCommand.Execute(null);

        Assert.Null(CalibrationProfile.Read(settings, "T"));
    }
}
