using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;

namespace OtdInterop;

/// <summary>
/// Installing and unloading daemon plugins. Reached today through <c>AppSession.Daemon</c> by the
/// Windows Ink install flows and the shared <c>PluginInstallApplier</c>.
/// </summary>
public interface IDaemonPluginService
{
    /// <summary>Asks the daemon to download and install a plugin release. False if it refused.</summary>
    Task<bool> DownloadPluginAsync(PluginMetadata metadata);

    /// <summary>Removes an installed plugin by its directory. False if it refused.</summary>
    Task<bool> UninstallPluginAsync(string directory);

    /// <summary>Reloads the plugin directory so a newly-copied plugin is picked up.</summary>
    Task LoadPluginsAsync();
}

/// <summary>
/// Everything the host session needs from the daemon connection, behind an interface so the
/// session can be tested without a live pipe (#740).
///
/// Until this existed, <c>AppSession</c> took a concrete <c>DaemonClient</c>, so nothing that talks
/// to the daemon could be exercised in a test: the data load, the apply path, and the per-app baseline
/// isolation from #737 were all unreachable, and the lifecycle tests had to construct a real client and
/// poke it.
///
/// This is deliberately wider than the app's other role interfaces (<see cref="IDaemonDebugSession"/>,
/// <see cref="IDaemonLogSource"/>, <see cref="IDaemonPluginService"/>), which are narrow because they have
/// several independent consumers each. This one has exactly one consumer and exists to make that class
/// testable; splitting it into three roles with one consumer apiece would be ceremony. It composes the
/// narrow ones so <c>AppSession.Daemon</c> can still hand the whole client to the pages that want
/// a debug stream, the log, or the plugin operations.
///
/// Reading and writing settings is deliberately NOT here. Those two verbs sat alongside the device list
/// and the log stream, so every class that legitimately wanted one of those also held a settings writer
/// that bypassed every protection in <see cref="IOtdSettingsSession"/>. They live on the internal
/// <see cref="IDaemonSettingsChannel"/> now, which only the settings session receives.
///
/// Internal, because owning a connection and using one are different things. A host gets
/// <see cref="IDaemonCapabilities"/> — reading, watching, plugins — from <see cref="OtdSession"/>, and the
/// operations that decide this connection's life stay on the session that owns it.
/// </summary>
internal interface IDaemonTransport : IDaemonDebugSession, IDaemonLogSource, IDaemonPluginService, IDisposable
{
    /// <summary>A connection was established. Raised off the UI thread.</summary>
    event Action? Connected;

    /// <summary>The connection dropped. Raised off the UI thread.</summary>
    event Action? Disconnected;

    /// <summary>The daemon reported a tablet add/remove (plug/unplug, sleep/wake).</summary>
    event Action? TabletsChanged;

    /// <summary>When true, an unexpected drop schedules an automatic reconnect. Cleared around a
    /// user-initiated stop so "stopped" stays stopped.</summary>
    bool AutoReconnect { get; set; }

    /// <summary>Requests a connection. Fire-and-forget; <see cref="Connected"/> reports success.</summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>The daemon's paths and version. Null when not connected.</summary>
    Task<AppInfo?> GetAppInfoAsync();

    /// <summary>Connected tablets, as raw JSON — complex runtime data the caller parses selectively.</summary>
    Task<JArray> GetTabletsAsync();

    /// <summary>Every device the daemon can see, including ones it failed to open. Used to tell
    /// "nothing is plugged in" apart from "something is, and the daemon can't reach it".</summary>
    Task<JArray> GetDevicesAsync();

    /// <summary>PID of the process answering the pipe, for daemon-identity checks. Null when it can't be
    /// read — an elevated or other-user daemon, or a platform without the lookup.</summary>
    int? GetServerProcessId();
}
