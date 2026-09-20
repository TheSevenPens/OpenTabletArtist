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
/// <para>
/// <b>After the session is disposed (#828).</b> This is borrowed, and disposing the session does not take
/// it back — a host that kept a reference can still call every member here. Each one then reports
/// <b>not connected</b>: null, empty, or false, exactly as it does for a session whose daemon is not
/// running, so a caller does not need to know whether the session it is holding has been disposed in
/// order to know how to read the result.
/// </para>
/// <para>
/// <b>The scope of that, precisely.</b> It covers calls <em>begun after</em> the session has torn down.
/// It is not a promise that a call already in flight cannot fault when its transport is disposed
/// underneath it, and it does not gate calls made during a graceful close while work is still settling —
/// the session is not torn down yet, so this still forwards them.
/// </para>
/// <para>
/// An empty list here is "nothing to say", not "a healthy daemon with no tablets". Hosts that need to
/// tell those apart read the connection state, and a host with its own teardown ordering still needs its
/// own guards; these answers do not replace them.
/// </para>
/// <para>
/// Subscribing after that point does nothing, because there is nothing left to raise an event and
/// attaching would only keep the handler alive against a connection that has gone.
/// <b>Unsubscribing keeps working regardless</b>, so a host tearing down in an order this library did not
/// choose can always let go.
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
