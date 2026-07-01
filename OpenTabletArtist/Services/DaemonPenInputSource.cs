using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Driver-side pen input for the Test tab: turns the OTD daemon's <c>DeviceReport</c> stream into
/// normalized <see cref="PenSample"/>s (the same stream Diagnostics uses, via
/// <see cref="IDaemonDebugSession"/>). Unlike OS pointer input, this reflects what the driver sees
/// before the Windows Ink output stage — so it works even when vmulti / Windows Ink isn't set up.
/// </summary>
public sealed class DaemonPenInputSource
{
    private readonly IDaemonDebugSession _daemon;
    private bool _wantRunning;  // desired state — survives across the awaits below
    private bool _starting;     // a StartAsync is mid-flight (awaiting enable)
    private bool _subscribed;

    public DaemonPenInputSource(IDaemonDebugSession daemon) => _daemon = daemon;

    /// <summary>Raised on the UI thread for each parseable device report.</summary>
    public event Action<PenSample>? Sample;

    /// <summary>Raised on the UI thread when a report carries auxiliary-button (express key) state,
    /// with the current pressed/released state of each button. Pen-only reports don't fire it.</summary>
    public event Action<bool[]>? AuxButtons;

    /// <summary>Wheel-button state per wheel (jagged: wheel → its buttons). Wheel-button reports only.</summary>
    public event Action<bool[][]>? WheelButtons;

    /// <summary>Absolute-wheel positions per wheel (touch rings). Absolute-wheel reports only.</summary>
    public event Action<uint?[]>? WheelPositions;

    /// <summary>Relative-wheel step deltas per wheel (scroll wheels). Relative-wheel reports only.</summary>
    public event Action<int[]>? WheelDeltas;

    public async Task StartAsync()
    {
        _wantRunning = true;
        if (_subscribed || _starting) return;

        _starting = true;
        try
        {
            // Enable the daemon stream first; only subscribe if it succeeds — a subscribe-before-
            // enable would leak the handler if the RPC throws (the #39 lesson from Diagnostics).
            try { await _daemon.SetTabletDebugAsync(true); }
            catch { return; }

            // If Stop ran while we were awaiting the enable, undo it instead of subscribing —
            // otherwise the stream would stay on after the page/source went inactive.
            if (!_wantRunning)
            {
                try { await _daemon.SetTabletDebugAsync(false); } catch { }
                return;
            }

            _daemon.DeviceReport -= OnDeviceReport; // idempotent
            _daemon.DeviceReport += OnDeviceReport;
            _subscribed = true;
        }
        finally { _starting = false; }
    }

    public async Task StopAsync()
    {
        // Only disable if we actually enabled (or an enable is in flight); avoids spurious RPCs
        // when stopping a source that never started (e.g. leaving the page in App mode).
        var wasActiveOrStarting = _subscribed || _starting;
        _wantRunning = false;

        if (_subscribed)
        {
            _daemon.DeviceReport -= OnDeviceReport;
            _subscribed = false;
        }
        if (wasActiveOrStarting)
            try { await _daemon.SetTabletDebugAsync(false); } catch { }
    }

    private void OnDeviceReport(JObject data)
    {
        // Reports arrive off the UI thread; marshal before raising (subscribers touch UI state).
        // Pen and aux state come on different report types, so parse/raise them independently.
        if (DeviceReportSample.TryParse(data, out var sample))
            Dispatcher.UIThread.Post(() => Sample?.Invoke(sample));
        if (DeviceReportSample.TryParseAuxButtons(data, out var aux))
            Dispatcher.UIThread.Post(() => AuxButtons?.Invoke(aux));
        if (DeviceReportSample.TryParseWheelButtons(data, out var wheelButtons))
            Dispatcher.UIThread.Post(() => WheelButtons?.Invoke(wheelButtons));
        if (DeviceReportSample.TryParseWheelPositions(data, out var wheelPositions))
            Dispatcher.UIThread.Post(() => WheelPositions?.Invoke(wheelPositions));
        if (DeviceReportSample.TryParseWheelDeltas(data, out var wheelDeltas))
            Dispatcher.UIThread.Post(() => WheelDeltas?.Invoke(wheelDeltas));
    }
}
