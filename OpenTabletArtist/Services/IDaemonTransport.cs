using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;

namespace OpenTabletArtist.Services;

/// <summary>
/// Installing and unloading daemon plugins. Reached today through <see cref="AppSession.Daemon"/> by the
/// Windows Ink install flows and the shared <see cref="PluginInstallApplier"/>.
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
/// Everything <see cref="AppSession"/> needs from the daemon connection, behind an interface so the
/// session can be tested without a live pipe (#740).
///
/// Until this existed, <c>AppSession</c> took a concrete <see cref="DaemonClient"/>, so nothing that talks
/// to the daemon could be exercised in a test: the data load, the apply path, and the per-app baseline
/// isolation from #737 were all unreachable, and the lifecycle tests had to construct a real client and
/// poke it.
///
/// This is deliberately wider than the app's other role interfaces (<see cref="IDaemonDebugSession"/>,
/// <see cref="IDaemonLogSource"/>, <see cref="IDaemonPluginService"/>), which are narrow because they have
/// several independent consumers each. This one has exactly one consumer and exists to make that class
/// testable; splitting it into three roles with one consumer apiece would be ceremony. It composes the
/// narrow ones so <see cref="AppSession.Daemon"/> can still hand the whole client to the pages that want
/// a debug stream, the log, or the plugin operations.
/// </summary>
public interface IDaemonTransport : IDaemonDebugSession, IDaemonLogSource, IDaemonPluginService, IDisposable
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

    /// <summary>The daemon's current in-memory settings. Null when not connected.</summary>
    Task<Settings?> GetSettingsAsync();

    /// <summary>Pushes settings to the daemon. False when there is no transport — the caller must not
    /// report an unsent change as live (#734).</summary>
    Task<bool> SetSettingsAsync(Settings settings);

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
