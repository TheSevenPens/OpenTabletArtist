using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Plugin.Logging;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The library's public surface, asserted rather than assumed (#807).
///
/// The separation is worth having only if it is enforced. Every one of these could be undone by a
/// well-meaning edit that made one type public to fix a compile error, and nothing else in the suite
/// would notice: the app would still build, and the bypass would be back.
///
/// <b>Regression checks, not completeness proofs.</b> They catch the exact names and visibility changes
/// they name. A renamed raw write, a public delegate or property returning one, a generic wrapper, a
/// ref/out parameter, or a capability reachable through a type defined elsewhere would all pass. The
/// real check is a reviewed snapshot of the public API plus behavioural composition tests — #807 Phase 6
/// asks for that, and this is not it. These are pinned here because Phase 4 is where the properties
/// first become true.
/// </summary>
public class OtdInteropBoundaryTests
{
    private static Assembly Library => typeof(IOtdSettingsSession).Assembly;

    /// <summary>
    /// Nothing a host can reach will write settings to the daemon.
    ///
    /// That write bypasses ordering, the session check, policy, the format guard, the no-op guard and the
    /// persistence bookkeeping — every protection <see cref="IOtdSettingsSession"/> exists to apply. It
    /// used to sit on the same interface as the device list and the log stream, so every class that
    /// wanted one of those held it too.
    /// </summary>
    [Fact]
    public void NoPublicTypeExposesADirectSettingsWrite()
    {
        var offenders = Library.GetExportedTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => (Type: t, Method: m)))
            .Where(x => x.Method.Name is "SetSettingsAsync" or "GetSettingsAsync")
            .Select(x => $"{x.Type.Name}.{x.Method.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The connection a host holds carries the device list, the log, the plugin verbs and the lifecycle —
    /// and no way to read or write settings. Asserted on the composed interface, including what it
    /// inherits, because inheriting the verbs back would be the easy way to reintroduce them.
    /// </summary>
    [Fact]
    public void TheHostFacingConnection_HasNoSettingsVerbs()
    {
        var names = typeof(IDaemonTransport).GetMethods()
            .Concat(typeof(IDaemonTransport).GetInterfaces().SelectMany(i => i.GetMethods()))
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain("SetSettingsAsync", names);
        Assert.DoesNotContain("GetSettingsAsync", names);
        // Still the whole of what the host legitimately needs, so this is a narrowing and not a removal.
        Assert.Contains("GetTabletsAsync", names);
        Assert.Contains("GetCurrentLogAsync", names);
        Assert.Contains("DownloadPluginAsync", names);
    }

    /// <summary>
    /// The settings session cannot be constructed from outside; it comes from the library's factory.
    ///
    /// A host able to build its own would be able to build one over a writer of its choosing, which is
    /// the same bypass by a longer route. This says nothing about how many the factory will build —
    /// see <c>OneConnectionGetsOneSettingsAuthority</c>, which is the behaviour that establishes that.
    /// </summary>
    [Fact]
    public void TheSettingsSessionImplementation_IsNotPublic()
    {
        Assert.DoesNotContain(Library.GetExportedTypes(),
            t => t.GetInterfaces().Contains(typeof(IOtdSettingsSession)));

        // And the supported way in is still there.
        Assert.Contains(typeof(OtdSettingsSession).GetMethods(BindingFlags.Public | BindingFlags.Static),
            m => m.Name == nameof(OtdSettingsSession.Create));
    }

    /// <summary>
    /// One connection gets one settings authority, and a second is refused.
    ///
    /// This is behaviour, not visibility, and it is the gap making the implementation internal did NOT
    /// close: a host could not build its own session, but it could ask the factory for two over the same
    /// connection. Two sessions are not two views of the same thing -- each has its own mutation gate,
    /// session generation, retry state and baseline -- so neither sees what the other is doing.
    ///
    /// Demonstrated as the loss it actually is rather than as an exception message: with the refusal
    /// removed, the assertion below sees TWO settings RPCs in flight against one daemon at once, which is
    /// precisely what the serialization exists to prevent.
    /// </summary>
    [Fact]
    public async Task OneConnectionGetsOneSettingsAuthority()
    {
        var daemon = new FakeDaemonTransport();
        var inFlight = 0;
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => { inFlight++; return held.Task; };

        var first = Session(daemon);

        var refused = Assert.Throws<InvalidOperationException>(() => Session(daemon));
        Assert.Contains("already has a settings session", refused.Message);

        // And the one session that exists still serializes, which is the property being protected.
        var a = first.ApplyLiveOnlyAsync(Tablet("X"));
        var b = first.ApplyLiveOnlyAsync(Tablet("Y"));
        await Task.Yield();
        Assert.Equal(1, inFlight);

        held.SetResult(true);
        await Task.WhenAll(a, b);
    }

    /// <summary>A connection the library did not make is refused, rather than yielding a session that
    /// silently cannot write.</summary>
    [Fact]
    public void AConnectionTheLibraryDidNotMake_IsRefused()
    {
        var refused = Assert.Throws<ArgumentException>(() => Session(new HostsOwnTransport()));
        Assert.Contains(nameof(DaemonTransport.Create), refused.Message);
    }

    private static IOtdSettingsSession Session(IDaemonTransport daemon) =>
        OtdSettingsSession.Create(daemon, () => "A/settings.json", () => true, _ => { },
            NullOtdLog.Instance, OpenTabletArtist.Services.OtaSettingsPolicy.Instance);

    private static Settings Tablet(string name) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = name } } };

    /// <summary>
    /// What a consumer can build: the public connection interface and nothing behind it. Every member
    /// throws, because none should ever be reached -- the factory refuses this before using it.
    /// </summary>
    private sealed class HostsOwnTransport : IDaemonTransport
    {
        public event Action? Connected { add { } remove { } }
        public event Action? Disconnected { add { } remove { } }
        public event Action? TabletsChanged { add { } remove { } }
        public event Action<JObject>? DeviceReport { add { } remove { } }
        public event Action<LogMessage>? LogReceived { add { } remove { } }

        public bool AutoReconnect { get; set; }

        public Task ConnectAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<AppInfo?> GetAppInfoAsync() => throw new NotSupportedException();
        public Task<JArray> GetTabletsAsync() => throw new NotSupportedException();
        public Task<JArray> GetDevicesAsync() => throw new NotSupportedException();
        public int? GetServerProcessId() => throw new NotSupportedException();
        public Task SetTabletDebugAsync(bool enabled) => throw new NotSupportedException();
        public Task<List<LogMessage>> GetCurrentLogAsync() => throw new NotSupportedException();
        public Task<bool> DownloadPluginAsync(PluginMetadata metadata) => throw new NotSupportedException();
        public Task<bool> UninstallPluginAsync(string directory) => throw new NotSupportedException();
        public Task LoadPluginsAsync() => throw new NotSupportedException();
        public void Dispose() { }
    }

    /// <summary>
    /// The library depends on nothing of the app's, and on no UI framework. A reference the other way
    /// would undo the whole separation quietly — it compiles, and only the dependency graph shows it.
    /// </summary>
    [Fact]
    public void TheLibraryReferencesNeitherTheAppNorAvalonia()
    {
        var referenced = Library.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain(referenced, n => n.StartsWith("OpenTabletArtist", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.StartsWith("SkiaSharp", StringComparison.Ordinal));
    }
}
