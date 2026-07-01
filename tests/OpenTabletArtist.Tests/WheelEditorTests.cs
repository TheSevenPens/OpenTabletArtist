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

    private static WheelEditor MakeEditor(double? stepSizeDegrees = 30)
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
            applyThreshold: null);
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
}
