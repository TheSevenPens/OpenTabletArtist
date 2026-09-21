using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Plugin.Logging;
using OtdInterop;

namespace OtdInterop.Tests;

/// <summary>Headless sessions over the same scripted transport in all three test suites.</summary>
internal static class FakeSession
{
    // Memory persistence is the default: a fake session must never write an actual driver settings file.
    public static OtdSession Over<T>(T daemon, ISettingsFileStore? store = null,
        IDaemonProcessLocator? locator = null)
        where T : IDaemonTransport, IDaemonSettingsChannel =>
        OtdSession.ForTesting(daemon, store ?? new MemorySettingsFileStore(), NullOtdLog.Instance,
            locator ?? new FakeProcessLocator { Path = "daemon.exe", OnlyDaemon = "daemon.exe" });
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
    public Func<Settings, Settings>? Readback { get; set; }
    public bool SetSettingsSucceeds { get; set; } = true;

    /// <summary>When set, <see cref="SetSettingsAsync"/> throws it — a reachable daemon that refused.</summary>
    public Exception? SetSettingsThrows { get; set; }

    /// <summary>
    /// When set, decides the result of <see cref="SetSettingsAsync"/> — and, more to the point, decides
    /// <em>when</em>. Returning an incomplete task holds the call open, which is the only way to test what
    /// a second operation arriving mid-apply does. A sleep would make the same test timing-dependent.
    /// </summary>
    public Func<Settings, Task<bool>>? SetSettingsHandler { get; set; }

    /// <summary>
    /// What the daemon says about itself, including where it keeps its settings.
    /// </summary>
    /// <remarks>
    /// Populated by default since #828, because a session now asks this to find out where to persist. A
    /// null AppInfo is a daemon that will not say — a real state, and one a test can still ask for, but
    /// not the ordinary one, and leaving it as the default made every save in every session-level test a
    /// write to nowhere.
    /// </remarks>
    public AppInfo? AppInfo { get; set; } =
        new() { AppDataDirectory = "A", SettingsFile = DefaultSettingsFile };

    /// <summary>Where this fake's daemon claims to keep its settings.</summary>
    public const string DefaultSettingsFile = "A/settings.json";
    public JArray Tablets { get; set; } = [];
    public JArray Devices { get; set; } = [];
    public int? ServerProcessId { get; set; } = 1;

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
    private int _incarnation;
    private bool _dropped;

    /// <summary>
    /// Which channel this is, or 0 when there is none.
    ///
    /// Zero after a drop, as the real client does -- it clears the channel value when its RPC
    /// disconnects. A fake that kept reporting the old number could not express "the connection this
    /// notification is about has already gone", which is the state obsolete queued work has to notice.
    /// </summary>
    public int Incarnation => _dropped ? 0 : _incarnation;

    /// <summary>
    /// A new channel, as the real client establishes one: the incarnation moves FIRST, then the event.
    ///
    /// That order is the whole of what is being modelled. The real client assigns its RPC channel, and
    /// only afterwards raises Connected — so there is a window in which sends already reach the new daemon
    /// and nobody has been told. A fake that bumped the two together could not express it.
    /// </summary>
    public void Reconnect()
    {
        _dropped = false;
        _incarnation++;
        RaiseConnected();
    }

    /// <summary>The channel is replaced, and nothing announces it. The window, on its own.</summary>
    public void ReconnectSilently()
    {
        _dropped = false;
        _incarnation++;
    }

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

        /// <summary>
        /// Whether this binding can still carry a send.
        /// </summary>
        /// <remarks>
        /// <b>Disposal counts, and it did not.</b> This checked incarnation alone, so a send issued after
        /// the transport had been disposed was accepted — where the real client's binding checks
        /// <c>rpc.IsDisposed</c> and refuses. A test about what a host's disposal does to an operation in
        /// flight could therefore assert a successful apply that production would never produce. That is
        /// the second time this fake has been more permissive than the thing it models; the first was
        /// reporting a process id after disposal.
        /// </remarks>
        private bool Live => !daemon.IsDisposed && incarnation == daemon.Incarnation;

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

        if (ok) Settings = Readback?.Invoke(settings) ?? SettingsCodec.Clone(settings);   // the daemon now holds it
        return ok;
    }

    /// <summary>
    /// When set, replaces <see cref="GetAppInfoAsync"/> entirely — so a test can hold the metadata call
    /// open, fail it, or answer differently on a second attempt.
    /// </summary>
    /// <remarks>
    /// A session asks this to find out where to persist (#828), and the interesting cases are all about
    /// <em>when</em> the answer arrives: before or after another daemon connects, and whether a failed
    /// lookup can recover. An always-immediate fake can express none of them.
    /// </remarks>
    public Func<Task<AppInfo?>>? GetAppInfoHandler { get; set; }

    /// <summary>How many times the metadata call was made, so a test can see a lookup it did not expect.</summary>
    public int GetAppInfoCalls { get; private set; }

    public Task<AppInfo?> GetAppInfoAsync()
    {
        Calls.Add(nameof(GetAppInfoAsync));
        GetAppInfoCalls++;
        return GetAppInfoHandler?.Invoke() ?? Task.FromResult(AppInfo);
    }

    /// <summary>An <see cref="AppInfo"/> reporting <paramref name="settingsFile"/>.</summary>
    public static AppInfo Reporting(string settingsFile) =>
        new() { AppDataDirectory = "A", SettingsFile = settingsFile };

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
    /// <summary>
    /// Which process is answering, and nothing once this has been disposed.
    /// </summary>
    /// <remarks>
    /// It used to answer after disposal, which no real transport does — the pipe is gone and there is
    /// nobody to ask. A test about what an exit stops could therefore resolve its target from a closed
    /// session and still get the right answer, which is precisely the mistake it existed to catch.
    /// </remarks>
    public int? GetServerProcessId() => IsDisposed || Incarnation == 0 ? null : ServerProcessId;

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

    /// <summary>
    /// Raise the Connected event <b>without</b> establishing a channel.
    ///
    /// Not a state the real client can be in — it assigns its channel before raising — so this exists
    /// only for a test that wants the bare event. Use <see cref="Reconnect"/> to model a connection;
    /// a notification with no channel behind it is now correctly discarded as obsolete.
    /// </summary>
    public void RaiseConnected() => Connected?.Invoke();

    /// <summary>Raise a transport drop. The channel goes with it, as the real client's does.</summary>
    public void RaiseDisconnected()
    {
        _dropped = true;
        Disconnected?.Invoke();
    }

    /// <summary>Raise a tablet add/remove push.</summary>
    public void RaiseTabletsChanged() => TabletsChanged?.Invoke();

    /// <summary>Suppresses the unused-event warning for the two events no current test drives.</summary>
    public void RaiseDeviceReport(JObject report) => DeviceReport?.Invoke(report);

    /// <inheritdoc cref="RaiseDeviceReport"/>
    public void RaiseLog(LogMessage message) => LogReceived?.Invoke(message);
}
