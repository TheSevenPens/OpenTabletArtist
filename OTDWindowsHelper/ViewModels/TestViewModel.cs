using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    }

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

    // --- page lifecycle (called by the shell on navigation, like Diagnostics) ---

    public async Task ActivateAsync()
    {
        _active = true;
        if (UseDriverInput) await _driver.StartAsync();
    }

    public async Task DeactivateAsync()
    {
        _active = false;
        await _driver.StopAsync();
    }

    partial void OnUseDriverInputChanged(bool value)
    {
        if (!_active) return;
        _ = value ? _driver.StartAsync() : _driver.StopAsync();
    }

    public void Dispose()
    {
        _driver.Sample -= OnDriverSample;
        _ = _driver.StopAsync();
    }
}
