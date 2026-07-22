using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Test page — a paint canvas for verifying pen features (pressure, tilt,
/// twist) live. The canvas itself (rendering + pointer input) is the <c>PenTestCanvas</c> control;
/// this VM owns the source toggle, the brush mode, the live readouts, and the driver-input source.
///
/// Input source: App (OS pointer / Windows Ink — what an app receives) or Driver (the OTD daemon's
/// DeviceReport stream — the driver's view, works even before Windows Ink is set up). The shell
/// activates/deactivates the page so the daemon debug stream is only on while Test is visible.
/// </summary>
public partial class TestViewModel : ObservableObject, IDisposable
{
    private readonly DaemonPenInputSource _driver;
    private readonly IDeviceData _deviceData;
    private readonly IDialogService _dialogs;
    private bool _active;

    public TestViewModel(IDaemonDebugSession daemon, IDeviceData deviceData, IDialogService dialogs)
    {
        _driver = new DaemonPenInputSource(daemon);
        _driver.Sample += OnDriverSample;
        _deviceData = deviceData;
        _dialogs = dialogs;
        _deviceData.DataLoaded += OnDataLoaded;
        _deviceData.PropertyChanged += OnDeviceDataPropertyChanged;
    }

    private void OnDataLoaded()
    {
        RecomputeMapping();
        RefreshTabletStatus();
        // The connected-tablet set may have changed → rebuild the picker (#190 phase 3).
        RebuildTablets();
    }

    private void OnDeviceDataPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Active tablet switched elsewhere (tray / another page) → re-target this page's view.
        if (e.PropertyName == nameof(IDeviceData.ActiveTabletName))
        {
            RecomputeMapping();
            RefreshTabletStatus();
            SyncSelectionFromActive();
        }
    }

    // --- Active-tablet picker (#190 phase 3): shown only when more than one tablet is connected. Uses the
    //     shared TabletSwitcherBar (dropdown only — no detection chip/refresh here), so the choice items are
    //     TabletChoiceViewModel, the same type the Tablet/Pen pages feed that control. ---

    /// <summary>The currently-connected tablets, as switcher choices. Reconciled in place on each data load.</summary>
    public ObservableCollection<TabletChoiceViewModel> Tablets { get; } = new();

    /// <summary>Only offer the picker when there's a choice to make.</summary>
    public bool ShowTabletPicker => Tablets.Count > 1;

    private TabletChoiceViewModel? _selectedTablet;
    // Last real selection + guards, to shrug off the spurious null the ComboBox writes back when its bound
    // ItemsSource is (re)set during a page crossfade, and to avoid re-activating during a device-driven
    // sync. Mirrors TabletPageViewModel — without this the switcher blanks out on the Scribble page.
    private TabletChoiceViewModel? _lastSelected;
    private bool _restoringSelection;
    private bool _suppressActivate;

    /// <summary>The tablet this page shows; setting it updates the app-wide active tablet.</summary>
    public TabletChoiceViewModel? SelectedTablet
    {
        get => _selectedTablet;
        set
        {
            // The ComboBox writes a spurious null when its ItemsSource is (re)set during a page crossfade;
            // re-assert the last real selection so the switcher doesn't drop to blank.
            if (value == null && !_restoringSelection && Tablets.Count > 0
                && _lastSelected is { } keep && Tablets.Contains(keep))
            {
                _restoringSelection = true;
                SelectedTablet = keep;
                _restoringSelection = false;
                return;
            }
            if (ReferenceEquals(_selectedTablet, value)) return;
            _selectedTablet = value;
            if (value != null) _lastSelected = value;
            OnPropertyChanged();
            if (!_suppressActivate && value != null) _deviceData.SetActiveTablet(value.Name);
        }
    }

    /// <summary>Reconcile the switcher choices with the connected tablets IN PLACE — never Clear()+rebuild,
    /// which momentarily clears the ComboBox's selection and blanks the switcher — then re-point the
    /// selection at the active tablet.</summary>
    private void RebuildTablets()
    {
        var ordered = _deviceData.DetectedTablets.Select(t => t.Name).ToList();
        var wanted = new HashSet<string>(ordered, StringComparer.OrdinalIgnoreCase);
        for (int i = Tablets.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Tablets[i].Name)) Tablets.RemoveAt(i);
        for (int i = 0; i < ordered.Count; i++)
        {
            int at = IndexOfTablet(ordered[i]);
            if (at >= 0) { if (at != i) Tablets.Move(at, i); }
            else Tablets.Insert(i, new TabletChoiceViewModel(ordered[i], isDetected: true));
        }
        SyncSelectionFromActive();
        OnPropertyChanged(nameof(ShowTabletPicker));
    }

    private int IndexOfTablet(string name)
    {
        for (int i = 0; i < Tablets.Count; i++)
            if (string.Equals(Tablets[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Point the selection at the app-wide active tablet (or the first available) without
    /// re-triggering SetActiveTablet, keeping the surviving instance so the ComboBox stays in sync.</summary>
    private void SyncSelectionFromActive()
    {
        var match = Tablets.FirstOrDefault(t => t.Name == _deviceData.ActiveTabletName)
                    ?? Tablets.FirstOrDefault();
        if (ReferenceEquals(match, _selectedTablet)) return;
        _suppressActivate = true;
        SelectedTablet = match;
        _suppressActivate = false;
    }

    // --- Tablet status banner (#128/#129/#130) ---

    /// <summary>A tablet is currently detected (drives the banner's green check vs. amber warning).</summary>
    [ObservableProperty] private bool _tabletDetected;
    /// <summary>Banner text: the detected tablet's name, or a "no tablet" message.</summary>
    [ObservableProperty] private string _tabletStatusText = "No tablet detected";
    /// <summary>Pen Dynamics is enabled on the detected tablet's profile (shows the "Dynamics on" chip).</summary>
    [ObservableProperty] private bool _dynamicsActive;

    // Which parts of the enabled dynamics actually alter the pen — so the Test page can tell the user
    // exactly what's affecting their stroke, not just that "dynamics" is on (#184).
    /// <summary>The pressure curve is bent (non-linear), so it's reshaping pen pressure.</summary>
    [ObservableProperty] private bool _curveActive;
    /// <summary>Pressure smoothing is on.</summary>
    [ObservableProperty] private bool _pressureSmoothingActive;
    /// <summary>Position smoothing is on.</summary>
    [ObservableProperty] private bool _positionSmoothingActive;
    /// <summary>Dynamics is enabled but nothing actually changes the pen (linear curve, no smoothing).</summary>
    [ObservableProperty] private bool _dynamicsNoOp;

    private void RefreshTabletStatus()
    {
        // Prefer the active tablet (when it's detected), else any detected one (#190 phase 3).
        var detected = _deviceData.Profiles.FirstOrDefault(p => p.IsDetected && p.Profile.Tablet == _deviceData.ActiveTabletName)
                       ?? _deviceData.Profiles.FirstOrDefault(p => p.IsDetected);
        TabletDetected = detected != null;
        TabletStatusText = detected != null
            ? (string.IsNullOrEmpty(detected.Profile.Tablet) ? "Tablet detected" : detected.Profile.Tablet)
            : "No tablet detected";

        // Dynamics is "on" when our filter is present AND enabled; then read the actual settings so we
        // can surface which aspects (curve / pressure smoothing / position smoothing) are in effect.
        var read = PressureCurveProfile.ReadProfile(detected?.Profile);
        DynamicsActive = read is { Enabled: true };
        var d = DynamicsActive ? read!.Value.Dynamics : PenDynamicsSettings.Default;
        CurveActive = DynamicsActive && d.CurveShapesPressure;
        PressureSmoothingActive = DynamicsActive && d.HasPressureSmoothing;
        PositionSmoothingActive = DynamicsActive && d.HasPositionSmoothing;
        DynamicsNoOp = DynamicsActive && d.IsNoOp;
    }

    /// <summary>Open a focused Pen Dynamics editor (curve + smoothing only) without leaving Test —
    /// targets the detected tablet, falling back to the first known profile (#133).</summary>
    [RelayCommand]
    private async Task OpenDynamics()
    {
        var profile = (_deviceData.Profiles.FirstOrDefault(p => p.IsDetected && p.Profile.Tablet == _deviceData.ActiveTabletName)
                       ?? _deviceData.Profiles.FirstOrDefault(p => p.IsDetected)
                       ?? _deviceData.Profiles.FirstOrDefault())?.Profile;
        if (profile == null) return;

        // Pointer-only mode draws nothing, so dynamics edits would be invisible. Switch to a pressure
        // view first so the user can actually see the effect of what they're about to tweak (#183).
        if (BrushMode == PenBrushMode.PointerOnly)
            BrushMode = PenBrushMode.PressureToSize;

        await _dialogs.ShowTabletSettingsAsync(profile, dynamicsOnly: true);
    }

    /// <summary>false = App input (Windows Ink pointer); true = Driver input (OTD DeviceReport).</summary>
    [ObservableProperty] private bool _useDriverInput;

    [ObservableProperty] private PenBrushMode _brushMode = PenBrushMode.PressureToSize;
    public Array BrushModes { get; } = Enum.GetValues(typeof(PenBrushMode));

    /// <summary>Pointer-only mode draws nothing, so active dynamics can't be seen — warn while both
    /// are true (the user can switch Mode to a pressure view). Complements the auto-switch on the
    /// Dynamics button (#183).</summary>
    public bool PointerOnlyWithDynamics => BrushMode == PenBrushMode.PointerOnly && DynamicsActive;

    partial void OnBrushModeChanged(PenBrushMode value) => OnPropertyChanged(nameof(PointerOnlyWithDynamics));
    partial void OnDynamicsActiveChanged(bool value) => OnPropertyChanged(nameof(PointerOnlyWithDynamics));

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanvasText))] private string _canvasXText = "—";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanvasText))] private string _canvasYText = "—";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RawText))] private string _rawXText = "—";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RawText))] private string _rawYText = "—";
    [ObservableProperty] private string _pressureText = "—";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TiltText))] private string _tiltXText = "—";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TiltText))] private string _tiltYText = "—";
    [ObservableProperty] private string _azimuthText = "—";
    [ObservableProperty] private string _altitudeText = "—";
    [ObservableProperty] private string _twistText = "—";
    /// <summary>Driver-reported hover height (0–255). Blank in App input (the OS pointer doesn't carry
    /// it); numeric only in Driver input. The readout is always shown so the grid layout is stable.</summary>
    [ObservableProperty] private string _hoverText = "—";

    // X and Y are shown paired in one readout cell ("x, y") to keep the panel compact (#scribble-readouts).
    public string CanvasText => Pair(CanvasXText, CanvasYText);
    public string RawText => Pair(RawXText, RawYText);
    public string TiltText => Pair(TiltXText, TiltYText);
    private static string Pair(string x, string y) => x == "—" && y == "—" ? "—" : $"{x}, {y}";

    /// <summary>Driver-mode samples; the view forwards these to the canvas.</summary>
    public event Action<PenSample>? DriverSample;
    /// <summary>Raised by the Clear command; the view clears the canvas.</summary>
    public event Action? ClearRequested;

    [RelayCommand]
    private void Clear() => ClearRequested?.Invoke();

    /// <summary>Where the stroke is drawn on the canvas (always the pointer position, both modes).</summary>
    public void UpdateCanvasPosition(double x, double y)
    {
        CanvasXText = x.ToString("0.#");
        CanvasYText = y.ToString("0.#");
    }

    /// <summary>Update the source-dependent readouts (raw coords, pressure, tilt) from a sample.</summary>
    public void UpdateReadout(PenSample s)
    {
        RawXText = s.RawX.ToString("0.#");
        RawYText = s.RawY.ToString("0.#");
        PressureText = s.Pressure.ToString("0.000");
        TiltXText = s.TiltX.ToString("0.0") + "°";
        TiltYText = s.TiltY.ToString("0.0") + "°";
        AzimuthText = DiagnosticsMath.TiltAzimuthDegrees(s.TiltX, s.TiltY).ToString("0.0") + "°";
        AltitudeText = DiagnosticsMath.TiltAltitudeDegrees(s.TiltX, s.TiltY).ToString("0.0") + "°";
        TwistText = s.Twist.ToString("0.0") + "°";
        // Only refresh hover when the report actually carried it, so reports without proximity data
        // don't blank out the last-known value mid-hover.
        if (s.HoverDistance is { } hover) HoverText = hover.ToString("0");
    }

    private void OnDriverSample(PenSample s) => DriverSample?.Invoke(s);

    // --- Driver-input position mapping (#95) ---
    // In Driver mode the canvas paints under the pen by mapping the daemon's raw tablet position
    // through OTD's Absolute-mode transform. Only works in an Absolute output mode; in Relative the
    // canvas is disabled with a note.

    private (TabletDigitizerSpec Digi, MappingArea Input, MappingArea Output, bool Clip, bool Limit)? _mapping;

    /// <summary>Driver mode + an Absolute output mode we can map → paint at the mapped position.</summary>
    public bool DriverPositioned => UseDriverInput && _mapping.HasValue;

    /// <summary>Driver mode but no usable Absolute mapping → canvas disabled, show the note.</summary>
    public bool DriverCanvasDisabled => UseDriverInput && !_mapping.HasValue;

    public string DriverDisabledNote =>
        "Driver-input painting needs an Absolute output mode (e.g. Windows Ink Absolute) on the active " +
        "tablet, so the raw pen position can be mapped to the screen. The current mode doesn't map " +
        "position, so the canvas is disabled while in Driver input. Switch to App input, or set this " +
        "tablet to Windows Ink Absolute.";

    /// <summary>Map a raw tablet point to a virtual-desktop pixel, or null if not mappable.</summary>
    public Vector2? MapRawToDesktop(double rawX, double rawY) =>
        _mapping is { } m
            ? AbsolutePositionMapper.MapToDesktop(new Vector2((float)rawX, (float)rawY), m.Digi, m.Input, m.Output, m.Clip, m.Limit)
            : null;

    private void RecomputeMapping()
    {
        _mapping = BuildMapping();
        OnPropertyChanged(nameof(DriverPositioned));
        OnPropertyChanged(nameof(DriverCanvasDisabled));
    }

    private (TabletDigitizerSpec, MappingArea, MappingArea, bool, bool)? BuildMapping()
    {
        var profile = ActiveProfile();
        // Absolute output modes (OTD AbsoluteOutputMode + VoiD's WinInkAbsoluteMode) carry "Absolute"
        // in their type path; Relative modes don't map an absolute position.
        if (profile?.OutputMode?.Path is not { } path ||
            !path.Contains("Absolute", StringComparison.OrdinalIgnoreCase))
            return null;

        var abs = profile.AbsoluteModeSettings;
        if (abs?.Tablet is not { } t || abs.Display is not { } disp) return null;
        if (t.Width <= 0 || t.Height <= 0 || disp.Width <= 0 || disp.Height <= 0) return null;
        if (ReadDigitizer(profile.Tablet) is not { } digi) return null;

        return (digi,
            new MappingArea(t.X, t.Y, t.Width, t.Height, t.Rotation),
            new MappingArea(disp.X, disp.Y, disp.Width, disp.Height),
            abs.EnableClipping, abs.EnableAreaLimiting);
    }

    private Profile? ActiveProfile()
    {
        var profiles = _deviceData.Profiles;
        return profiles.FirstOrDefault(p => p.Profile.Tablet == _deviceData.ActiveTabletName)?.Profile
            ?? profiles.FirstOrDefault(p => p.IsDetected)?.Profile
            ?? profiles.FirstOrDefault()?.Profile;
    }

    private TabletDigitizerSpec? ReadDigitizer(string? tabletName)
    {
        if (_deviceData.Tablets is not JArray tablets || string.IsNullOrEmpty(tabletName)) return null;
        foreach (var tk in tablets)
        {
            var props = tk["Properties"] ?? tk;
            if (props["Name"]?.ToString() != tabletName) continue;
            var d = props["Specifications"]?["Digitizer"];
            if (d == null) return null;
            float w = d["Width"]?.Value<float>() ?? 0, h = d["Height"]?.Value<float>() ?? 0;
            float mx = d["MaxX"]?.Value<float>() ?? 0, my = d["MaxY"]?.Value<float>() ?? 0;
            return w > 0 && h > 0 && mx > 0 && my > 0 ? new TabletDigitizerSpec(w, h, mx, my) : null;
        }
        return null;
    }

    // --- page lifecycle (called by the shell on navigation, like Diagnostics) ---

    public async Task ActivateAsync()
    {
        _active = true;
        RecomputeMapping(); // data may already be loaded before the page is shown
        RefreshTabletStatus();
        if (UseDriverInput) await _driver.StartAsync();
    }

    public async Task DeactivateAsync()
    {
        _active = false;
        await _driver.StopAsync();
    }

    partial void OnUseDriverInputChanged(bool value)
    {
        // The position-source state depends on the toggle.
        OnPropertyChanged(nameof(DriverPositioned));
        OnPropertyChanged(nameof(DriverCanvasDisabled));
        HoverText = "—"; // hover is driver-only; blank it in App input and don't carry a stale value across a switch
        if (!_active) return;
        _ = value ? _driver.StartAsync() : _driver.StopAsync();
    }

    public void Dispose()
    {
        _deviceData.DataLoaded -= OnDataLoaded;
        _deviceData.PropertyChanged -= OnDeviceDataPropertyChanged;
        _driver.Sample -= OnDriverSample;
        _ = _driver.StopAsync();
    }
}
