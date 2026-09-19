using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Services;
using OtdInterop;

namespace OpenTabletArtist.Tests;

/// <summary>
/// A daemon that isn't there (#740). Scripted results, recorded calls, and raisable events, so the
/// session's data load, apply path and connection handling can be exercised without a pipe.
///
/// Before <see cref="IDaemonTransport"/> existed, <c>AppSession</c> took a concrete <c>DaemonClient</c>,
/// so none of that was reachable from a test: the lifecycle tests had to construct a real client and
/// assert against a connection that never happened.
///
/// It implements <see cref="IDaemonSettingsChannel"/> as well, which the app cannot — that interface is
/// internal precisely so only the library's own connection carries a settings writer. This project is
/// granted internal access for the same reason it can construct the file store: the behaviour under test
/// is the implementation's, not the interface's.
/// </summary>
internal static class FakeSession
{
    /// <summary>
    /// A library session over a daemon that is not there.
    ///
    /// The library's own test support, reached through its internal seam. It used to be a parameter on
    /// the supported host API -- <c>AppSession</c> took an optional file store purely so a write could be
    /// made to fail on demand -- which shaped the application's constructor around this project's needs.
    /// </summary>
    /// <param name="daemon">The stand-in connection.</param>
    /// <param name="store">A writer whose failures a test controls, or null for the library's own.</param>
    /// <param name="locator">
    /// What the session is told is running. Defaults to one that can see nothing, which is the ordinary
    /// case for a fake -- and the case where the session must leave its state alone rather than treat
    /// "cannot see" as "it changed".
    /// </param>
    public static OtdSession Over<T>(T daemon, ISettingsFileStore? store = null,
        IDaemonProcessLocator? locator = null)
        where T : IDaemonTransport, IDaemonSettingsChannel =>
        OtdSession.ForTesting(daemon, store, NullOtdLog.Instance, OtaSettingsPolicy.Instance,
            locator ?? new FakeProcessLocator());
}

/// <summary>
/// What is running, as a test decides. Both answers are settable, and both default to null -- which is
/// what an elevated or another user's daemon looks like, and what the session must not read as a change.
/// </summary>
internal sealed class FakeProcessLocator : IDaemonProcessLocator
{
    /// <summary>The executable a process id resolves to. Null means "cannot see".</summary>
    public string? Path { get; set; }

    /// <summary>The executable for the off-Windows single-daemon fallback.</summary>
    public string? OnlyDaemon { get; set; }

    /// <summary>
    /// How many times each lookup was asked.
    ///
    /// Counted because "the fallback was skipped" and "the fallback ran and returned null" produce the
    /// same answer, and only one of them is the behaviour being asserted.
    /// </summary>
    public int PathOfCalls { get; private set; }

    /// <inheritdoc cref="PathOfCalls"/>
    public int FallbackCalls { get; private set; }

    public string? PathOf(int processId)
    {
        PathOfCalls++;
        return Path;
    }

    public string? SingleRunningDaemonPath()
    {
        FallbackCalls++;
        return OnlyDaemon;
    }
}

internal sealed class FakeDaemonTransport : IDaemonTransport, IDaemonSettingsChannel
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

    /// <summary>
    /// When set, replaces <see cref="GetSettingsAsync"/> entirely — so a test can hold a read open and
    /// let something else complete while it is outstanding. Reads are not instantaneous against a real
    /// daemon, and an immediately-answering fake cannot express that.
    /// </summary>
    public Func<Task<Settings?>>? GetSettingsHandler { get; set; }

    /// <summary>
    /// Which channel this is. <see cref="Reconnect"/> moves it; re-raising <see cref="RaiseConnected"/>
    /// alone does not, so a test can model the two separately — a fresh channel, and a notification about
    /// one.
    /// </summary>
    public int Incarnation { get; private set; }

    /// <summary>
    /// A new channel, as the real client establishes one: the incarnation moves FIRST, then the event.
    ///
    /// That order is the whole of what is being modelled. The real client assigns its RPC channel, and
    /// only afterwards raises Connected — so there is a window in which sends already reach the new daemon
    /// and nobody has been told. A fake that bumped the two together could not express it.
    /// </summary>
    public void Reconnect()
    {
        Incarnation++;
        RaiseConnected();
    }

    /// <summary>The channel is replaced, and nothing announces it. The window, on its own.</summary>
    public void ReconnectSilently() => Incarnation++;

    /// <inheritdoc />
    IDaemonSettingsBinding IDaemonSettingsChannel.Bind() => new Binding(this, Incarnation);

    /// <summary>
    /// A hold on one of this fake's channels, modelling the real one: a send through a hold taken before
    /// a reconnect reaches a channel that is gone, and fails -- it is NOT delivered to the replacement.
    ///
    /// Recording the attempt on the fake regardless is deliberate. A test asserting that obsolete work
    /// never entered the transport has to be able to see it if it did, and a hold that silently dropped
    /// the call would make that assertion unfalsifiable.
    /// </summary>
    private sealed class Binding(FakeDaemonTransport daemon, int incarnation) : IDaemonSettingsBinding
    {
        public int Incarnation => incarnation;

        private bool Live => incarnation == daemon.Incarnation;

        public Task<Settings?> GetSettingsAsync() =>
            Live ? daemon.GetSettingsAsync() : Task.FromResult<Settings?>(null);

        public Task<bool> SetSettingsAsync(Settings settings) =>
            Live ? daemon.SetSettingsAsync(settings) : Task.FromResult(false);
    }

    public Task<Settings?> GetSettingsAsync()
    {
        GetSettingsCalls++;
        return GetSettingsHandler?.Invoke() ?? Task.FromResult(Settings);
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

    public Task<AppInfo?> GetAppInfoAsync()
    {
        Calls.Add(nameof(GetAppInfoAsync));
        return Task.FromResult(AppInfo);
    }

    public Task<JArray> GetTabletsAsync()
    {
        Calls.Add(nameof(GetTabletsAsync));
        return Task.FromResult(Tablets);
    }

    public Task<JArray> GetDevicesAsync()
    {
        Calls.Add(nameof(GetDevicesAsync));
        return Task.FromResult(Devices);
    }
    public int? GetServerProcessId() => ServerProcessId;

    /// <summary>How many times the debug stream was toggled -- proof a forwarder reached this object.</summary>
    public int DebugCalls { get; private set; }

    /// <summary>
    /// Every member reached on this object, in order.
    ///
    /// For proving that a forwarding object forwards each member to the member of the same name. Nine
    /// one-line forwards all compile whether or not they are wired correctly, and a result scripted the
    /// same for two of them makes a swap invisible.
    /// </summary>
    public List<string> Calls { get; } = new();

    /// <summary>The last value passed to each member that takes one — a forward can reach the right
    /// member and still hand it the wrong thing.</summary>
    public bool? LastDebugEnabled { get; private set; }

    /// <inheritdoc cref="LastDebugEnabled"/>
    public PluginMetadata? LastDownloaded { get; private set; }

    /// <inheritdoc cref="LastDebugEnabled"/>
    public string? LastUninstalled { get; private set; }

    public Task SetTabletDebugAsync(bool enabled)
    {
        DebugCalls++;
        LastDebugEnabled = enabled;
        Calls.Add(nameof(SetTabletDebugAsync));
        return Task.CompletedTask;
    }
    /// <summary>What <see cref="GetCurrentLogAsync"/> returns, so a forward's result can be checked.</summary>
    public List<LogMessage> BufferedLog { get; } = new();

    public Task<List<LogMessage>> GetCurrentLogAsync()
    {
        Calls.Add(nameof(GetCurrentLogAsync));
        return Task.FromResult(BufferedLog);
    }

    /// <summary>What the two plugin verbs report, separately, so a swap between them is visible.</summary>
    public bool DownloadSucceeds { get; set; } = true;

    /// <inheritdoc cref="DownloadSucceeds"/>
    public bool UninstallSucceeds { get; set; } = true;

    public Task<bool> DownloadPluginAsync(PluginMetadata metadata)
    {
        LastDownloaded = metadata;
        Calls.Add(nameof(DownloadPluginAsync));
        return Task.FromResult(DownloadSucceeds);
    }

    public Task<bool> UninstallPluginAsync(string directory)
    {
        LastUninstalled = directory;
        Calls.Add(nameof(UninstallPluginAsync));
        return Task.FromResult(UninstallSucceeds);
    }

    public Task LoadPluginsAsync()
    {
        Calls.Add(nameof(LoadPluginsAsync));
        return Task.CompletedTask;
    }

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
