using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for a single tablet's settings — the tabbed editor (Screen Mapping, Pen Switches,
/// ExpressKeys, Dynamics, Hover, Filters, JSON). Hosted either as an in-app page (the Tablets nav)
/// or in the focused Pen Dynamics dialog the tray opens. Delegate-based (apply / refresh / detection
/// probe / live pen input / calibrate) so the host wires it to the session.
/// </summary>
public partial class TabletDetailViewModel : ObservableObject
{
    private const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string WinInkRelativeModePath = "VoiDPlugins.OutputMode.WinInkRelativeMode";

    private Profile _profile;
    private Settings? _settings;
    private readonly Func<Settings, Task>? _applyAction;
    // Returns the freshly-reloaded settings together with this tablet's profile from within them, so
    // the VM can keep _settings and _profile coherent (the profile is a reference inside the settings).
    private readonly Func<Task<(Settings? Settings, Profile? Profile)>>? _refreshAction;
    // Probes whether this tablet is currently detected/connected (re-checked on open and on Refresh).
    private readonly Func<bool>? _isDetectedProbe;
    private readonly (float Width, float Height)? _tabletDigitizer;

    [ObservableProperty] private IReadOnlyList<DisplayInfo> _displays = [];
    [ObservableProperty] private int? _selectedDisplayNumber;

    /// <summary>A display is selected, so "Apply" can map to it.</summary>
    public bool CanApplyDisplay => SelectedDisplayNumber != null && _applyAction != null;

    /// <summary>The selected display differs from the one currently applied, so the change still needs
    /// "Apply mapping" — drives the pending hint so it's obvious the selection isn't live yet (#179
    /// follow-up). Suppressed during the initial load so an as-opened profile doesn't read as pending
    /// before the user changes anything.</summary>
    [ObservableProperty] private bool _mappingChangePending;
    private bool _suppressMappingPending;

    private void RecomputeMappingPending() =>
        MappingChangePending = _applyAction != null
            && SelectedDisplayNumber != null
            && SelectedDisplayNumber != CurrentlyMappedNumber();

    partial void OnSelectedDisplayNumberChanged(int? value)
    {
        OnPropertyChanged(nameof(CanApplyDisplay));
        ApplyDisplayCommand.NotifyCanExecuteChanged();
        if (!_suppressMappingPending) RecomputeMappingPending();
    }

    partial void OnIsWinInkAbsoluteChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAbsoluteMode)); // keep the Absolute/Relative toggle in sync
        OnPropertyChanged(nameof(CanCalibrate));   // calibration needs an Absolute mode (#127)
        OnPropertyChanged(nameof(CanRunCalibration));
        OnPropertyChanged(nameof(ShowConnectToCalibrateHint));
        if (!_skipOutputModeChange && value)
            _ = SetOutputMode(WinInkAbsoluteModePath);
    }

    // --- Pointer calibration entry point (#127) ---

    /// <summary>Calibration corrects an Absolute mapping, so the calibration UI is only shown in an
    /// Absolute mode. (Whether it can actually run also needs a live tablet — see
    /// <see cref="CanRunCalibration"/>.)</summary>
    public bool CanCalibrate => IsWinInkAbsolute;

    /// <summary>Calibration captures live pen taps, so it additionally needs the tablet connected
    /// (#177). Gates the Calibrate button so it can't be clicked into the "not detected" dead-end,
    /// and flips live as the tablet is (un)plugged while the page stays open.</summary>
    public bool CanRunCalibration => CanCalibrate && IsTabletDetected;

    /// <summary>In an Absolute mode but the tablet isn't connected, so calibration is shown but
    /// disabled — prompt the user to connect it (#177).</summary>
    public bool ShowConnectToCalibrateHint => CanCalibrate && !IsTabletDetected;

    /// <summary>Calibration capture presets: the current corner method (→ homography, #195) or a finer
    /// grid (→ bilinear offsets, #196). The user picks one before calibrating.</summary>
    public IReadOnlyList<CalibrationModeChoice> CalibrationModeChoices { get; } = new List<CalibrationModeChoice>
    {
        new("4 point", CalibrationMode.Corners, 0, 0),
        new("9 point", CalibrationMode.Grid, 3, 3),
        new("25 point", CalibrationMode.Grid, 5, 5),
    };

    [ObservableProperty] private CalibrationModeChoice _selectedCalibrationMode;

    /// <summary>The capture options for the current selection (used when opening the overlay).</summary>
    public CalibrationOptions CalibrationOptions => SelectedCalibrationMode.ToOptions();

    /// <summary>Raised when the user clicks Calibrate; the host opens the overlay.</summary>
    public event Action? CalibrationRequested;

    [RelayCommand]
    private void Calibrate() => CalibrationRequested?.Invoke();

    /// <summary>True when a calibration exists but was captured against a different area mapping than
    /// the current one — it may no longer be accurate, so suggest recalibrating (#147).</summary>
    [ObservableProperty] private bool _calibrationStale;

    /// <summary>An enabled calibration is active on this profile (drives the status indicator).</summary>
    [ObservableProperty] private bool _isCalibrated;
    /// <summary>Human-readable calibration state incl. the mode used ("Not calibrated" / "Calibrated — …").</summary>
    [ObservableProperty] private string _calibrationStatusText = "Not calibrated";

    public bool CanClearCalibration => IsCalibrated && _applyAction != null;
    partial void OnIsCalibratedChanged(bool value) => OnPropertyChanged(nameof(CanClearCalibration));

    private void RefreshCalibrationStatus()
    {
        var cal = CalibrationProfile.ReadProfile(_profile);
        CalibrationStale = CalibrationProfile.IsStale(cal, CurrentMappingFingerprint());
        IsCalibrated = cal is { Enabled: true };
        CalibrationStatusText = !IsCalibrated
            ? "Not calibrated"
            : cal!.Model switch
            {
                CalibrationProfile.CalibrationModel.Homography => "Calibrated — 4 point (perspective)",
                CalibrationProfile.CalibrationModel.Grid => $"Calibrated — {(cal.Grid?.Cols ?? 0) * (cal.Grid?.Rows ?? 0)} point",
                _ => "Calibrated — 4 point (legacy)",
            };
    }

    /// <summary>Remove the calibration filter, returning the pointer to its uncorrected default.</summary>
    [RelayCommand]
    private async Task ClearCalibration()
        => await ApplySettingsChange(p => CalibrationProfile.Clear(_settings, p.Tablet ?? ""));

    /// <summary>Fingerprint of the profile's current Absolute mapping (input + output area + mapped
    /// display), matching how <see cref="CalibrationProfile.Fingerprint"/> was written at calibration.</summary>
    private string? CurrentMappingFingerprint()
    {
        var abs = _profile.AbsoluteModeSettings;
        if (abs?.Tablet is not { } t || abs.Display is not { } d || CurrentlyMappedNumber() is not { } num)
            return null;
        var input = new MappingArea(t.X, t.Y, t.Width, t.Height, t.Rotation);
        var output = new MappingArea(d.X, d.Y, d.Width, d.Height);
        return CalibrationProfile.Fingerprint(input, output, num);
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

    public TabletDetailViewModel(Profile profile, Settings? settings,
        Func<Settings, Task>? applyAction = null,
        Func<Task<(Settings? Settings, Profile? Profile)>>? refreshAction = null,
        (float Width, float Height)? tabletDigitizer = null,
        IDaemonDebugSession? penInput = null,
        Func<bool>? isDetected = null,
        bool dynamicsOnly = false)
    {
        _profile = profile;
        _settings = settings;
        _applyAction = applyAction;
        _refreshAction = refreshAction;
        _isDetectedProbe = isDetected;
        _tabletDigitizer = tabletDigitizer;
        DynamicsOnly = dynamicsOnly;
        SelectedCalibrationMode = CalibrationModeChoices[0]; // default: Corners

        if (penInput != null)
        {
            _penInput = new DaemonPenInputSource(penInput);
            _penInput.Sample += OnPenSample;
        }

        TabletName = profile.Tablet ?? "Unknown Tablet";
        HasAreaMapping = profile.AbsoluteModeSettings != null;

        Displays = DisplayEnumerator.Enumerate();
        RefreshFromProfile();
        RefreshDetectionStatus();
        // Highlight the display the tablet is currently mapped to (else the primary). Suppress the
        // pending flag for this initial, programmatic selection so it doesn't open "pending".
        _suppressMappingPending = true;
        SelectedDisplayNumber = CurrentlyMappedNumber()
            ?? Displays.FirstOrDefault(d => d.IsPrimary)?.Number
            ?? Displays.FirstOrDefault()?.Number;
        _suppressMappingPending = false;
    }

    // Parameterless constructor for design-time
    public TabletDetailViewModel()
    {
        _profile = new Profile();
        TabletName = "Design Tablet";
        Displays = [];
        SelectedCalibrationMode = CalibrationModeChoices[0];
    }

    private static string? GetPluginFriendlyName(string? path) =>
        path == null ? null : AppInfo.PluginManager.GetFriendlyName(path);

    private static string GetBindingName(PluginSettingStore? store) =>
        PenSwitchBinding.GetBindingLabel(store, GetPluginFriendlyName);

    /// <summary>Set when an in-page Refresh finds this tablet's profile gone (unplugged/removed since
    /// it was opened); surfaced as a header warning. Cleared on a successful refresh.
    /// (#124 / Cursor review on #125)</summary>
    [ObservableProperty] private string? _refreshWarning;

    /// <summary>When true, this is the focused Pen Dynamics editor: the tab bar is hidden and only the
    /// Dynamics content shows (#133). The Dynamics tab is preselected by the view.</summary>
    [ObservableProperty] private bool _dynamicsOnly;

    // --- Tablet detected/connected banner (#132) ---

    /// <summary>True when this tablet is the currently-connected one (green check vs. amber warning).</summary>
    [ObservableProperty] private bool _isTabletDetected;
    /// <summary>Banner text describing the detection state.</summary>
    [ObservableProperty] private string _detectionText = "";

    partial void OnIsTabletDetectedChanged(bool value)
    {
        // Tablet-dependent actions follow the live detection state (#177).
        OnPropertyChanged(nameof(CanRunCalibration));
        OnPropertyChanged(nameof(ShowConnectToCalibrateHint));
    }

    /// <summary>Re-evaluate the detection banner + tablet-dependent actions from the current session
    /// state. Called on open, on manual Refresh, and live whenever the daemon reports a tablet
    /// add/remove while the page stays open (#177, driven by the #170 TabletsChanged signal).</summary>
    public void RefreshDetectionStatus()
    {
        IsTabletDetected = _isDetectedProbe?.Invoke() ?? false;
        DetectionText = IsTabletDetected
            ? "Connected"
            : "Not currently connected — showing this tablet's saved settings.";
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (_refreshAction == null) return;
        var (settings, profile) = await _refreshAction();
        if (profile == null)
        {
            // The tablet/profile is gone (unplugged or removed since it was opened). Keep showing the
            // last-known data rather than blanking it, but warn it may be stale.
            RefreshWarning = "This tablet is no longer connected — showing the last known settings.";
            RefreshDetectionStatus();
            return;
        }

        // Reassign BOTH so later edits write to and push the same settings object the profile
        // lives in — otherwise persists would mutate stale settings (#124).
        _settings = settings;
        _profile = profile;
        // The profile must be a live reference inside the settings we now persist through; if a
        // future refresh source returns a detached profile, edits would silently write elsewhere.
        Debug.Assert(settings?.Profiles.Contains(profile) == true,
            "Refreshed profile must be a reference inside the refreshed settings (#124).");
        RefreshWarning = null;
        RefreshFromProfile();
        RefreshDetectionStatus();
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

        // Bindings — pen switches (tip, eraser, barrel buttons)
        var bindings = _profile.BindingSettings;
        RefreshPenSwitchRows();

        // ExpressKeys (read-only)
        AuxButtonCount = bindings.AuxButtons.Count.ToString();

        var newAuxButtons = new List<ButtonBinding>();
        for (int i = 0; i < bindings.AuxButtons.Count; i++)
            newAuxButtons.Add(new ButtonBinding { Index = i + 1, Name = GetBindingName(bindings.AuxButtons[i]) });
        AuxButtons = newAuxButtons;
        NoAuxButtons = newAuxButtons.Count == 0;

        // Filters + raw JSON view (also refreshed after a dynamics toggle/edit persists, so the
        // Filters tab reflects the DynamicsFilter's enabled state without a manual Refresh).
        UpdateFiltersDisplay();

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

        // Hover limit (#188) — load without triggering a persist.
        var hover = HoverProfile.Read(_settings, _profile.Tablet ?? "");
        _skipHoverPersist = true;
        MaxHoverDistance = hover?.MaxHoverDistance ?? DefaultMaxHoverDistance;
        HoverLimitEnabled = hover?.Enabled ?? false;
        _skipHoverPersist = false;
        CanEditHover = _applyAction != null;

        RefreshCalibrationStatus();
    }

    /// <summary>Recomputes the Filters-tab list and the raw-JSON view from the current
    /// <see cref="_profile"/>. Called on a full refresh and again after a dynamics edit persists,
    /// so the Filters tab tracks the DynamicsFilter's enabled state without a manual Refresh.</summary>
    private void UpdateFiltersDisplay()
    {
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

        // Same mapping the tray's "Switch display" uses — aspect-locked, full-monitor (#187).
        await ApplySettingsChange(p => DisplayMappingApplier.ApplyToProfile(p, _tabletDigitizer, display));
        MappingChangePending = false; // the selection is now the applied mapping
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
    private int? CurrentlyMappedNumber() => DisplayMappingApplier.CurrentlyMapped(_profile, Displays)?.Number;

    private void RefreshPenSwitchRows()
    {
        var bindings = _profile.BindingSettings;
        var canEdit = _applyAction != null;
        var rows = new List<PenSwitchRowViewModel>
        {
            new(PenSwitchKind.Tip, 1, bindings.TipButton, canEdit, ApplyPenSwitchBindingAsync),
            new(PenSwitchKind.Eraser, 1, bindings.EraserButton, canEdit, ApplyPenSwitchBindingAsync),
        };
        for (int i = 0; i < bindings.PenButtons.Count; i++)
            rows.Add(new(PenSwitchKind.PenButton, i + 1, bindings.PenButtons[i], canEdit, ApplyPenSwitchBindingAsync));
        if (rows.Count > 0) rows[0].IsFirst = true; // suppresses the leading divider in the merged card
        PenSwitchRows = rows;
    }

    private async Task ApplyPenSwitchBindingAsync(PenSwitchKind kind, int penButtonIndex, PluginSettingStore store)
    {
        await ApplySettingsChange(p =>
        {
            switch (kind)
            {
                case PenSwitchKind.Tip:
                    p.BindingSettings.TipButton = store;
                    break;
                case PenSwitchKind.Eraser:
                    p.BindingSettings.EraserButton = store;
                    break;
                case PenSwitchKind.PenButton:
                    if (penButtonIndex >= 1 && penButtonIndex <= p.BindingSettings.PenButtons.Count)
                        p.BindingSettings.PenButtons[penButtonIndex - 1] = store;
                    break;
            }
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
    [ObservableProperty] private List<PenSwitchRowViewModel> _penSwitchRows = [];
    [ObservableProperty] private string _auxButtonCount = "0";
    [ObservableProperty] private bool _noAuxButtons;
    [ObservableProperty] private List<ButtonBinding> _auxButtons = [];
    [ObservableProperty] private string _filtersText = "";
    [ObservableProperty] private string _rawJson = "";

    // ── Pressure curve tab ──────────────────────────────────────

    [ObservableProperty] private PressureCurveSettings _curve = PressureCurveSettings.Default;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DynamicsControlsOpacity))]
    private bool _pressureCurveEnabled;
    [ObservableProperty] private bool _canEditPressure;

    /// <summary>Dim the curve/smoothing controls when Pen Dynamics is off so they read as inactive.
    /// <c>IsEnabled</c> alone barely greys them, and the custom-drawn curve chart ignores
    /// <c>IsEnabled</c> entirely, so without this it would still look fully interactive.</summary>
    public double DynamicsControlsOpacity => PressureCurveEnabled ? 1.0 : 0.4;

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

    // Read-only display of the node values (#131). Editing is via dragging the chart nodes; these
    // just show where the pink (min) / cyan (max) nodes currently sit (input → output).
    public string InputMinimumText => Curve.InputMinimum.ToString("0.00");
    public string OutputMinimumText => Curve.Minimum.ToString("0.00");
    public string InputMaximumText => Curve.InputMaximum.ToString("0.00");
    public string OutputMaximumText => Curve.Maximum.ToString("0.00");

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    partial void OnCurveChanged(PressureCurveSettings value)
    {
        OnPropertyChanged(nameof(Softness));
        OnPropertyChanged(nameof(CutBelowMinimum));
        OnPropertyChanged(nameof(SoftnessText));
        OnPropertyChanged(nameof(InputMinimumText));
        OnPropertyChanged(nameof(InputMaximumText));
        OnPropertyChanged(nameof(OutputMinimumText));
        OnPropertyChanged(nameof(OutputMaximumText));
        SchedulePersist();
    }

    partial void OnPressureCurveEnabledChanged(bool value) => SchedulePersist();

    [RelayCommand]
    private void ResetCurve() => Curve = PressureCurveSettings.Default;

    /// <summary>Reset every dynamics setting — the curve, both smoothing amounts, and the order —
    /// back to their no-op defaults (#185). Each setter schedules the debounced persist, so the
    /// cleared state is written to the daemon. Leaves the On/Off toggle as the user set it.</summary>
    [RelayCommand]
    private void ResetAllDynamics()
    {
        Curve = PressureCurveSettings.Default;
        PressureSmoothing = 0;
        PositionSmoothing = 0;
        SmoothAfterCurve = true;
    }

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
        // The write mutated _profile.Filters (added/enabled/disabled the DynamicsFilter); reflect that
        // in the Filters tab and JSON view immediately rather than waiting for a manual Refresh.
        UpdateFiltersDisplay();
        await _applyAction(_settings);
    }

    // ── Hover limit tab (#188) ──────────────────────────────────

    public const int DefaultMaxHoverDistance = 127; // sensible mid-point when first enabled (range 0-255)
    public int HoverDistanceMax => HoverProfile.MaxDistance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoverControlsOpacity))]
    private bool _hoverLimitEnabled;
    [ObservableProperty] private bool _canEditHover;
    [ObservableProperty] private double _maxHoverDistance = DefaultMaxHoverDistance;

    public string MaxHoverDistanceText => ((int)MaxHoverDistance).ToString();
    /// <summary>Dim the hover controls when the limit is off so they read as inactive (like Dynamics).</summary>
    public double HoverControlsOpacity => HoverLimitEnabled ? 1.0 : 0.4;

    private bool _skipHoverPersist;
    private CancellationTokenSource? _hoverPersistCts;

    partial void OnHoverLimitEnabledChanged(bool value) => SchedulePersistHover();

    partial void OnMaxHoverDistanceChanged(double value)
    {
        OnPropertyChanged(nameof(MaxHoverDistanceText));
        SchedulePersistHover();
    }

    private void SchedulePersistHover()
    {
        if (_skipHoverPersist || _applyAction == null || _settings == null) return;
        _hoverPersistCts?.Cancel();
        var cts = _hoverPersistCts = new CancellationTokenSource();
        _ = DebounceAsync(cts.Token);

        async Task DebounceAsync(CancellationToken ct)
        {
            try { await Task.Delay(400, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(PersistHoverAsync);
        }
    }

    private async Task PersistHoverAsync()
    {
        if (_applyAction == null || _settings == null) return;
        HoverProfile.Write(_settings, _profile.Tablet ?? "", (int)MaxHoverDistance, HoverLimitEnabled);
        UpdateFiltersDisplay();
        await _applyAction(_settings);
    }
}

public partial class PenSwitchRowViewModel : ObservableObject
{
    private readonly PenSwitchKind _kind;
    private readonly int _penButtonIndex;
    private readonly Func<PenSwitchKind, int, PluginSettingStore, Task> _applyAsync;

    public PenSwitchRowViewModel(
        PenSwitchKind kind,
        int penButtonIndex,
        PluginSettingStore? store,
        bool canEdit,
        Func<PenSwitchKind, int, PluginSettingStore, Task> applyAsync)
    {
        _kind = kind;
        _penButtonIndex = penButtonIndex;
        _applyAsync = applyAsync;
        SectionLabel = kind switch
        {
            PenSwitchKind.Tip => "PEN TIP",
            PenSwitchKind.Eraser => "ERASER",
            PenSwitchKind.PenButton => $"BUTTON {penButtonIndex}",
            _ => "SWITCH"
        };
        RefreshFromStore(store, canEdit);
    }

    public string SectionLabel { get; }

    /// <summary>First row in the merged Pen Switches card — its separating divider is hidden.</summary>
    public bool IsFirst { get; set; }

    [ObservableProperty] private string _bindingLabel = "None";
    [ObservableProperty] private PenSwitchBindingMode _mode;
    [ObservableProperty] private bool _canEdit;

    public bool IsAutoSelected => Mode == PenSwitchBindingMode.Auto;
    public bool IsLegacySelected => Mode == PenSwitchBindingMode.Legacy;
    public bool IsOtherSelected => Mode == PenSwitchBindingMode.Other;
    public bool IsRecommended => Mode == PenSwitchBindingMode.Auto;
    public bool IsNotRecommended => Mode != PenSwitchBindingMode.Auto;

    partial void OnModeChanged(PenSwitchBindingMode value)
    {
        OnPropertyChanged(nameof(IsAutoSelected));
        OnPropertyChanged(nameof(IsLegacySelected));
        OnPropertyChanged(nameof(IsOtherSelected));
        OnPropertyChanged(nameof(IsRecommended));
        OnPropertyChanged(nameof(IsNotRecommended));
        SetAutoCommand.NotifyCanExecuteChanged();
        SetLegacyCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanEditChanged(bool value)
    {
        SetAutoCommand.NotifyCanExecuteChanged();
        SetLegacyCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFromStore(PluginSettingStore? store, bool canEdit)
    {
        CanEdit = canEdit;
        Mode = PenSwitchBinding.DetectMode(store, _kind, _penButtonIndex);
        BindingLabel = PenSwitchBinding.GetDisplayLabel(store, _kind, _penButtonIndex, GetPluginFriendlyName);
    }

    private static string? GetPluginFriendlyName(string? path) =>
        path == null ? null : AppInfo.PluginManager.GetFriendlyName(path);

    private bool CanSetAuto => CanEdit && Mode != PenSwitchBindingMode.Auto;
    private bool CanSetLegacy => CanEdit && Mode != PenSwitchBindingMode.Legacy;

    [RelayCommand(CanExecute = nameof(CanSetAuto))]
    private Task SetAuto() =>
        _applyAsync(_kind, _penButtonIndex, PenSwitchBinding.MakeAdaptiveBinding(_kind, _penButtonIndex));

    [RelayCommand(CanExecute = nameof(CanSetLegacy))]
    private Task SetLegacy() =>
        _applyAsync(_kind, _penButtonIndex, PenSwitchBinding.MakeLegacyBinding(_kind));
}

public class ButtonBinding
{
    public int Index { get; set; }
    public string Name { get; set; } = "None";
    public string Label => $"Button {Index}";
}
