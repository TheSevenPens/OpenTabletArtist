using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Editor state for a single tablet wheel: a clockwise + counter-clockwise rotation binding (each a
/// <see cref="ButtonBinding"/>), any wheel buttons, per-direction activation thresholds (how far you
/// turn before the action fires), and step-size info. Live rotation reports flash the matching row by
/// toggling its <see cref="ButtonBinding.IsPressed"/> — reusing the ExpressKeys press highlight.
/// </summary>
public partial class WheelEditor : ObservableObject
{
    private readonly Func<int, bool, double, Task>? _applyThreshold;
    private readonly double _wheelSteps;   // StepCount (360 / step°); 0 when unknown/relative
    private int? _lastPosition;
    private CancellationTokenSource? _flashCts;
    private bool _suppress;

    public int WheelIndex { get; }
    public string Title { get; }
    public bool ShowTitle { get; }

    public ButtonBinding Clockwise { get; }
    public ButtonBinding CounterClockwise { get; }
    public IReadOnlyList<ButtonBinding> Buttons { get; }
    public bool HasButtons => Buttons.Count > 0;

    /// <summary>False while wheel mapping is suspended / read-only — dims the sensitivity sliders.</summary>
    public bool CanEdit => _applyThreshold != null;

    public bool HasStepInfo { get; }
    public string StepInfo { get; }

    // Sensitivity slider bounds (degrees). Snaps to whole steps when the step size is known.
    public double ThresholdMin { get; }
    public double ThresholdMax { get; }
    public double ThresholdTick { get; }

    [ObservableProperty] private double _clockwiseThreshold;
    [ObservableProperty] private double _counterClockwiseThreshold;
    public string ClockwiseThresholdText => ThresholdText(ClockwiseThreshold);
    public string CounterClockwiseThresholdText => ThresholdText(CounterClockwiseThreshold);

    // Live gauge state: LivePosition is 0..1 around the ring (null before the first touch / relative
    // wheels); the turning flags drive the direction glyph and fade with the row flash.
    [ObservableProperty] private double? _livePosition;
    [ObservableProperty] private bool _turningClockwise;
    [ObservableProperty] private bool _turningCounterClockwise;

    public WheelEditor(int wheelIndex, string title, bool showTitle,
        ButtonBinding clockwise, ButtonBinding counterClockwise, IReadOnlyList<ButtonBinding> buttons,
        double clockwiseThreshold, double counterClockwiseThreshold, double? stepSizeDegrees,
        Func<int, bool, double, Task>? applyThreshold)
    {
        WheelIndex = wheelIndex;
        Title = title;
        ShowTitle = showTitle;
        Clockwise = clockwise;
        CounterClockwise = counterClockwise;
        Buttons = buttons;
        _applyThreshold = applyThreshold;

        if (stepSizeDegrees is > 0 and double step)
        {
            HasStepInfo = true;
            _wheelSteps = Math.Round(360.0 / step);
            StepInfo = $"≈{step:0.#}° per step · {_wheelSteps:0} steps per full turn";
            ThresholdMin = step;
            ThresholdMax = Math.Max(step * 2, 90);
            ThresholdTick = step;
        }
        else
        {
            ThresholdMin = 1;
            ThresholdMax = 90;
            ThresholdTick = 1;
        }

        _suppress = true;   // seed from the profile without scheduling a save
        ClockwiseThreshold = Clamp(clockwiseThreshold);
        CounterClockwiseThreshold = Clamp(counterClockwiseThreshold);
        _suppress = false;
    }

    private double Clamp(double v) => v < ThresholdMin ? ThresholdMin : v > ThresholdMax ? ThresholdMax : v;

    private string ThresholdText(double deg) => HasStepInfo
        ? $"{deg:0}° · every {Math.Max(1, Math.Round(deg / ThresholdTick)):0} step(s)"
        : $"{deg:0}°";

    partial void OnClockwiseThresholdChanged(double value)
    {
        OnPropertyChanged(nameof(ClockwiseThresholdText));
        if (!_suppress) _ = _applyThreshold?.Invoke(WheelIndex, true, value);
    }

    partial void OnCounterClockwiseThresholdChanged(double value)
    {
        OnPropertyChanged(nameof(CounterClockwiseThresholdText));
        if (!_suppress) _ = _applyThreshold?.Invoke(WheelIndex, false, value);
    }

    // ── Live rotation feedback ──────────────────────────────────────────────
    /// <summary>Absolute-wheel (touch-ring) position update. Computes a wrapped delta the same way the
    /// daemon does (see OTD WheelBindings.ComputeAbsoluteWheelDelta) so the flash matches the binding
    /// that actually fires; null resets tracking (finger lifted).</summary>
    public void OnAbsolutePosition(uint? position)
    {
        if (position is not uint pos) { _lastPosition = null; return; }  // idle / no change → keep last marker
        if (_wheelSteps > 0) LivePosition = pos / _wheelSteps;
        if (_lastPosition is int last && _wheelSteps > 0)
        {
            // OTD's delta sign is positive for its "clockwise", but the reported position runs opposite
            // to physical rotation on tested rings — so a negative delta is a physical clockwise turn.
            // Kept in step with the inverted CW/CCW store mapping in TabletDetailViewModel.RefreshWheels.
            double d = ((pos - (double)last + 1.5 * _wheelSteps) % _wheelSteps) - 0.5 * _wheelSteps;
            if (d < 0) Flash(clockwise: true);
            else if (d > 0) Flash(clockwise: false);
        }
        _lastPosition = (int)pos;
    }

    /// <summary>Relative-wheel step delta. Inverted to match the physical CW/CCW mapping used for the
    /// binding stores (see TabletDetailViewModel.RefreshWheels).</summary>
    public void OnRelativeDelta(int delta)
    {
        if (delta < 0) Flash(clockwise: true);
        else if (delta > 0) Flash(clockwise: false);
    }

    private void Flash(bool clockwise)
    {
        Clockwise.IsPressed = clockwise;
        CounterClockwise.IsPressed = !clockwise;
        TurningClockwise = clockwise;
        TurningCounterClockwise = !clockwise;
        _flashCts?.Cancel();
        var cts = _flashCts = new CancellationTokenSource();
        _ = ResetAsync(cts.Token);

        async Task ResetAsync(CancellationToken ct)
        {
            try { await Task.Delay(220, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            Clockwise.IsPressed = false;
            CounterClockwise.IsPressed = false;
            TurningClockwise = false;
            TurningCounterClockwise = false;
        }
    }
}
