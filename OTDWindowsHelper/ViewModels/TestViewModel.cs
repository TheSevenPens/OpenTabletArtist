using System;
using System.Linq;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

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
    }

    private void OnDataLoaded()
    {
        RecomputeMapping();
        RefreshTabletStatus();
    }

    // --- Tablet status banner (#128/#129/#130) ---

    /// <summary>A tablet is currently detected (drives the banner's green check vs. amber warning).</summary>
    [ObservableProperty] private bool _tabletDetected;
    /// <summary>Banner text: the detected tablet's name, or a "no tablet" message.</summary>
    [ObservableProperty] private string _tabletStatusText = "No tablet detected";
    /// <summary>Pen Dynamics is enabled on the detected tablet's profile (shows the "Dynamics on" chip).</summary>
    [ObservableProperty] private bool _dynamicsActive;

    private void RefreshTabletStatus()
    {
        var detected = _deviceData.Profiles.FirstOrDefault(p => p.IsDetected);
        TabletDetected = detected != null;
        TabletStatusText = detected != null
            ? (string.IsNullOrEmpty(detected.Profile.Tablet) ? "Tablet detected" : detected.Profile.Tablet)
            : "No tablet detected";
        // Dynamics is "on" when the OtdWindowsHelper.Dynamics filter is present AND enabled on the
        // profile. Accept the pre-rename legacy path too, mirroring PressureCurveProfile.FindStore.
        DynamicsActive = detected?.Profile.Filters
            ?.Any(f => f.Enable && (f.Path == PressureCurveProfile.FilterTypeName
                                    || f.Path == PressureCurveProfile.LegacyFilterTypeName)) ?? false;
        // The detected tablet's configured dynamics (curve + smoothing) — used to render the
        // "With dynamics" side of the split canvas (#134). Default = identity (no visible change).
        CurrentDynamics = PressureCurveProfile.ReadProfile(detected?.Profile)?.Dynamics
                          ?? PenDynamicsSettings.Default;
    }

    // --- Split canvas: compare raw vs dynamics-processed (#134). Off by default. ---

    [ObservableProperty] private bool _showSplit;
    [ObservableProperty] private PenDynamicsSettings _currentDynamics = PenDynamicsSettings.Default;

    /// <summary>UniformGrid column count for the canvas area: 1 normally, 2 when comparing.</summary>
    public int SplitColumnCount => ShowSplit ? 2 : 1;
    partial void OnShowSplitChanged(bool value) => OnPropertyChanged(nameof(SplitColumnCount));

    /// <summary>Open the tablet's settings dialog straight to the Dynamics tab without leaving Test —
    /// targets the detected tablet, falling back to the first known profile.</summary>
    [RelayCommand]
    private async Task OpenDynamics()
    {
        var profile = (_deviceData.Profiles.FirstOrDefault(p => p.IsDetected)
                       ?? _deviceData.Profiles.FirstOrDefault())?.Profile;
        if (profile != null)
            await _dialogs.ShowTabletSettingsAsync(profile, openDynamics: true);
    }

    /// <summary>false = App input (Windows Ink pointer); true = Driver input (OTD DeviceReport).</summary>
    [ObservableProperty] private bool _useDriverInput;

    [ObservableProperty] private PenBrushMode _brushMode = PenBrushMode.PressureToSize;
    public Array BrushModes { get; } = Enum.GetValues(typeof(PenBrushMode));

    [ObservableProperty] private string _canvasXText = "—";
    [ObservableProperty] private string _canvasYText = "—";
    [ObservableProperty] private string _rawXText = "—";
    [ObservableProperty] private string _rawYText = "—";
    [ObservableProperty] private string _pressureText = "—";
    [ObservableProperty] private string _tiltXText = "—";
    [ObservableProperty] private string _tiltYText = "—";
    [ObservableProperty] private string _azimuthText = "—";
    [ObservableProperty] private string _altitudeText = "—";
    [ObservableProperty] private string _twistText = "—";

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
        return profiles.FirstOrDefault(p => p.Profile.Tablet == _deviceData.TabletName)?.Profile
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
        if (!_active) return;
        _ = value ? _driver.StartAsync() : _driver.StopAsync();
    }

    public void Dispose()
    {
        _deviceData.DataLoaded -= OnDataLoaded;
        _driver.Sample -= OnDriverSample;
        _ = _driver.StopAsync();
    }
}
