using System;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace OpenTabletArtist.Controls;

/// <summary>
/// A labelled slider row: a caption on the left, a slider, and (optionally) a live numeric readout on
/// the right. Consolidates the label + <see cref="Slider"/> + value-text pattern repeated across the
/// Dynamics and Theme pages. The default <c>ControlTheme</c> lives in Themes/Styles.axaml.
///
/// <see cref="Value"/> binds two-way by default. <see cref="Ticks"/> is a comma-separated list (e.g.
/// "0,0.5,1") applied to the inner slider's tick marks; <see cref="ValueFormat"/> drives the readout.
/// </summary>
public class LabeledSlider : TemplatedControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<LabeledSlider, string>(nameof(Label), "");
    public static readonly StyledProperty<double> LabelWidthProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(LabelWidth), 120);
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(Value), 0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> TickFrequencyProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(TickFrequency), 0);
    // Keyboard step for arrow keys (SmallChange) / PageUp-Down (LargeChange). Defaults match Avalonia's
    // Slider so existing callers are unchanged; set a fine SmallChange for keyboard accessibility on
    // narrow-range sliders where the default step of 1 would jump the whole range (#603).
    public static readonly StyledProperty<double> SmallChangeProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(SmallChange), 1);
    public static readonly StyledProperty<double> LargeChangeProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(LargeChange), 10);
    public static readonly StyledProperty<bool> IsSnapToTickEnabledProperty =
        AvaloniaProperty.Register<LabeledSlider, bool>(nameof(IsSnapToTickEnabled));
    public static readonly StyledProperty<TickPlacement> TickPlacementProperty =
        AvaloniaProperty.Register<LabeledSlider, TickPlacement>(nameof(TickPlacement), TickPlacement.None);
    public static readonly StyledProperty<string?> TicksProperty =
        AvaloniaProperty.Register<LabeledSlider, string?>(nameof(Ticks));
    public static readonly StyledProperty<bool> ShowValueProperty =
        AvaloniaProperty.Register<LabeledSlider, bool>(nameof(ShowValue), true);
    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<LabeledSlider, string>(nameof(ValueFormat), "0.00");
    /// <summary>When set (non-empty), shown in the readout instead of the formatted <see cref="Value"/>
    /// — for callers whose view model already formats the value (with units, steps, etc.).</summary>
    public static readonly StyledProperty<string?> ValueTextOverrideProperty =
        AvaloniaProperty.Register<LabeledSlider, string?>(nameof(ValueTextOverride));
    /// <summary>Renders the readout in a monospace font + primary color (so changing digits don't shift).</summary>
    public static readonly StyledProperty<bool> MonospaceProperty =
        AvaloniaProperty.Register<LabeledSlider, bool>(nameof(Monospace));
    /// <summary>Fixed readout width; NaN (default) auto-sizes to the text.</summary>
    public static readonly StyledProperty<double> ValueWidthProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(ValueWidth), double.NaN);

    /// <summary>The value a reset puts the slider back to (#1048). Null (the default) draws no reset button, which is
    /// how the Theme sliders stay as they were. When set, the button is always drawn and only enabled once the value
    /// has moved off it, so the row does not change shape as you drag.</summary>
    public static readonly StyledProperty<double?> DefaultValueProperty =
        AvaloniaProperty.Register<LabeledSlider, double?>(nameof(DefaultValue));

    public static readonly DirectProperty<LabeledSlider, bool> HasDefaultProperty =
        AvaloniaProperty.RegisterDirect<LabeledSlider, bool>(nameof(HasDefault), o => o.HasDefault);
    public static readonly DirectProperty<LabeledSlider, bool> CanResetProperty =
        AvaloniaProperty.RegisterDirect<LabeledSlider, bool>(nameof(CanReset), o => o.CanReset);
    public static readonly DirectProperty<LabeledSlider, string> ResetTipProperty =
        AvaloniaProperty.RegisterDirect<LabeledSlider, string>(nameof(ResetTip), o => o.ResetTip);

    public static readonly DirectProperty<LabeledSlider, string> ValueTextProperty =
        AvaloniaProperty.RegisterDirect<LabeledSlider, string>(nameof(ValueText), o => o.ValueText);

    private bool _hasDefault, _canReset;
    private string _resetTip = "";

    /// <summary>There is a value to reset to, so the reset button is drawn.</summary>
    public bool HasDefault { get => _hasDefault; private set => SetAndRaise(HasDefaultProperty, ref _hasDefault, value); }
    /// <summary>The value is not the default, so a reset would change something.</summary>
    public bool CanReset { get => _canReset; private set => SetAndRaise(CanResetProperty, ref _canReset, value); }
    /// <summary>What the reset button's tooltip and screen-reader name say: it names the slider and the value.</summary>
    public string ResetTip { get => _resetTip; private set => SetAndRaise(ResetTipProperty, ref _resetTip, value); }

    private string _valueText = "";
    /// <summary>The formatted readout shown on the right (Value formatted with ValueFormat).</summary>
    public string ValueText
    {
        get => _valueText;
        private set => SetAndRaise(ValueTextProperty, ref _valueText, value);
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double LabelWidth { get => GetValue(LabelWidthProperty); set => SetValue(LabelWidthProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double TickFrequency { get => GetValue(TickFrequencyProperty); set => SetValue(TickFrequencyProperty, value); }
    public double SmallChange { get => GetValue(SmallChangeProperty); set => SetValue(SmallChangeProperty, value); }
    public double LargeChange { get => GetValue(LargeChangeProperty); set => SetValue(LargeChangeProperty, value); }
    public bool IsSnapToTickEnabled { get => GetValue(IsSnapToTickEnabledProperty); set => SetValue(IsSnapToTickEnabledProperty, value); }
    public TickPlacement TickPlacement { get => GetValue(TickPlacementProperty); set => SetValue(TickPlacementProperty, value); }
    public string? Ticks { get => GetValue(TicksProperty); set => SetValue(TicksProperty, value); }
    public bool ShowValue { get => GetValue(ShowValueProperty); set => SetValue(ShowValueProperty, value); }
    public string ValueFormat { get => GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }
    public string? ValueTextOverride { get => GetValue(ValueTextOverrideProperty); set => SetValue(ValueTextOverrideProperty, value); }
    public bool Monospace { get => GetValue(MonospaceProperty); set => SetValue(MonospaceProperty, value); }
    public double ValueWidth { get => GetValue(ValueWidthProperty); set => SetValue(ValueWidthProperty, value); }
    public double? DefaultValue { get => GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }

    private Slider? _slider;
    private Button? _reset;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _slider = e.NameScope.Find<Slider>("PART_Slider");
        if (_reset is not null) _reset.Click -= OnResetClick;
        _reset = e.NameScope.Find<Button>("PART_Reset");
        if (_reset is not null) _reset.Click += OnResetClick;
        ApplyTicks();
        UpdateValueText();
        UpdateReset();
    }

    // Back to the default. Clamped to the slider's own range, so a default outside it cannot write a value
    // the slider would then clamp and write back.
    private void OnResetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DefaultValue is { } d) Value = Math.Clamp(d, Minimum, Maximum);
    }

    private void UpdateReset()
    {
        HasDefault = DefaultValue is not null;
        CanReset = DefaultValue is { } d && Math.Abs(Value - Math.Clamp(d, Minimum, Maximum)) > 1e-9;
        ResetTip = DefaultValue is { } v
            ? $"Reset {(string.IsNullOrEmpty(Label) ? "this setting" : Label.ToLowerInvariant())} to {FormatDefault(v)}"
            : "";
    }

    // The default as the readout would show it, so the tip and the readout agree.
    private string FormatDefault(double v) => v.ToString(ValueFormat, CultureInfo.CurrentCulture);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == ValueFormatProperty
            || change.Property == ValueTextOverrideProperty)
            UpdateValueText();
        else if (change.Property == TicksProperty)
            ApplyTicks();

        if (change.Property == ValueProperty || change.Property == DefaultValueProperty
            || change.Property == MinimumProperty || change.Property == MaximumProperty
            || change.Property == LabelProperty || change.Property == ValueFormatProperty)
            UpdateReset();
    }

    private void UpdateValueText() =>
        ValueText = !string.IsNullOrEmpty(ValueTextOverride)
            ? ValueTextOverride!
            : Value.ToString(ValueFormat, CultureInfo.CurrentCulture);

    // The inner slider's Ticks is an AvaloniaList<double>; parse the comma-separated string into it.
    private void ApplyTicks()
    {
        if (_slider is null) return;
        if (string.IsNullOrWhiteSpace(Ticks)) { _slider.Ticks = null; return; }

        var list = new AvaloniaList<double>();
        foreach (var part in Ticks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                list.Add(v);
        _slider.Ticks = list;
    }
}
