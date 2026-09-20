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
///
/// <para>
/// <b>After the session is gone (#828).</b> This is borrowed, and disposing the session does not take it
/// back: a host that kept a reference can still call and still subscribe. That had no answer, so it got
/// whatever the transport happened to do — which was to throw, because a disposed channel passes a
/// null check.
/// </para>
/// <para>
/// It reports <b>not connected</b> instead, which is what every one of these members already documents
/// for a session with no daemon, and what the transport itself returns when it has no channel. The same
/// answer either way is the point: a host should not need to know whether the session it is holding has
/// been disposed to know how to read the result.
/// </para>
/// <para>
/// <b>Subscribing is refused; unsubscribing always works.</b> There is nothing left to observe, so
/// attaching would only keep the handler — and whatever it closes over — alive against a connection that
/// has gone. Detaching has to keep working regardless, or a host tearing down in an order this library
/// did not choose would be unable to let go.
/// </para>
/// </remarks>
/// <param name="inner">The connection to forward to.</param>
/// <param name="gone">Whether the session that lent this out has been torn down.</param>
internal sealed class DaemonCapabilities(IDaemonTransport inner, Func<bool> gone) : IDaemonCapabilities
{
    /// <inheritdoc />
    public event Action<JObject>? DeviceReport
    {
        add { if (!gone()) inner.DeviceReport += value; }
        remove => inner.DeviceReport -= value;
    }

    /// <inheritdoc />
    public event Action<LogMessage>? LogReceived
    {
        add { if (!gone()) inner.LogReceived += value; }
        remove => inner.LogReceived -= value;
    }

    /// <inheritdoc />
    public event Action? TabletsChanged
    {
        add { if (!gone()) inner.TabletsChanged += value; }
        remove => inner.TabletsChanged -= value;
    }

    /// <inheritdoc />
    public Task SetTabletDebugAsync(bool enabled) =>
        gone() ? Task.CompletedTask : inner.SetTabletDebugAsync(enabled);

    /// <inheritdoc />
    public Task<List<LogMessage>> GetCurrentLogAsync() =>
        gone() ? Task.FromResult(new List<LogMessage>()) : inner.GetCurrentLogAsync();

    /// <inheritdoc />
    public Task<AppInfo?> GetAppInfoAsync() =>
        gone() ? Task.FromResult<AppInfo?>(null) : inner.GetAppInfoAsync();

    /// <inheritdoc />
    public Task<JArray> GetTabletsAsync() =>
        gone() ? Task.FromResult(new JArray()) : inner.GetTabletsAsync();

    /// <inheritdoc />
    public Task<JArray> GetDevicesAsync() =>
        gone() ? Task.FromResult(new JArray()) : inner.GetDevicesAsync();

    /// <inheritdoc />
    public Task<bool> DownloadPluginAsync(PluginMetadata metadata) =>
        gone() ? Task.FromResult(false) : inner.DownloadPluginAsync(metadata);

    /// <inheritdoc />
    public Task<bool> UninstallPluginAsync(string directory) =>
        gone() ? Task.FromResult(false) : inner.UninstallPluginAsync(directory);

    /// <inheritdoc />
    public Task LoadPluginsAsync() => gone() ? Task.CompletedTask : inner.LoadPluginsAsync();
}
