using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Services;

/// <summary>
/// The slice of the daemon connection the Diagnostics page needs: the device-report
/// stream and the debug-mode toggle. Introduced for the page-VM split (#14 phase 2) so
/// <c>DiagnosticsViewModel</c> depends on a small interface (fakeable in tests) rather
/// than the whole <see cref="DaemonClient"/>. Implemented by <see cref="DaemonClient"/>.
/// </summary>
public interface IDaemonDebugSession
{
    /// <summary>Raised for every tablet report while debug mode is enabled.</summary>
    event Action<JObject>? DeviceReport;

    /// <summary>Enables/disables the daemon's debug report stream.</summary>
    Task SetTabletDebugAsync(bool enabled);
}
