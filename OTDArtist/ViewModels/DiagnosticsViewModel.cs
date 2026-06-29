using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OtdArtist.Domain;
using OtdArtist.Services;

namespace OtdArtist.ViewModels;

/// <summary>
/// View model for the Diagnostics page (live pen data). Page-VM split (#14 phase 2):
/// owns the debug-report subscription lifecycle. Depends on <see cref="IDaemonDebugSession"/>
/// (the report stream + debug toggle) and, optionally, on <see cref="IConnectionState"/> to
/// self-sync <see cref="IsConnected"/> (gates Start; drives the not-connected warning). The
/// shell calls <see cref="StopDebuggingAsync"/> on page-leave (#19), with <see cref="Dispose"/>
/// as a backstop.
/// </summary>
public partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly IDaemonDebugSession _daemon;
    private readonly IConnectionState? _connection;
    private double _reportPeriodEma;
    private DateTime _lastReportTime;

    // Synced from the session's IConnectionState when provided (gates Start; drives the
    // not-connected warning). Stays settable so tests can simulate connection state directly.
    [ObservableProperty] private bool _isConnected;

    [ObservableProperty] private bool _isDebugging;
    [ObservableProperty] private string _lastReportRaw = "";
    [ObservableProperty] private string _lastReportFormatted = "";
    [ObservableProperty] private int _reportCount;
    [ObservableProperty] private string _debugReportRate = "";
    [ObservableProperty] private string _debugTabletName = "";
    [ObservableProperty] private string _debugReportType = "";
    [ObservableProperty] private double _debugPenX;
    [ObservableProperty] private double _debugPenY;
    [ObservableProperty] private double _debugPenPressure;
    [ObservableProperty] private double _debugMaxX;
    [ObservableProperty] private double _debugMaxY;
    [ObservableProperty] private double _debugMaxPressure;
    [ObservableProperty] private double _debugDigitizerWidth;
    [ObservableProperty] private double _debugDigitizerHeight;
    [ObservableProperty] private bool _debugHasPosition;
    [ObservableProperty] private double _debugTiltX;
    [ObservableProperty] private double _debugTiltY;
    [ObservableProperty] private double _debugPressurePercent;
    [ObservableProperty] private double _debugTiltAzimuth;
    [ObservableProperty] private double _debugTiltAltitude;
    [ObservableProperty] private string _debugPenButtons = "";
    [ObservableProperty] private string _debugNearProximity = "";
    [ObservableProperty] private string _debugHoverDistance = "";

    public DiagnosticsViewModel(IDaemonDebugSession daemon, IConnectionState? connection = null)
    {
        _daemon = daemon;
        _connection = connection;
        if (_connection != null)
        {
            IsConnected = _connection.IsConnected;
            _connection.PropertyChanged += OnConnectionChanged;
        }
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IConnectionState.IsConnected))
            IsConnected = _connection!.IsConnected;
    }

    [RelayCommand]
    private async Task ToggleDebugging()
    {
        if (IsDebugging)
            await StopDebuggingAsync();
        else
            await StartDebuggingAsync();
    }

    private async Task StartDebuggingAsync()
    {
        if (IsDebugging || !IsConnected) return;

        // Enable the daemon stream first; only subscribe if it succeeds. If the RPC throws
        // or the daemon disconnects, a subscribe-before-enable would leak the handler (with
        // IsDebugging still false), and a later Start would double-subscribe. (#39)
        try
        {
            await _daemon.SetTabletDebugAsync(true);
        }
        catch
        {
            return;
        }

        // Idempotent subscribe: removing a non-subscribed handler is a no-op, so this
        // guarantees exactly one subscription even if something slips past the guards.
        _daemon.DeviceReport -= OnDeviceReport;
        _daemon.DeviceReport += OnDeviceReport;
        IsDebugging = true;
        ReportCount = 0;
        LastReportRaw = "";
        LastReportFormatted = "";
        DebugReportRate = "";
        DebugTabletName = "";
        DebugReportType = "";
        DebugPenX = 0; DebugPenY = 0; DebugPenPressure = 0; DebugPressurePercent = 0;
        DebugMaxX = 0; DebugMaxY = 0; DebugMaxPressure = 0;
        DebugDigitizerWidth = 0; DebugDigitizerHeight = 0;
        DebugHasPosition = false;
        DebugTiltX = 0; DebugTiltY = 0; DebugTiltAzimuth = 0; DebugTiltAltitude = 0;
        DebugPenButtons = ""; DebugNearProximity = ""; DebugHoverDistance = "";
        _reportPeriodEma = 0;
        _lastReportTime = DateTime.MinValue;
    }

    /// <summary>Stops debugging and disables the daemon's debug stream. Public so the shell can call it on page-leave.</summary>
    public async Task StopDebuggingAsync()
    {
        if (!IsDebugging) return;
        _daemon.DeviceReport -= OnDeviceReport;
        IsDebugging = false;
        try { await _daemon.SetTabletDebugAsync(false); } catch { }
    }

    private void OnDeviceReport(JObject data)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ReportCount++;

            // Report rate (EMA)
            var now = DateTime.UtcNow;
            if (_lastReportTime != DateTime.MinValue)
            {
                var deltaMs = (now - _lastReportTime).TotalMilliseconds;
                _reportPeriodEma = DiagnosticsMath.UpdateReportPeriodEma(_reportPeriodEma, deltaMs);
                var hz = DiagnosticsMath.ReportRateHz(_reportPeriodEma);
                if (hz > 0) DebugReportRate = $"{hz} Hz";
            }
            _lastReportTime = now;

            // Tablet name
            var tabletName = data["Tablet"]?["Properties"]?["Name"]?.ToString();
            if (tabletName != null) DebugTabletName = tabletName;

            // Report type
            var path = data["Path"]?.ToString();
            if (path != null) DebugReportType = path.Split('.').LastOrDefault() ?? path;

            // Digitizer specs (for visualizer scaling)
            var digi = data["Tablet"]?["Properties"]?["Specifications"]?["Digitizer"];
            if (digi != null)
            {
                var maxX = digi["MaxX"]?.Value<double>() ?? 0;
                var maxY = digi["MaxY"]?.Value<double>() ?? 0;
                var digiW = digi["Width"]?.Value<double>() ?? 0;
                var digiH = digi["Height"]?.Value<double>() ?? 0;
                if (maxX > 0) DebugMaxX = maxX;
                if (maxY > 0) DebugMaxY = maxY;
                if (digiW > 0) DebugDigitizerWidth = digiW;
                if (digiH > 0) DebugDigitizerHeight = digiH;
            }

            // Max pressure from pen specs
            var pen = data["Tablet"]?["Properties"]?["Specifications"]?["Pen"];
            if (pen != null)
            {
                var maxP = pen["MaxPressure"]?.Value<double>() ?? 0;
                if (maxP > 0) DebugMaxPressure = maxP;
            }

            var reportData = data["Data"];
            if (reportData == null) return;

            // Raw bytes
            var rawBase64 = reportData["Raw"]?.ToString();
            if (rawBase64 != null)
                LastReportRaw = DiagnosticsMath.FormatRawHex(rawBase64);

            // Position for visualizer
            var pos = reportData["Position"];
            if (pos != null)
            {
                DebugPenX = pos["X"]?.Value<double>() ?? 0;
                DebugPenY = pos["Y"]?.Value<double>() ?? 0;
                DebugHasPosition = true;
            }

            // Pressure
            var pressure = reportData["Pressure"];
            if (pressure != null)
            {
                DebugPenPressure = pressure.Value<double>();
                DebugPressurePercent = DiagnosticsMath.PressurePercent(DebugPenPressure, DebugMaxPressure);
            }

            // Tilt
            var tilt = reportData["Tilt"];
            if (tilt != null)
            {
                DebugTiltX = tilt["X"]?.Value<double>() ?? 0;
                DebugTiltY = tilt["Y"]?.Value<double>() ?? 0;
                DebugTiltAzimuth = DiagnosticsMath.TiltAzimuthDegrees(DebugTiltX, DebugTiltY);
                DebugTiltAltitude = DiagnosticsMath.TiltAltitudeDegrees(DebugTiltX, DebugTiltY);
            }

            // Pen buttons
            var buttons = reportData["PenButtons"];
            if (buttons is JArray btnArr)
                DebugPenButtons = string.Join("  ", btnArr.Select((b, i) => $"{i + 1}: {b}"));

            // Formatted fields
            var lines = new List<string>();
            if (pos != null) lines.Add($"Position: [{pos["X"]}, {pos["Y"]}]");
            if (pressure != null) lines.Add($"Pressure: {pressure}");
            if (buttons != null) lines.Add($"PenButtons: {buttons}");
            if (tilt != null)
            {
                lines.Add($"Tilt: [{tilt["X"]}, {tilt["Y"]}]");
                lines.Add($"Azimuth: {DebugTiltAzimuth:F1}°  Altitude: {DebugTiltAltitude:F1}°");
            }
            var aux = reportData["AuxButtons"];
            if (aux != null) lines.Add($"AuxButtons: {aux}");
            var proximity = reportData["NearProximity"];
            if (proximity != null)
            {
                DebugNearProximity = proximity.Value<bool>() ? "Yes" : "No";
                lines.Add($"NearProximity: {proximity}");
            }
            var hover = reportData["HoverDistance"];
            if (hover != null)
            {
                DebugHoverDistance = hover.ToString();
                lines.Add($"HoverDistance: {hover}");
            }

            LastReportFormatted = string.Join("\n", lines);
        });
    }

    public void Dispose()
    {
        if (_connection != null)
            _connection.PropertyChanged -= OnConnectionChanged;
        if (IsDebugging)
        {
            _daemon.DeviceReport -= OnDeviceReport;
            try { _daemon.SetTabletDebugAsync(false).Wait(2000); } catch { }
        }
    }
}
