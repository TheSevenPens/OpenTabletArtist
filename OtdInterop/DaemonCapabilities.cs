using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Plugin.Logging;

namespace OtdInterop;

/// <summary>
/// Forwards the capabilities a host may use to a connection it does not own.
/// </summary>
///
/// <remarks>
/// Every member is a one-line forward, and the tedium is the feature. Returning the connection itself as
/// <see cref="IDaemonCapabilities"/> would be shorter and would narrow nothing: a caster gets back the
/// whole transport, including <see cref="IDisposable"/>, and can close the connection the session is
/// still using. This object is not that connection, so there is nothing behind it to reach.
///
/// The events forward by subscription rather than by re-raising, so a subscriber is attached to the real
/// connection and there is no intermediate list to leak or to forget to clear.
/// </remarks>
internal sealed class DaemonCapabilities(IDaemonTransport inner) : IDaemonCapabilities
{
    /// <inheritdoc />
    public event Action<JObject>? DeviceReport
    {
        add => inner.DeviceReport += value;
        remove => inner.DeviceReport -= value;
    }

    /// <inheritdoc />
    public event Action<LogMessage>? LogReceived
    {
        add => inner.LogReceived += value;
        remove => inner.LogReceived -= value;
    }

    /// <inheritdoc />
    public event Action? TabletsChanged
    {
        add => inner.TabletsChanged += value;
        remove => inner.TabletsChanged -= value;
    }

    /// <inheritdoc />
    public Task SetTabletDebugAsync(bool enabled) => inner.SetTabletDebugAsync(enabled);

    /// <inheritdoc />
    public Task<List<LogMessage>> GetCurrentLogAsync() => inner.GetCurrentLogAsync();

    /// <inheritdoc />
    public Task<AppInfo?> GetAppInfoAsync() => inner.GetAppInfoAsync();

    /// <inheritdoc />
    public Task<JArray> GetTabletsAsync() => inner.GetTabletsAsync();

    /// <inheritdoc />
    public Task<JArray> GetDevicesAsync() => inner.GetDevicesAsync();

    /// <inheritdoc />
    public Task<bool> DownloadPluginAsync(PluginMetadata metadata) => inner.DownloadPluginAsync(metadata);

    /// <inheritdoc />
    public Task<bool> UninstallPluginAsync(string directory) => inner.UninstallPluginAsync(directory);

    /// <inheritdoc />
    public Task LoadPluginsAsync() => inner.LoadPluginsAsync();
}
