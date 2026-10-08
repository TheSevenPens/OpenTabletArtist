using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OtdInterop;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Test page — a paint canvas for verifying pen features (pressure, tilt,
/// twist) live. The canvas itself (rendering + pointer input) is the <c>PenTestCanvas</c> control;
/// this VM owns the brush mode, the live readouts, and the driver-input source.
///
/// The pen data always comes from the driver — the OTD daemon's DeviceReport stream, its own view of
/// the pen before the OS sees it. A toggle used to offer the OS pointer (what an app receives) as a
/// second source; it was removed (#scribble-driver-only) because no other tablet driver's UI offers
/// that choice and neither of its two words means anything to someone who just wants to test a pen.
/// The shell activates/deactivates the page so the daemon debug stream is only on while Test is visible.
/// </summary>
public partial class TestViewModel : ObservableObject, IDisposable
{
    private readonly DaemonPenInputSource _driver;
    private readonly IDeviceData _deviceData;

    /// <param name="daemonVersion">The connected daemon's version, for the driver line of a saved recording.</param>
    /// <param name="showRecordingReview">Opens the save window when a recording is stopped.</param>
    /// <param name="showRecordingProblem">Says why Record could not start, in a message of its own.</param>
    public TestViewModel(
        IDaemonDebugSession daemon,
        IDeviceData deviceData,
        Func<string>? daemonVersion = null,
        Func<StrokeRecordingViewModel, Task>? showRecordingReview = null,
        Func<string, Task>? showRecordingProblem = null)
    {
        _driver = new DaemonPenInputSource(daemon);
        _driver.Sample += OnDriverSample;
        _driver.CountSamples = CountersEnabled; // the initializer doesn't go through OnCountersEnabledChanged
        _deviceData = deviceData;
        Recording = new StrokeRecordingViewModel(
            () => StrokeRecordingContext.From(_deviceData.Tablets, DetectedProfile()?.Profile.Tablet, daemonVersion?.Invoke()),
            _driver.SetTap,
            showRecordingReview,
            showRecordingProblem);
        _deviceData.DataLoaded += OnDataLoaded;
        _deviceData.PropertyChanged += OnDeviceDataPropertyChanged;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshMs) };
        _refreshTimer.Tick += OnRefreshTick;
    }

    private void OnDataLoaded()
    {
        RecomputeMapping();
        RefreshTabletStatus();
    }

    private void OnDeviceDataPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Active tablet switched elsewhere (tray / another page) → re-target this page's view.
        if (e.PropertyName == nameof(IDeviceData.ActiveTabletName))
        {
            RecomputeMapping();
            RefreshTabletStatus();
        }
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

    /// <summary>The tablet this page follows: the active one when it's detected, else any detected one (#190 phase 3).</summary>
    private ProfileItem? DetectedProfile() =>
        _deviceData.Profiles.FirstOrDefault(p => p.IsDetected && p.Profile.Tablet == _deviceData.ActiveTabletName)
        ?? _deviceData.Profiles.FirstOrDefault(p => p.IsDetected);

    private void RefreshTabletStatus()
    {
        var detected = DetectedProfile();
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

    [ObservableProperty] private PenBrushMode _brushMode = PenBrushMode.PressureToSize;
    public Array BrushModes { get; } = Enum.GetValues(typeof(PenBrushMode));

    /// <summary>Mark every pen report that lands ink with a red dot (for judging strokes at high report rates).
    /// Off by default.</summary>
    [ObservableProperty] private bool _showReportDots;

    /// <summary>Record mode: keep every pen report and save them as a Stroke Corpus recording. It is fed from the
    /// daemon's receive thread, not from <see cref="UpdateReadout"/>; see <see cref="StrokeRecordingViewModel"/>.</summary>
    public StrokeRecordingViewModel Recording { get; }

    /// <summary>Pointer-only mode draws nothing, so active dynamics can't be seen — warn while both
    /// are true (the user can switch Mode to a pressure view). (#183)</summary>
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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AzAltText))] private string _azimuthText = "—";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AzAltText))] private string _altitudeText = "—";
    [ObservableProperty] private string _twistText = "—";
    /// <summary>Driver-reported hover height (0–255), or "—" before the pen is in range.</summary>
    [ObservableProperty] private string _hoverText = "—";
    /// <summary>Tablet reports per second while the pen is drawing or hovering, or "—" before any.</summary>
    [ObservableProperty] private string _rateText = "—";

    private readonly ReportRateMeter _rateMeter = new();

    // --- Readout refresh ---
    // Reports can arrive at 1000 Hz, which would set every readout ~1000×/s — a blur of digits and a pile of
    // binding work. So the per-report path only stashes the latest values (cheap); a ~20 Hz timer formats and
    // publishes them. The canvas is unaffected: it still gets every sample straight from DriverSample.
    private const int RefreshMs = 50;
    private const int RateEveryTicks = 3; // ≈7 rate refreshes a second — readable, still responsive

    /// <summary>No report for this long means the pen has left range (tablets report continuously while the
    /// pen is in range, even held still), so the readouts go back to "—". Same as the rate meter's pause.</summary>
    private const double OutOfRangeMs = ReportRateMeter.PauseThresholdMs;

    private readonly DispatcherTimer _refreshTimer;
    private int _ticks;
    private long _lastReportTicks;
    private PenSample _latest;
    private bool _hasSample;
    private int? _latestHover;
    private (double X, double Y)? _latestCanvas;
    private bool _readoutDirty;

    // X and Y are shown paired in one readout cell ("x, y") to keep the panel compact (#scribble-readouts).
    public string CanvasText => Pair(CanvasXText, CanvasYText);
    public string RawText => Pair(RawXText, RawYText);
    public string TiltText => Pair(TiltXText, TiltYText);
    public string AzAltText => Pair(AzimuthText, AltitudeText);
    private static string Pair(string x, string y) => x == "—" && y == "—" ? "—" : $"{x}, {y}";

    /// <summary>Driver-mode samples; the view forwards these to the canvas.</summary>
    public event Action<PenSample>? DriverSample;
    /// <summary>Raised by the Clear command; the view clears the canvas.</summary>
    public event Action? ClearRequested;

    [RelayCommand]
    private void Clear()
    {
        SaveRecording();
        ResetCounters();
        ClearRequested?.Invoke();
    }

    // --- Report recording (opt-in) ---
    // Set OTA_SCRIBBLE_RECORD to a file path and every report is kept until Clear (or leaving the page), when
    // they're written there as CSV — for working out, offline, how fast position / pressure really update.
    // Unset (the default): no recorder exists and the per-report path does nothing for it.
    private readonly ReportRecorder? _recorder =
        Environment.GetEnvironmentVariable("OTA_SCRIBBLE_RECORD") is { Length: > 0 } ? new ReportRecorder() : null;

    private void SaveRecording()
    {
        if (_recorder is null) return;
        var path = Environment.GetEnvironmentVariable("OTA_SCRIBBLE_RECORD")!;
        try { _recorder.WriteAndReset(path); }
        catch (Exception ex) { ProfileToast.Show($"Couldn't save the recording: {ex.Message}", "IconAlert"); }
    }

    /// <summary>Where the stroke is drawn on the canvas (always the pointer position, both modes).
    /// Shown on the next refresh tick.</summary>
    public void UpdateCanvasPosition(double x, double y)
    {
        _latestCanvas = (x, y);
        _readoutDirty = true;
    }

    /// <summary>Takes a sample for the source-dependent readouts (raw coords, pressure, tilt, hover) and the
    /// report-rate count. Cheap enough to call per report; the text is published on the next refresh tick.</summary>
    public void UpdateReadout(PenSample s)
    {
        _recorder?.Add(s);
        _latest = s;
        _hasSample = true;
        // Only refresh hover when the report actually carried it, so reports without proximity data
        // don't blank out the last-known value mid-hover.
        if (s.HoverDistance is { } hover) _latestHover = hover;
        _readoutDirty = true;

        // The daemon-side arrival time when we have it (0 = unstamped, e.g. a hand-built sample).
        var ticks = s.Timestamp != 0 ? s.Timestamp : Stopwatch.GetTimestamp();
        _lastReportTicks = ticks;
        _rateMeter.Record(ticks * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>If the pen has been silent for <see cref="OutOfRangeMs"/> as of <paramref name="nowTicks"/>
    /// (a <see cref="Stopwatch.GetTimestamp"/> value), it has left range: blank every readout to "—" instead of
    /// leaving the last values on screen. Runs on the refresh timer; public so tests can drive it.</summary>
    public void ClearIfOutOfRange(long nowTicks)
    {
        if (!_hasSample) return;
        if ((nowTicks - _lastReportTicks) * 1000.0 / Stopwatch.Frequency <= OutOfRangeMs) return;

        ResetReadouts();
        const string none = "—";
        CanvasXText = CanvasYText = RawXText = RawYText = none;
        TiltXText = TiltYText = none;
        PressureText = AzimuthText = AltitudeText = TwistText = HoverText = RateText = none;
    }

    /// <summary>Formats the latest stashed values into the readout text. Runs on the refresh timer; public so
    /// tests can drive it without waiting on the clock.</summary>
    public void PublishReadouts()
    {
        if (!_readoutDirty) return;
        _readoutDirty = false;

        if (_latestCanvas is { } c)
        {
            CanvasXText = c.X.ToString("0.#");
            CanvasYText = c.Y.ToString("0.#");
        }
        if (_latestHover is { } hover) HoverText = hover.ToString("0");
        if (!_hasSample) return;

        var s = _latest;
        RawXText = s.RawX.ToString("0.#");
        RawYText = s.RawY.ToString("0.#");
        PressureText = s.Pressure.ToString("0.000");
        TiltXText = s.TiltX.ToString("0.0") + "°";
        TiltYText = s.TiltY.ToString("0.0") + "°";
        AzimuthText = DiagnosticsMath.TiltAzimuthDegrees(s.TiltX, s.TiltY).ToString("0.0") + "°";
        AltitudeText = DiagnosticsMath.TiltAltitudeDegrees(s.TiltX, s.TiltY).ToString("0.0") + "°";
        TwistText = s.Twist.ToString("0.0") + "°";
    }

    /// <summary>Publishes the report rate. Held at its last value while the pen is silent.</summary>
    public void PublishRate()
    {
        if (_rateMeter.Hz is { } hz) RateText = $"{hz:0} /s";
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        ClearIfOutOfRange(Stopwatch.GetTimestamp());
        PublishReadouts();
        Recording.Tick();
        _ticks++;
        if (_ticks % RateEveryTicks == 0) PublishRate();
        PublishCounters(newLagWindow: _ticks % (1000 / RefreshMs) == 0); // lag window ≈ 1 s
    }

    private void ResetReadouts()
    {
        _rateMeter.Reset();
        _latest = default;
        _hasSample = false;
        _latestHover = null;
        _latestCanvas = null;
        _readoutDirty = false;
        _ticks = 0;
        _lastReportTicks = 0;
    }

    private void OnDriverSample(PenSample s)
    {
        if (CountersEnabled) NoteHandled(s, Stopwatch.GetTimestamp());
        DriverSample?.Invoke(s);
    }

    // --- Pipeline counters ---
    // Where every pen report ends up, so "is every point used?" has a numeric answer at 1000 Hz:
    //   parsed (queued to the UI thread) = handled + queued;  handled = drawn + hover + off-canvas.
    // "queued" is the UI thread's backlog (parsed but not yet handled) — it stays near 0 when the UI keeps up.
    // The lag is arrival-at-the-app → handled, averaged/maxed over the last second. Counted since the page
    // opened or Clear was pressed.

    /// <summary>What happened to a handled sample on the canvas.</summary>
    public enum SampleOutcome
    {
        /// <summary>Put ink on the canvas.</summary>
        Drawn,
        /// <summary>On the canvas but no ink: pen up (hovering) or the pointer-only brush.</summary>
        Hover,
        /// <summary>Not on the canvas: the pen's mapped position is outside it (or the canvas is disabled).</summary>
        OffCanvas,
    }

    // _handledEver never resets: it is the baseline for "parsed", so a report already queued when the counters
    // reset is counted as parsed AND handled after it (parsed = handled + queued stays exact at any reset).
    /// <summary>Whether the counters run at all. Off by default: with it off, the per-report path does no
    /// counting or timing, the daemon source skips its counter, and the "Reports" line is hidden — so they
    /// cost nothing until wanted. Set the environment variable <c>OTA_SCRIBBLE_COUNTERS=1</c> to turn them on.</summary>
    [ObservableProperty] private bool _countersEnabled =
        Environment.GetEnvironmentVariable("OTA_SCRIBBLE_COUNTERS") == "1";

    partial void OnCountersEnabledChanged(bool value)
    {
        _driver.CountSamples = value;
        ResetCounters();
    }

    private long _handled, _handledEver, _drawn, _hover, _offCanvas;
    private double _lagSumMs, _lagMaxMs;
    private long _lagCount;
    private string _lagWindowText = "—";

    /// <summary>One line summarising the counters; see the block comment above.</summary>
    [ObservableProperty] private string _countersText = "no reports yet";

    /// <summary>Counts a sample the UI thread has taken off the queue. <paramref name="nowTicks"/> is a
    /// <see cref="Stopwatch.GetTimestamp"/> value; the lag is measured against the sample's arrival stamp.</summary>
    public void NoteHandled(PenSample s, long nowTicks)
    {
        if (!CountersEnabled) return;
        _handled++;
        _handledEver++;
        if (s.Timestamp == 0) return; // unstamped (hand-built) — no arrival time to measure from
        var lagMs = (nowTicks - s.Timestamp) * 1000.0 / Stopwatch.Frequency;
        _lagSumMs += lagMs;
        _lagCount++;
        if (lagMs > _lagMaxMs) _lagMaxMs = lagMs;
    }

    /// <summary>Counts what the canvas did with a handled sample.</summary>
    public void NoteOutcome(SampleOutcome outcome)
    {
        if (!CountersEnabled) return;
        switch (outcome)
        {
            case SampleOutcome.Drawn: _drawn++; break;
            case SampleOutcome.Hover: _hover++; break;
            default: _offCanvas++; break;
        }
    }

    /// <summary>Formats the counters into <see cref="CountersText"/>. <paramref name="newLagWindow"/> closes the
    /// current lag window (about once a second) so the lag figures are a recent average, not an all-time one.</summary>
    public void PublishCounters(bool newLagWindow = false)
    {
        if (!CountersEnabled) return;
        if (newLagWindow && _lagCount > 0)
        {
            _lagWindowText = $"{_lagSumMs / _lagCount:0.0} / {_lagMaxMs:0.0} ms";
            _lagSumMs = _lagMaxMs = 0;
            _lagCount = 0;
        }

        // Everything handled before the last reset is excluded from "parsed"; what was still queued then is not.
        var parsed = _driver.PenSampleCount - (_handledEver - _handled);
        if (parsed <= 0 && _handled == 0) { CountersText = "no reports yet"; return; }
        var queued = parsed - _handled;
        CountersText = $"parsed {parsed:N0} · queued {queued:N0} · drawn {_drawn:N0} · hover {_hover:N0} · " +
                       $"off-canvas {_offCanvas:N0} · lag avg/max {_lagWindowText}";
    }

    /// <summary>Zeroes the counters (page opened, or Clear pressed). Reports already queued but not yet handled
    /// still arrive afterwards; they stay counted as parsed, so the books keep balancing.</summary>
    public void ResetCounters()
    {
        _handled = _drawn = _hover = _offCanvas = 0;
        _lagSumMs = _lagMaxMs = 0;
        _lagCount = 0;
        _lagWindowText = "—";
        CountersText = "no reports yet";
    }

    // --- Driver-input position mapping (#95) ---
    // In Driver mode the canvas paints under the pen by mapping the daemon's raw tablet position
    // through OTD's Absolute-mode transform. Only works in an Absolute output mode; in Relative the
    // canvas is disabled with a note.

    private (TabletDigitizerSpec Digi, MappingArea Input, MappingArea Output, bool Clip, bool Limit)? _mapping;

    /// <summary>Driver mode + an Absolute output mode we can map → paint at the mapped position.</summary>
    public bool DriverPositioned => _mapping.HasValue;

    /// <summary>Driver mode but no usable Absolute mapping → canvas disabled, show the note.</summary>
    public bool DriverCanvasDisabled => !_mapping.HasValue;

    public string DriverDisabledNote =>
        "Painting here needs an Absolute output mode (e.g. Windows Ink Absolute) on the active tablet, so " +
        "the pen's position on the tablet can be mapped to the screen. The current mode doesn't map " +
        "position, so the canvas is disabled — the readouts above still work. Set this tablet to an " +
        "Absolute output mode to draw here.";

    // Where OTD's 0-based virtual-desktop space starts on screen — non-zero when a monitor sits left of /
    // above the primary. Without it a mapped point lands that far off the canvas and nothing draws.
    private Vector2 _desktopOrigin;

    /// <summary>Map a raw tablet point to a screen pixel (virtual-desktop coordinates), or null if not mappable.</summary>
    public Vector2? MapRawToDesktop(double rawX, double rawY) =>
        _mapping is { } m
            ? AbsolutePositionMapper.MapToDesktop(new Vector2((float)rawX, (float)rawY), m.Digi, m.Input, m.Output, m.Clip, m.Limit) + _desktopOrigin
            : null;

    private void RecomputeMapping()
    {
        _mapping = BuildMapping();
        _desktopOrigin = DisplayMappingApplier.DesktopOrigin(DisplayEnumerator.Enumerate());
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
        RecomputeMapping(); // data may already be loaded before the page is shown
        RefreshTabletStatus();
        ResetReadouts();
        ResetCounters();
        RateText = "—";
        _refreshTimer.Start();
        await _driver.StartAsync();
    }

    public async Task DeactivateAsync()
    {
        _refreshTimer.Stop();
        // The driver stream goes off with the page, so a recording in progress ends here; it waits at review.
        Recording.StopCommand.Execute(null);
        SaveRecording();
        await _driver.StopAsync();
    }

    public void Dispose()
    {
        _deviceData.DataLoaded -= OnDataLoaded;
        _deviceData.PropertyChanged -= OnDeviceDataPropertyChanged;
        _driver.Sample -= OnDriverSample;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
        _driver.SetTap(null);
        _ = _driver.StopAsync();
    }
}
