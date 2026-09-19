using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// What a host may do with a daemon connection it does not own.
/// </summary>
///
/// <remarks>
/// <para>
/// Reading, watching and plugin management — the things a page legitimately needs — and nothing that
/// decides the connection's life. Connecting, reconnecting and closing belong to
/// <see cref="OtdSession"/>, which owns the connection; changing settings belongs to
/// <see cref="IOtdSettingsSession"/>, which owns the ordering around them.
/// </para>
/// <para>
/// <b>A forwarding object, not the connection wearing a smaller interface.</b> Narrowing by returning the
/// same instance as a narrower type is not narrowing at all: anything holding it can cast back to the
/// full transport, and to <see cref="IDisposable"/>, and close the connection out from under the session.
/// What is handed out here forwards to the connection and is not it, so there is nothing to cast back to.
/// </para>
/// <para>
/// This composes the three role interfaces that already had several independent consumers each, rather
/// than restating them. It adds the read-only queries and the tablet-change signal, which every page that
/// shows tablets needs and none of which can change anything.
/// </para>
/// </remarks>
public interface IDaemonCapabilities : IDaemonDebugSession, IDaemonLogSource, IDaemonPluginService
{
    /// <summary>The daemon reported a tablet add/remove (plug/unplug, sleep/wake).</summary>
    event Action? TabletsChanged;

    /// <summary>The daemon's paths and version. Null when not connected.</summary>
    Task<AppInfo?> GetAppInfoAsync();

    /// <summary>Connected tablets, as raw JSON — complex runtime data the caller parses selectively.</summary>
    Task<JArray> GetTabletsAsync();

    /// <summary>
    /// Every device the daemon can see, including ones it failed to open. Tells "nothing is plugged in"
    /// apart from "something is, and the daemon cannot reach it".
    /// </summary>
    Task<JArray> GetDevicesAsync();
}
