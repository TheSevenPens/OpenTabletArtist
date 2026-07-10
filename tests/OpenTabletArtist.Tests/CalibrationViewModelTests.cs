using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

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

    // Feed a full hold (HoldSamplesTarget on-target down-samples) whose raw maps exactly onto target
    // index i — the point commits once the hold fills (#457), no lift needed.
    private static void Tap(CalibrationViewModel vm, int targetIndex)
    {
        var t = vm.Targets[targetIndex];
        var desktop = new Vector2((float)(Display.X + t.X * Display.Width), (float)(Display.Y + t.Y * Display.Height));
        var raw = AbsolutePositionMapper.MapFromDesktop(desktop, Digi, Input, Output)!.Value;
        for (int k = 0; k < CalibrationViewModel.HoldSamplesTarget; k++)
            vm.OnSample(new PenSample(0, 0, raw.X, raw.Y, 0.5, 0, 0, 0, IsDown: true));
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

    // Feed a full hold on target i at a fixed pen tilt (#481).
    private static void TapWithTilt(CalibrationViewModel vm, int targetIndex, float tiltX, float tiltY)
    {
        var t = vm.Targets[targetIndex];
        var desktop = new Vector2((float)(Display.X + t.X * Display.Width), (float)(Display.Y + t.Y * Display.Height));
        var raw = AbsolutePositionMapper.MapFromDesktop(desktop, Digi, Input, Output)!.Value;
        for (int k = 0; k < CalibrationViewModel.HoldSamplesTarget; k++)
            vm.OnSample(new PenSample(0, 0, raw.X, raw.Y, 0.5, tiltX, tiltY, 0, IsDown: true));
    }

    [Fact]
    public void Capture_RecordsAveragedPenTilt()
    {
        var (vm, settings, _) = NewVm();
        TapWithTilt(vm, 0, 15f, -5f);
        TapWithTilt(vm, 1, 15f, -5f);
        TapWithTilt(vm, 2, 15f, -5f);
        TapWithTilt(vm, 3, 15f, -5f);

        var report = CalibrationProfile.Read(settings, "T")!.Report!;
        Assert.All(report.Points, p =>
        {
            Assert.True(p.HasTilt);
            Assert.Equal(15f, p.TiltX, 2);
            Assert.Equal(-5f, p.TiltY, 2);
        });
        var tilt = report.ComputeTilt();
        Assert.NotNull(tilt);
        Assert.Equal(4, tilt!.Value.Count);
    }

    [Fact]
    public void Capture_WithoutTilt_LeavesTiltUnset()
    {
        // The default Tap() feeds TiltX/TiltY = 0 (a tablet that doesn't report tilt) → no tilt recorded.
        var (vm, settings, _) = NewVm();
        Tap(vm, 0); Tap(vm, 1); Tap(vm, 2); Tap(vm, 3);

        var report = CalibrationProfile.Read(settings, "T")!.Report!;
        Assert.All(report.Points, p => Assert.False(p.HasTilt));
        Assert.Null(report.ComputeTilt());
    }

    [Fact]
    public void CornersMode_WritesAffineModel()
    {
        // 4-point corners fit a least-squares affine (#483) — robust to tap noise, unlike an exact
        // 4-point homography which overfits it and warps the corner neighbourhoods.
        var (vm, settings, _) = NewVm();
        Tap(vm, 0); Tap(vm, 1); Tap(vm, 2); Tap(vm, 3);

        var cal = CalibrationProfile.Read(settings, "T");
        Assert.Equal(CalibrationProfile.CalibrationModel.Affine, cal!.Model);
        Assert.True(cal.Enabled);
    }

    [Fact]
    public void GridMode_CapturesEveryNode_ThenWritesAffineModel()
    {
        // #486: every capture density now fits a least-squares affine (the 9 taps just make it more
        // robust). Grid capture still records all 9 nodes, but the written model is Affine, not Grid.
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "T" } } };
        var ctx = new CalibrationViewModel.Context(
            "T", Digi, Input, Output, Display, settings,
            _ => Task.CompletedTask, new NoopDebugSession(),
            CalibrationMode.Grid, GridCols: 3, GridRows: 3);
        var vm = new CalibrationViewModel(ctx);

        Assert.Equal(9, vm.Targets.Count);
        for (int i = 0; i < 9; i++) Tap(vm, i);

        Assert.True(vm.IsConfirming);
        var cal = CalibrationProfile.Read(settings, "T");
        Assert.Equal(CalibrationProfile.CalibrationModel.Affine, cal!.Model);
        Assert.Equal(9, cal.Report!.Points.Count);   // still captured every node
    }

    [Fact]
    public void Clear_RemovesCalibration()
    {
        var (vm, settings, _) = NewVm();
        CalibrationProfile.Write(settings, "T", new Matrix3x2(1.05f, 0, 0, 1.05f, 0.01f, 0.01f), enable: true, "fp");

        vm.ClearCommand.Execute(null);

        Assert.Null(CalibrationProfile.Read(settings, "T"));
    }

    // ---- #458: undo the last recorded point ----

    [Fact]
    public void UndoLastPoint_PopsOnlyTheLastPoint_AndReArmsThatTarget()
    {
        var (vm, _, _) = NewVm();
        Tap(vm, 0);
        Tap(vm, 1);
        Assert.Equal(2, vm.CapturedCount);
        Assert.Equal(2, vm.CurrentTarget);
        Assert.True(vm.CanUndoPoint);

        vm.UndoLastPointCommand.Execute(null);

        Assert.Equal(1, vm.CapturedCount);
        Assert.Equal(1, vm.CurrentTarget);
        Assert.True(vm.IsCapturing);

        vm.UndoLastPointCommand.Execute(null);
        Assert.Equal(0, vm.CapturedCount);
        Assert.False(vm.CanUndoPoint);
    }

    [Fact]
    public void UndoLastPoint_AfterFullCapture_ReturnsToCapturingAndDisablesPreview()
    {
        var (vm, settings, _) = NewVm();
        Tap(vm, 0); Tap(vm, 1); Tap(vm, 2); Tap(vm, 3);
        Assert.True(vm.IsConfirming);
        Assert.True(CalibrationProfile.Read(settings, "T")!.Enabled); // preview is live

        vm.UndoLastPointCommand.Execute(null);

        Assert.True(vm.IsCapturing);
        Assert.Equal(3, vm.CapturedCount);
        Assert.Equal(3, vm.CurrentTarget);
        var cal = CalibrationProfile.Read(settings, "T");
        Assert.True(cal is null || !cal.Enabled); // preview correction dropped before recapture
    }

    // ---- #460: recorded points are persisted with the calibration ----

    [Fact]
    public void Capture_PersistsReport_OnePointPerTarget()
    {
        var (vm, settings, _) = NewVm();
        Tap(vm, 0); Tap(vm, 1); Tap(vm, 2); Tap(vm, 3);

        var cal = CalibrationProfile.Read(settings, "T");
        Assert.NotNull(cal!.Report);
        Assert.Equal(4, cal.Report!.Points.Count);
        Assert.All(cal.Report.Points, p => Assert.Equal(CalibrationViewModel.HoldSamplesTarget, p.Samples)); // full hold per Tap()
    }

    // ---- #461: each recorded point carries the pixel-equivalent + fit quality ----

    [Fact]
    public void Capture_RecordsPixelEquivalent_AndNearZeroFit_ForExactTaps()
    {
        var (vm, settings, _) = NewVm();
        Tap(vm, 0); Tap(vm, 1); Tap(vm, 2); Tap(vm, 3); // Tap() lands raw exactly on each target

        var report = CalibrationProfile.Read(settings, "T")!.Report!;
        Assert.All(report.Points, p =>
        {
            Assert.False(float.IsNaN(p.MeasuredX));
            Assert.False(float.IsNaN(p.MeasuredY));
        });

        // Exact taps → the uncorrected pen landed on the target, so error ≈ 0.
        var fit = report.ComputeFit();
        Assert.NotNull(fit);
        Assert.True(fit!.Value.MaxErrorPx < 0.5f);
        Assert.False(fit.Value.HasOutlier);
    }

    [Fact]
    public void Capture_StoresDisplayRelativeCoordinates_NotDesktop()
    {
        // A 1920×1080 display sitting at desktop offset (0, 2160) — a monitor stacked above it. The
        // output area is centered on the display's desktop position so the mapping is consistent.
        var display = new DisplayInfo(Number: 1, Name: "", Width: 1920, Height: 1080, X: 0, Y: 2160, IsPrimary: false);
        var output = new MappingArea(960, 2700, 1920, 1080);   // center (960, 2160+540)
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "T" } } };
        var ctx = new CalibrationViewModel.Context("T", Digi, Input, output, display, settings,
            _ => Task.CompletedTask, new NoopDebugSession());
        var vm = new CalibrationViewModel(ctx);

        for (int i = 0; i < vm.Targets.Count; i++)
        {
            var t = vm.Targets[i];
            var desktop = new Vector2((float)(display.X + t.X * display.Width), (float)(display.Y + t.Y * display.Height));
            var raw = AbsolutePositionMapper.MapFromDesktop(desktop, Digi, Input, output)!.Value;
            for (int k = 0; k < CalibrationViewModel.HoldSamplesTarget; k++)
                vm.OnSample(new PenSample(0, 0, raw.X, raw.Y, 0.5, 0, 0, 0, IsDown: true));
        }

        var report = CalibrationProfile.Read(settings, "T")!.Report!;
        // Coordinates are the display's own pixels (Y in 0..1080), NOT desktop (which would be 2160..3240).
        Assert.All(report.Points, p =>
        {
            Assert.InRange(p.TargetY, 0f, 1080f);
            Assert.InRange(p.MeasuredY, -2f, 1082f);
        });
    }

    // Regression for the negative-origin capture bug (#140). When a monitor sits at a NEGATIVE
    // virtual-desktop coordinate, the mapped display's Output area is stored in min-shifted 0-based
    // space, so its origin no longer equals the raw DisplayInfo origin. The live-dot hit-test (and the
    // solver's targets) must normalize the mapped pen position against the OUTPUT origin — the same
    // space MapToDesktop produces — not the raw DisplayInfo. With the raw origin every tap landed off
    // by the shift and capture silently never fired: on macOS the cursor tracked but pressing a target
    // did nothing, even though the pen reports were perfect. (Existing tests never hit this because
    // there Output-origin == DisplayInfo-origin.)
    [Fact]
    public void Capture_Succeeds_WhenMappedDisplaySitsAtShiftedOrigin()
    {
        // Movink-like layout: display #2 at raw (0,1080); a monitor at x=-1920 shifts the desktop origin,
        // so the Output area centres at (2400,1350) in 0-based space — its origin (1920,1080) is 1920px
        // off the raw DisplayInfo origin (0,1080).
        var display = new DisplayInfo(Number: 2, Name: "", Width: 960, Height: 540, X: 0, Y: 1080, IsPrimary: false);
        var output = new MappingArea(2400, 1350, 960, 540);
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "T" } } };
        var ctx = new CalibrationViewModel.Context("T", Digi, Input, output, display, settings,
            _ => Task.CompletedTask, new NoopDebugSession());
        var vm = new CalibrationViewModel(ctx);

        float originX = output.CenterX - output.Width / 2, originY = output.CenterY - output.Height / 2;
        for (int i = 0; i < vm.Targets.Count; i++)
        {
            var t = vm.Targets[i];
            var desktop = new Vector2(originX + (float)t.X * output.Width, originY + (float)t.Y * output.Height);
            var raw = AbsolutePositionMapper.MapFromDesktop(desktop, Digi, Input, output)!.Value;
            for (int k = 0; k < CalibrationViewModel.HoldSamplesTarget; k++)
                vm.OnSample(new PenSample(0, 0, raw.X, raw.Y, 0.5, 0, 0, 0, IsDown: true));
        }

        // Every target captured despite the shift → hit-testing used the Output origin, not raw DisplayInfo.
        Assert.Equal(vm.Targets.Count, vm.CapturedCount);
        Assert.True(vm.IsConfirming);
    }

    // Tier-1 debug pen readout: off by default, populates from live samples once toggled on, and
    // reflects pen-down/pressure/raw off the report (the "is pen data even arriving?" HUD).
    [Fact]
    public void PenReadout_OffByDefault_PopulatesFromSamplesWhenToggledOn()
    {
        var (vm, _, _) = NewVm();
        Assert.False(vm.ShowPenReadout);

        // Hidden → OnSample leaves the readout at its idle default (no per-sample work when off).
        vm.OnSample(new PenSample(0, 0, 100, 200, 0.5, 0, 0, 0, IsDown: true));
        Assert.Equal("waiting for pen…", vm.PenStateText);

        vm.TogglePenReadout();
        Assert.True(vm.ShowPenReadout);

        // A down sample with pressure → state reads DOWN; details carry pressure % and raw position.
        vm.OnSample(new PenSample(0, 0, 41700, 7758, 0.67, 6, 8, 0, IsDown: true));
        Assert.True(vm.PenDown);
        Assert.Contains("DOWN", vm.PenStateText);
        Assert.Contains("67%", vm.PenDetailsText);
        Assert.Contains("41700", vm.PenDetailsText);

        // A lifted sample → state flips to "up" (rate is still > 0 as the sample just arrived).
        vm.OnSample(new PenSample(0, 0, 41700, 7758, 0, 0, 0, 0, IsDown: false));
        Assert.False(vm.PenDown);
        Assert.Contains("up", vm.PenStateText);

        vm.TogglePenReadout();
        Assert.False(vm.ShowPenReadout);
    }
}
