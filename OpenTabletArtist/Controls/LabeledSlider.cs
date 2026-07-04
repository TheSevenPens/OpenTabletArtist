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
    /// <summary>Renders the readout in a monospace font + primary colour (so changing digits don't shift).</summary>
    public static readonly StyledProperty<bool> MonospaceProperty =
        AvaloniaProperty.Register<LabeledSlider, bool>(nameof(Monospace));
    /// <summary>Fixed readout width; NaN (default) auto-sizes to the text.</summary>
    public static readonly StyledProperty<double> ValueWidthProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(ValueWidth), double.NaN);

    public static readonly DirectProperty<LabeledSlider, string> ValueTextProperty =
        AvaloniaProperty.RegisterDirect<LabeledSlider, string>(nameof(ValueText), o => o.ValueText);

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
    public bool IsSnapToTickEnabled { get => GetValue(IsSnapToTickEnabledProperty); set => SetValue(IsSnapToTickEnabledProperty, value); }
    public TickPlacement TickPlacement { get => GetValue(TickPlacementProperty); set => SetValue(TickPlacementProperty, value); }
    public string? Ticks { get => GetValue(TicksProperty); set => SetValue(TicksProperty, value); }
    public bool ShowValue { get => GetValue(ShowValueProperty); set => SetValue(ShowValueProperty, value); }
    public string ValueFormat { get => GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }
    public string? ValueTextOverride { get => GetValue(ValueTextOverrideProperty); set => SetValue(ValueTextOverrideProperty, value); }
    public bool Monospace { get => GetValue(MonospaceProperty); set => SetValue(MonospaceProperty, value); }
    public double ValueWidth { get => GetValue(ValueWidthProperty); set => SetValue(ValueWidthProperty, value); }

    private Slider? _slider;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _slider = e.NameScope.Find<Slider>("PART_Slider");
        ApplyTicks();
        UpdateValueText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == ValueFormatProperty
            || change.Property == ValueTextOverrideProperty)
            UpdateValueText();
        else if (change.Property == TicksProperty)
            ApplyTicks();
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
