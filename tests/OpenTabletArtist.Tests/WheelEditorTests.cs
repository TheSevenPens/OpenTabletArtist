using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class WheelEditorTests
{
    private static ButtonBinding Row(string label) =>
        new(0, AuxBinding.Unbound, isOtherBinding: false, otherLabel: "", canEdit: false,
            applyBinding: null, label: label);

    private static WheelEditor MakeEditor(double? stepSizeDegrees = 30,
        System.Func<int, bool, double, Task>? applyThreshold = null)
    {
        var cw = Row("Clockwise");
        var ccw = Row("Counter-clockwise");
        return new WheelEditor(
            wheelIndex: 0,
            title: "Wheel",
            showTitle: false,
            clockwise: cw,
            counterClockwise: ccw,
            buttons: new List<ButtonBinding>(),
            clockwiseThreshold: 30,
            counterClockwiseThreshold: 30,
            stepSizeDegrees: stepSizeDegrees,
            applyThreshold: applyThreshold);
    }

    [Fact]
    public void OnRelativeDelta_Negative_FlashesClockwise()
    {
        var editor = MakeEditor();
        editor.OnRelativeDelta(-1);
        Assert.True(editor.Clockwise.IsPressed);
        Assert.False(editor.CounterClockwise.IsPressed);
    }

    [Fact]
    public void OnRelativeDelta_Positive_FlashesCounterClockwise()
    {
        var editor = MakeEditor();
        editor.OnRelativeDelta(1);
        Assert.False(editor.Clockwise.IsPressed);
        Assert.True(editor.CounterClockwise.IsPressed);
    }

    [Fact]
    public void OnRelativeDelta_AccumulatesSyntheticPosition()
    {
        var editor = MakeEditor(stepSizeDegrees: 15); // 24 steps per turn (e.g. Wacom PTK-670)
        Assert.Null(editor.LivePosition);             // no position until the wheel moves
        editor.OnRelativeDelta(6);                    // reversed: -6 steps wraps to 18/24
        Assert.Equal(0.75, editor.LivePosition!.Value, 3);
    }

    [Fact]
    public void OnRelativeDelta_OppositeDirection_AccumulatesOtherWay()
    {
        var editor = MakeEditor(stepSizeDegrees: 15); // 24 steps per turn
        editor.OnRelativeDelta(-6);                   // reversed: +6 steps → 6/24
        Assert.Equal(0.25, editor.LivePosition!.Value, 3);
    }

    [Fact]
    public void OnRelativeDelta_WithoutStepInfo_LeavesPositionNull()
    {
        var editor = MakeEditor(stepSizeDegrees: null); // steps/turn unknown → no synthetic position
        editor.OnRelativeDelta(3);
        Assert.Null(editor.LivePosition);
        Assert.True(editor.CounterClockwise.IsPressed); // still flashes direction
    }

    [Fact]
    public void OnAbsolutePosition_WrappedDelta_FlashesMatchingDirection()
    {
        var editor = MakeEditor(stepSizeDegrees: 30); // 12 steps per turn
        editor.OnAbsolutePosition(0);   // seed
        editor.OnAbsolutePosition(3);   // +3 steps → OTD positive delta → physical CCW flash
        Assert.True(editor.CounterClockwise.IsPressed);
    }

    [Fact]
    public void ThresholdText_WithStepSize_IncludesStepCount()
    {
        var editor = MakeEditor(stepSizeDegrees: 30);
        Assert.Contains("step", editor.ClockwiseThresholdText, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasStepInfo_True_WhenStepSizeKnown()
    {
        var editor = MakeEditor(stepSizeDegrees: 30);
        Assert.True(editor.HasStepInfo);
        Assert.Contains("30", editor.StepInfo);
    }

    [Fact]
    public void Threshold_UserChange_AboveMinimum_Applies()
    {
        var applied = new List<(bool Clockwise, double Deg)>();
        var editor = MakeEditor(stepSizeDegrees: 30, applyThreshold: (_, cw, deg) =>
        {
            applied.Add((cw, deg));
            return Task.CompletedTask;
        });

        editor.ClockwiseThreshold = 60; // a real slider gesture (>= ThresholdMin = 30)

        Assert.Single(applied);
        Assert.Equal((true, 60), applied[0]);
        Assert.Equal(60, editor.ClockwiseThreshold);
    }

    [Fact]
    public void Threshold_SubMinimumWriteback_IsIgnoredAndSnappedBack()
    {
        // A slider rebuilt by RefreshWheels can push its raw stored value back through the TwoWay binding
        // before Minimum is applied, landing below ThresholdMin. That must not persist (it would clobber
        // the applied value and loop) — the display snaps back to a valid value instead (#wheel-sensitivity).
        var applied = new List<(bool Clockwise, double Deg)>();
        var editor = MakeEditor(stepSizeDegrees: 30, applyThreshold: (_, cw, deg) =>
        {
            applied.Add((cw, deg));
            return Task.CompletedTask;
        });

        editor.ClockwiseThreshold = 1;         // below ThresholdMin (30) — spurious rebuilt-slider writeback
        editor.CounterClockwiseThreshold = 1;

        Assert.Empty(applied);                 // nothing persisted
        Assert.Equal(30, editor.ClockwiseThreshold);         // snapped back to the minimum
        Assert.Equal(30, editor.CounterClockwiseThreshold);
    }
}
