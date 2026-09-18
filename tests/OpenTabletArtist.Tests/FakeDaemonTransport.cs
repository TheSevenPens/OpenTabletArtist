using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Tests;

/// <summary>
/// A daemon that isn't there (#740). Scripted results, recorded calls, and raisable events, so the
/// session's data load, apply path and connection handling can be exercised without a pipe.
///
/// Before <see cref="IDaemonTransport"/> existed, <c>AppSession</c> took a concrete <c>DaemonClient</c>,
/// so none of that was reachable from a test: the lifecycle tests had to construct a real client and
/// assert against a connection that never happened.
/// </summary>
internal sealed class FakeDaemonTransport : IDaemonTransport
{
    // --- Scripted responses ---

    /// <summary>What <see cref="GetSettingsAsync"/> returns. Null models "not connected".</summary>
    public Settings? Settings { get; set; }

    /// <summary>What <see cref="SetSettingsAsync"/> reports. False models no transport (#734).</summary>
    public bool SetSettingsSucceeds { get; set; } = true;

    /// <summary>When set, <see cref="SetSettingsAsync"/> throws it — a reachable daemon that refused.</summary>
    public Exception? SetSettingsThrows { get; set; }

    /// <summary>
    /// When set, decides the result of <see cref="SetSettingsAsync"/> — and, more to the point, decides
    /// <em>when</em>. Returning an incomplete task holds the call open, which is the only way to test what
    /// a second operation arriving mid-apply does. A sleep would make the same test timing-dependent.
    /// </summary>
    public Func<Settings, Task<bool>>? SetSettingsHandler { get; set; }

    public AppInfo? AppInfo { get; set; }
    public JArray Tablets { get; set; } = [];
    public JArray Devices { get; set; } = [];
    public int? ServerProcessId { get; set; }

    // --- Recorded calls ---

    /// <summary>Every settings object pushed, in order. The last one is what the daemon "holds".</summary>
    public List<Settings> Applied { get; } = new();
    public int GetSettingsCalls { get; private set; }
    public int ConnectCalls { get; private set; }
    public bool IsDisposed { get; private set; }

    // --- IDaemonTransport ---

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action? TabletsChanged;
    public event Action<JObject>? DeviceReport;
    public event Action<LogMessage>? LogReceived;

    public bool AutoReconnect { get; set; } = true;

    public Task ConnectAsync(CancellationToken ct)
    {
        ConnectCalls++;
        AutoReconnect = true;   // matches DaemonClient: an explicit connect re-enables it
        return Task.CompletedTask;
    }

    public Task<Settings?> GetSettingsAsync()
    {
        GetSettingsCalls++;
        return Task.FromResult(Settings);
    }

    public async Task<bool> SetSettingsAsync(Settings settings)
    {
        if (SetSettingsThrows != null) throw SetSettingsThrows;
        Applied.Add(settings);

        bool ok = SetSettingsHandler is { } handler
            ? await handler(settings)
            : SetSettingsSucceeds;

        if (ok) Settings = settings;   // the daemon now holds it
        return ok;
    }

    public Task<AppInfo?> GetAppInfoAsync() => Task.FromResult(AppInfo);
    public Task<JArray> GetTabletsAsync() => Task.FromResult(Tablets);
    public Task<JArray> GetDevicesAsync() => Task.FromResult(Devices);
    public int? GetServerProcessId() => ServerProcessId;

    public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    public Task<List<LogMessage>> GetCurrentLogAsync() => Task.FromResult(new List<LogMessage>());

    public Task<bool> DownloadPluginAsync(PluginMetadata metadata) => Task.FromResult(true);
    public Task<bool> UninstallPluginAsync(string directory) => Task.FromResult(true);
    public Task LoadPluginsAsync() => Task.CompletedTask;

    public void Dispose() => IsDisposed = true;

    // --- Test drivers ---

    /// <summary>Raise the daemon's Connected event, as the real client does after a successful connect.</summary>
    public void RaiseConnected() => Connected?.Invoke();

    /// <summary>Raise a transport drop.</summary>
    public void RaiseDisconnected() => Disconnected?.Invoke();

    /// <summary>Raise a tablet add/remove push.</summary>
    public void RaiseTabletsChanged() => TabletsChanged?.Invoke();

    /// <summary>Suppresses the unused-event warning for the two events no current test drives.</summary>
    public void RaiseDeviceReport(JObject report) => DeviceReport?.Invoke(report);

    /// <inheritdoc cref="RaiseDeviceReport"/>
    public void RaiseLog(LogMessage message) => LogReceived?.Invoke(message);
}
