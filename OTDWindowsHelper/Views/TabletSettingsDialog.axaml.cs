using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.Views;

public partial class TabletSettingsDialog : Window
{
    public TabletSettingsDialog(Profile profile, Settings? settings,
        Func<Settings, Task>? onApplyChanges = null,
        Func<Task<Profile?>>? onRefresh = null,
        (float Width, float Height)? tabletDigitizer = null,
        bool openDynamics = false,
        OtdWindowsHelper.Services.IDaemonDebugSession? penInput = null)
    {
        InitializeComponent();
        DataContext = new TabletSettingsDialogViewModel(profile, settings, onApplyChanges, onRefresh, tabletDigitizer, penInput);
        if (openDynamics)
            DynamicsTab.IsChecked = true;
    }

    // Parameterless constructor required by Avalonia XAML loader
    public TabletSettingsDialog() { InitializeComponent(); }

    // Live-refresh the display list when a monitor is added/removed/rearranged while the dialog is
    // open (#95 follow-up). Scoped to the dialog's lifetime — no lingering hooks.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Screens != null) Screens.Changed += OnScreensChanged;
        DynamicsTab.IsCheckedChanged += OnDynamicsTabChanged;
        UpdateLivePressure(); // start the live dot if the dialog opened on the Dynamics tab
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (Screens != null) Screens.Changed -= OnScreensChanged;
        DynamicsTab.IsCheckedChanged -= OnDynamicsTabChanged;
        (DataContext as TabletSettingsDialogViewModel)?.StopLivePressure();
    }

    private void OnScreensChanged(object? sender, EventArgs e) =>
        (DataContext as TabletSettingsDialogViewModel)?.RefreshDisplaysCommand.Execute(null);

    // Only stream the live pen-pressure dot while the Dynamics tab is visible (#102).
    private void OnDynamicsTabChanged(object? sender, RoutedEventArgs e) => UpdateLivePressure();

    private void UpdateLivePressure()
    {
        if (DataContext is not TabletSettingsDialogViewModel vm) return;
        if (DynamicsTab.IsChecked == true) vm.StartLivePressure();
        else vm.StopLivePressure();
    }
}

public partial class TabletSettingsDialogViewModel : ObservableObject
{
    private const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string WinInkRelativeModePath = "VoiDPlugins.OutputMode.WinInkRelativeMode";
    private const string AdaptiveBindingPath = "OpenTabletDriver.Desktop.Binding.AdaptiveBinding";

    private Profile _profile;
    private Settings? _settings;
    private readonly Func<Settings, Task>? _applyAction;
    private readonly Func<Task<Profile?>>? _refreshAction;
    private readonly (float Width, float Height)? _tabletDigitizer;

    [ObservableProperty] private IReadOnlyList<DisplayInfo> _displays = [];
    [ObservableProperty] private int? _selectedDisplayNumber;

    /// <summary>A display is selected, so "Apply" can map to it.</summary>
    public bool CanApplyDisplay => SelectedDisplayNumber != null && _applyAction != null;

    partial void OnSelectedDisplayNumberChanged(int? value)
    {
        OnPropertyChanged(nameof(CanApplyDisplay));
        ApplyDisplayCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsWinInkAbsoluteChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAbsoluteMode)); // keep the Absolute/Relative toggle in sync
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(WinInkAbsoluteModePath);
    }

    partial void OnIsWinInkRelativeChanged(bool value)
    {
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(WinInkRelativeModePath);
    }

    /// <summary>The Output Mode toggle: on = Windows Ink Absolute, off = Windows Ink Relative.
    /// Setting it switches the mode (also fixing a non-Windows-Ink mode to Windows Ink).</summary>
    public bool IsAbsoluteMode
    {
        get => IsWinInkAbsolute;
        set => _ = SetOutputMode(value ? WinInkAbsoluteModePath : WinInkRelativeModePath);
    }

    private async Task SetOutputMode(string path)
    {
        await ApplySettingsChange(p =>
        {
            p.OutputMode ??= new PluginSettingStore(path, true);
            p.OutputMode.Path = path;
        });
    }

    public TabletSettingsDialogViewModel(Profile profile, Settings? settings,
        Func<Settings, Task>? applyAction = null,
        Func<Task<Profile?>>? refreshAction = null,
        (float Width, float Height)? tabletDigitizer = null,
        IDaemonDebugSession? penInput = null)
    {
        _profile = profile;
        _settings = settings;
        _applyAction = applyAction;
        _refreshAction = refreshAction;
        _tabletDigitizer = tabletDigitizer;

        if (penInput != null)
        {
            _penInput = new DaemonPenInputSource(penInput);
            _penInput.Sample += OnPenSample;
        }

        TabletName = profile.Tablet ?? "Unknown Tablet";
        HasAreaMapping = profile.AbsoluteModeSettings != null;

        Displays = DisplayEnumerator.Enumerate();
        RefreshFromProfile();
        // Highlight the display the tablet is currently mapped to (else the primary).
        SelectedDisplayNumber = CurrentlyMappedNumber()
            ?? Displays.FirstOrDefault(d => d.IsPrimary)?.Number
            ?? Displays.FirstOrDefault()?.Number;
    }

    // Parameterless constructor for design-time
    public TabletSettingsDialogViewModel()
    {
        _profile = new Profile();
        TabletName = "Design Tablet";
        Displays = [];
    }

    private static string GetBindingName(PluginSettingStore? store)
    {
        if (store?.Path == null) return "None";
        if (store.Path == AdaptiveBindingPath)
        {
            var bindingSetting = store.Settings.FirstOrDefault(s => s.Property == "Binding");
            var value = bindingSetting?.Value?.ToString();
            return string.IsNullOrEmpty(value) ? "Adaptive Binding" : $"Adaptive Binding ({value})";
        }
        return store.Path.Split('.').LastOrDefault() ?? store.Path;
    }

    private static bool IsAdaptive(PluginSettingStore? store)
        => store?.Path == AdaptiveBindingPath;

    private static PluginSettingStore MakeAdaptiveBinding(string value)
    {
        var store = new PluginSettingStore(typeof(AdaptiveBinding), true);
        // Set the "Binding" property to the desired value
        var bindingSetting = store.Settings.FirstOrDefault(s => s.Property == "Binding");
        if (bindingSetting != null)
            bindingSetting.SetValue(value);
        else
            store.Settings.Add(new PluginSetting("Binding", value));
        return store;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (_refreshAction == null) return;
        var updated = await _refreshAction();
        if (updated != null)
        {
            _profile = updated;
            RefreshFromProfile();
        }
    }

    private async Task ApplySettingsChange(Action<Profile> modify)
    {
        if (_applyAction == null || _settings == null) return;
        modify(_profile);
        await _applyAction(_settings);
        RefreshFromProfile();
    }

    private void RefreshFromProfile()
    {
        // Output mode
        OutputModePath = _profile.OutputMode?.Path ?? "Not set";
        OutputModeShort = OutputModePath.Split('.').LastOrDefault() ?? OutputModePath;

        _skipOutputModeChange = true;
        IsWinInkAbsolute = OutputModePath.Equals(WinInkAbsoluteModePath, StringComparison.OrdinalIgnoreCase);
        IsWinInkRelative = OutputModePath.Equals(WinInkRelativeModePath, StringComparison.OrdinalIgnoreCase);
        _skipOutputModeChange = false;

        var isWinInk = IsWinInkAbsolute || IsWinInkRelative;
        CanFixOutputMode = !isWinInk && _applyAction != null;

        // Bindings
        var bindings = _profile.BindingSettings;
        TipBinding = GetBindingName(bindings.TipButton);
        EraserBinding = GetBindingName(bindings.EraserButton);
        CanFixTip = !IsAdaptive(bindings.TipButton) && _applyAction != null;
        CanFixEraser = !IsAdaptive(bindings.EraserButton) && _applyAction != null;

        // Pen buttons
        PenButtonCount = bindings.PenButtons.Count.ToString();
        AuxButtonCount = bindings.AuxButtons.Count.ToString();
        bool allAdaptive = true;
        var newPenButtons = new List<ButtonBinding>();
        for (int i = 0; i < bindings.PenButtons.Count; i++)
        {
            newPenButtons.Add(new ButtonBinding { Index = i + 1, Name = GetBindingName(bindings.PenButtons[i]) });
            if (!IsAdaptive(bindings.PenButtons[i])) allAdaptive = false;
        }
        PenButtons = newPenButtons;
        NoPenButtons = newPenButtons.Count == 0;
        CanFixPenButtons = !(bindings.PenButtons.Count == 0 || allAdaptive) && _applyAction != null;

        var newAuxButtons = new List<ButtonBinding>();
        for (int i = 0; i < bindings.AuxButtons.Count; i++)
            newAuxButtons.Add(new ButtonBinding { Index = i + 1, Name = GetBindingName(bindings.AuxButtons[i]) });
        AuxButtons = newAuxButtons;
        NoAuxButtons = newAuxButtons.Count == 0;

        // Filters
        if (_profile.Filters.Count > 0)
        {
            var filterNames = _profile.Filters.Select(f =>
            {
                var name = (f?.Path ?? "Unknown").Split('.').LastOrDefault() ?? "Unknown";
                var enabled = f?.Enable ?? true;
                return enabled ? name : $"{name} (disabled)";
            });
            FiltersText = string.Join("\n", filterNames);
        }
        else
        {
            FiltersText = "No filters configured";
        }

        // Pen dynamics — curve + smoothing (load without triggering a persist)
        var pc = PressureCurveProfile.Read(_settings, _profile.Tablet ?? "");
        var dynamics = pc?.Dynamics ?? PenDynamicsSettings.Default;
        _skipCurvePersist = true;
        Curve = dynamics.Curve;
        PressureSmoothing = dynamics.PressureSmoothing;
        PositionSmoothing = dynamics.PositionSmoothing;
        SmoothAfterCurve = dynamics.SmoothAfterCurve;
        PressureCurveEnabled = pc?.Enabled ?? false;
        _skipCurvePersist = false;
        CanEditPressure = _applyAction != null;

        // Raw JSON
        RawJson = JsonConvert.SerializeObject(_profile, Formatting.Indented);
    }

    [RelayCommand]
    private async Task FixOutputMode()
    {
        await SetOutputMode(WinInkAbsoluteModePath);
    }

    [RelayCommand(CanExecute = nameof(CanApplyDisplay))]
    private async Task ApplyDisplay()
    {
        var display = Displays.FirstOrDefault(d => d.Number == SelectedDisplayNumber);
        if (_applyAction == null || _settings == null || display == null) return;

        await ApplySettingsChange(p =>
        {
            var abs = p.AbsoluteModeSettings;
            if (abs == null) return;

            // Set display area to the full selected monitor
            abs.Display.Width = display.Width;
            abs.Display.Height = display.Height;
            abs.Display.X = display.X + display.Width / 2f;
            abs.Display.Y = display.Y + display.Height / 2f;

            // Start from the FULL tablet digitizer area
            float fullWidth = _tabletDigitizer?.Width ?? abs.Tablet.Width;
            float fullHeight = _tabletDigitizer?.Height ?? abs.Tablet.Height;

            // Guard against degenerate dimensions (the inline math used to divide by these).
            if (fullWidth <= 0 || fullHeight <= 0 || display.Width <= 0 || display.Height <= 0)
                return;

            // Largest centered sub-area of the digitizer that matches the display's aspect ratio.
            var area = AreaMappingCalculator.FitToDisplayAspect(fullWidth, fullHeight, display.Width, display.Height);
            abs.Tablet.Width = area.Width;
            abs.Tablet.Height = area.Height;
            abs.Tablet.X = area.X;
            abs.Tablet.Y = area.Y;
            abs.LockAspectRatio = true;
        });
    }

    /// <summary>Re-read the connected monitors from Windows (manual Refresh or a live display change).</summary>
    [RelayCommand]
    private void RefreshDisplays()
    {
        var keep = SelectedDisplayNumber;
        Displays = DisplayEnumerator.Enumerate();
        SelectedDisplayNumber =
            (keep != null && Displays.Any(d => d.Number == keep)) ? keep
            : CurrentlyMappedNumber()
              ?? Displays.FirstOrDefault(d => d.IsPrimary)?.Number
              ?? Displays.FirstOrDefault()?.Number;
    }

    [RelayCommand]
    private void OpenDisplaySettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    /// <summary>The display the profile is currently mapped to (full-monitor match), or null.</summary>
    private int? CurrentlyMappedNumber()
    {
        var disp = _profile.AbsoluteModeSettings?.Display;
        if (disp == null) return null;
        foreach (var d in Displays)
            // ApplyDisplay stores the display area as centre = monitor centre, size = monitor size.
            if (Approx(disp.Width, d.Width) && Approx(disp.Height, d.Height)
                && Approx(disp.X, d.X + d.Width / 2f) && Approx(disp.Y, d.Y + d.Height / 2f))
                return d.Number;
        return null;
    }

    private static bool Approx(float a, float b) => Math.Abs(a - b) <= 1.5f;

    [RelayCommand]
    private async Task FixTipBinding()
    {
        await ApplySettingsChange(p => p.BindingSettings.TipButton = MakeAdaptiveBinding("Tip"));
    }

    [RelayCommand]
    private async Task FixEraserBinding()
    {
        await ApplySettingsChange(p => p.BindingSettings.EraserButton = MakeAdaptiveBinding("Eraser"));
    }

    [RelayCommand]
    private async Task FixPenButtons()
    {
        await ApplySettingsChange(p =>
        {
            for (int i = 0; i < p.BindingSettings.PenButtons.Count; i++)
                p.BindingSettings.PenButtons[i] = MakeAdaptiveBinding($"Button {i + 1}");
        });
    }


    public string TabletName { get; }
    [ObservableProperty] private string _outputModeShort = "";
    [ObservableProperty] private string _outputModePath = "";
    [ObservableProperty] private bool _canFixOutputMode;
    [ObservableProperty] private bool _isWinInkAbsolute;
    [ObservableProperty] private bool _isWinInkRelative;
    private bool _skipOutputModeChange;
    public bool HasAreaMapping { get; }
    [ObservableProperty] private string _tipBinding = "None";
    [ObservableProperty] private bool _canFixTip;
    [ObservableProperty] private string _eraserBinding = "None";
    [ObservableProperty] private bool _canFixEraser;
    [ObservableProperty] private string _penButtonCount = "0";
    [ObservableProperty] private string _auxButtonCount = "0";
    [ObservableProperty] private bool _canFixPenButtons;
    [ObservableProperty] private bool _noPenButtons;
    [ObservableProperty] private bool _noAuxButtons;
    [ObservableProperty] private List<ButtonBinding> _penButtons = [];
    [ObservableProperty] private List<ButtonBinding> _auxButtons = [];
    [ObservableProperty] private string _filtersText = "";
    [ObservableProperty] private string _rawJson = "";

    // ── Pressure curve tab ──────────────────────────────────────

    [ObservableProperty] private PressureCurveSettings _curve = PressureCurveSettings.Default;
    [ObservableProperty] private bool _pressureCurveEnabled;
    [ObservableProperty] private bool _canEditPressure;

    [ObservableProperty] private double _pressureSmoothing;
    [ObservableProperty] private double _positionSmoothing;
    [ObservableProperty] private bool _smoothAfterCurve = true;

    public string PressureSmoothingText => PressureSmoothing.ToString("0.00");
    public string PositionSmoothingText => PositionSmoothing.ToString("0.00");

    private bool _skipCurvePersist;
    private CancellationTokenSource? _persistCts;

    partial void OnPressureSmoothingChanged(double value)
    {
        OnPropertyChanged(nameof(PressureSmoothingText));
        SchedulePersist();
    }

    partial void OnPositionSmoothingChanged(double value)
    {
        OnPropertyChanged(nameof(PositionSmoothingText));
        SchedulePersist();
    }

    partial void OnSmoothAfterCurveChanged(bool value) => SchedulePersist();

    /// <summary>Softness slider value, projected onto the <see cref="Curve"/> struct.</summary>
    public double Softness
    {
        get => Curve.Softness;
        set { if (Curve.Softness != value) Curve = Curve with { Softness = value }; }
    }

    /// <summary>Cut (dead-zone) vs Clamp (floor) below the input minimum.</summary>
    public bool CutBelowMinimum
    {
        get => Curve.MinApproach == PressureMinApproach.Cut;
        set
        {
            var want = value ? PressureMinApproach.Cut : PressureMinApproach.Clamp;
            if (Curve.MinApproach != want) Curve = Curve with { MinApproach = want };
        }
    }

    public string SoftnessText => Curve.Softness.ToString("0.00");

    // Numeric entry for the node values (#103). Setters keep input/output min &lt; max and clamp 0..1.
    public double InputMinimum
    {
        get => Curve.InputMinimum;
        set { var v = Math.Min(Clamp01(value), Curve.InputMaximum - 0.01); if (Curve.InputMinimum != v) Curve = Curve with { InputMinimum = v }; }
    }
    public double InputMaximum
    {
        get => Curve.InputMaximum;
        set { var v = Math.Max(Clamp01(value), Curve.InputMinimum + 0.01); if (Curve.InputMaximum != v) Curve = Curve with { InputMaximum = v }; }
    }
    public double OutputMinimum
    {
        get => Curve.Minimum;
        set { var v = Math.Min(Clamp01(value), Curve.Maximum); if (Curve.Minimum != v) Curve = Curve with { Minimum = v }; }
    }
    public double OutputMaximum
    {
        get => Curve.Maximum;
        set { var v = Math.Max(Clamp01(value), Curve.Minimum); if (Curve.Maximum != v) Curve = Curve with { Maximum = v }; }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    partial void OnCurveChanged(PressureCurveSettings value)
    {
        OnPropertyChanged(nameof(Softness));
        OnPropertyChanged(nameof(CutBelowMinimum));
        OnPropertyChanged(nameof(SoftnessText));
        OnPropertyChanged(nameof(InputMinimum));
        OnPropertyChanged(nameof(InputMaximum));
        OnPropertyChanged(nameof(OutputMinimum));
        OnPropertyChanged(nameof(OutputMaximum));
        SchedulePersist();
    }

    partial void OnPressureCurveEnabledChanged(bool value) => SchedulePersist();

    [RelayCommand]
    private void ResetCurve() => Curve = PressureCurveSettings.Default;

    [RelayCommand]
    private void ResetSoftness() => Softness = PressureCurveSettings.Default.Softness;

    /// <summary>Quick-start curve presets (#103).</summary>
    [RelayCommand]
    private void ApplyPreset(string kind) => Curve = kind switch
    {
        "soft" => PressureCurveSettings.Default with { Softness = 0.5 },   // lighter touch (concave)
        "firm" => PressureCurveSettings.Default with { Softness = -0.5 },  // firmer (convex)
        _ => PressureCurveSettings.Default,                               // linear
    };

    // ── Live pen-pressure preview (#102) ──────────────────────────
    private readonly DaemonPenInputSource? _penInput;
    [ObservableProperty] private double? _livePressure;

    // DeviceReportSample already normalizes pressure to 0..1; show the dot only while the pen is down.
    private void OnPenSample(PenSample s) => LivePressure = s.IsDown ? Clamp01(s.Pressure) : null;

    public void StartLivePressure() => _ = _penInput?.StartAsync();

    public void StopLivePressure()
    {
        LivePressure = null;
        _ = _penInput?.StopAsync();
    }

    /// <summary>Debounce rapid edits (node drags / slider) into a single daemon apply.</summary>
    private void SchedulePersist()
    {
        if (_skipCurvePersist || _applyAction == null || _settings == null) return;
        _persistCts?.Cancel();
        var cts = _persistCts = new CancellationTokenSource();
        _ = DebounceAsync(cts.Token);

        async Task DebounceAsync(CancellationToken ct)
        {
            try { await Task.Delay(400, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(PersistCurveAsync);
        }
    }

    private async Task PersistCurveAsync()
    {
        if (_applyAction == null || _settings == null) return;
        var dynamics = new PenDynamicsSettings(Curve, PressureSmoothing, PositionSmoothing, SmoothAfterCurve);
        PressureCurveProfile.Write(_settings, _profile.Tablet ?? "", dynamics, PressureCurveEnabled);
        await _applyAction(_settings);
    }
}

public class ButtonBinding
{
    public int Index { get; set; }
    public string Name { get; set; } = "None";
    public string Label => $"Button {Index}";
}
