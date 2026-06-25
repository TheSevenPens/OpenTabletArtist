using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.Services;

/// <summary>
/// Driver-side pen input for the Test tab: turns the OTD daemon's <c>DeviceReport</c> stream into
/// normalized <see cref="PenSample"/>s (the same stream Diagnostics uses, via
/// <see cref="IDaemonDebugSession"/>). Unlike OS pointer input, this reflects what the driver sees
/// before the Windows Ink output stage — so it works even when vmulti / Windows Ink isn't set up.
/// </summary>
public sealed class DaemonPenInputSource
{
    private readonly IDaemonDebugSession _daemon;
    private bool _running;

    public DaemonPenInputSource(IDaemonDebugSession daemon) => _daemon = daemon;

    /// <summary>Raised on the UI thread for each parseable device report.</summary>
    public event Action<PenSample>? Sample;

    public async Task StartAsync()
    {
        if (_running) return;

        // Enable the daemon stream first; only subscribe if it succeeds — a subscribe-before-enable
        // would leak the handler if the RPC throws (the #39 lesson from Diagnostics).
        try { await _daemon.SetTabletDebugAsync(true); }
        catch { return; }

        _daemon.DeviceReport -= OnDeviceReport; // idempotent
        _daemon.DeviceReport += OnDeviceReport;
        _running = true;
    }

    public async Task StopAsync()
    {
        if (!_running) return;
        _daemon.DeviceReport -= OnDeviceReport;
        _running = false;
        try { await _daemon.SetTabletDebugAsync(false); } catch { }
    }

    private void OnDeviceReport(JObject data)
    {
        if (!DeviceReportSample.TryParse(data, out var sample)) return;
        // Reports arrive off the UI thread; marshal before raising (subscribers touch UI state).
        Dispatcher.UIThread.Post(() => Sample?.Invoke(sample));
    }
}
