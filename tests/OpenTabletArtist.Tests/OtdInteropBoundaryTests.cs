using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
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
    /// What a host can reach carries the device list, the log and the plugin verbs — and no way to read
    /// or write settings, and no way to end the connection. Asserted through what it inherits too,
    /// because inheriting something back is the easy way to reintroduce it.
    /// </summary>
    [Fact]
    public void TheHostFacingCapabilities_CarryNoSettingsOrOwnership()
    {
        var t = typeof(IDaemonCapabilities);
        var names = t.GetMethods()
            .Concat(t.GetInterfaces().SelectMany(i => i.GetMethods()))
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain("SetSettingsAsync", names);
        Assert.DoesNotContain("GetSettingsAsync", names);
        // Ownership: closing, reconnecting, or deciding whether to keep reconnecting.
        Assert.DoesNotContain("Dispose", names);
        Assert.DoesNotContain("ConnectAsync", names);
        Assert.DoesNotContain("get_AutoReconnect", names);
        Assert.DoesNotContain(t.GetInterfaces(), i => i == typeof(IDisposable));

        // Still the whole of what a page legitimately needs, so this is a narrowing and not a removal.
        Assert.Contains("GetTabletsAsync", names);
        Assert.Contains("GetCurrentLogAsync", names);
        Assert.Contains("DownloadPluginAsync", names);
        Assert.Contains("SetTabletDebugAsync", names);
    }

    /// <summary>
    /// The capabilities object is not the connection wearing a smaller interface.
    ///
    /// Narrowing by returning the same instance as a narrower type narrows nothing: anything holding it
    /// casts back to the full transport, to <see cref="IDisposable"/>, and closes the connection out from
    /// under the session. This is the assertion that the forwarding object is real.
    /// </summary>
    [Fact]
    public void TheCapabilitiesObject_CannotBeCastBackToTheConnection()
    {
        var daemon = new FakeDaemonTransport();
        var capabilities = FakeSession.Over(daemon).Capabilities;

        Assert.NotSame(daemon, capabilities);
        Assert.IsNotAssignableFrom<IDaemonTransport>(capabilities);
        Assert.IsNotAssignableFrom<IDisposable>(capabilities);

        // And it really is wired to that connection, not to nothing.
        capabilities.SetTabletDebugAsync(true);
        Assert.Equal(1, daemon.DebugCalls);
    }

    /// <summary>The connection itself is not something a host can name at all.</summary>
    [Fact]
    public void TheConnectionType_IsNotPublic()
    {
        Assert.DoesNotContain(Library.GetExportedTypes(), t => t.Name == nameof(IDaemonTransport));
    }

    /// <summary>
    /// The settings session cannot be constructed from outside; it comes from <see cref="OtdSession"/>.
    ///
    /// A host able to build its own would be able to build one over a writer of its choosing, which is
    /// the same bypass by a longer route. This says nothing about how many a host can obtain — see
    /// <c>OneConnectionGetsOneSettingsAuthority</c>, which is the behaviour that settles that.
    /// </summary>
    [Fact]
    public void TheSettingsSessionImplementation_IsNotPublic()
    {
        Assert.DoesNotContain(Library.GetExportedTypes(),
            t => t.GetInterfaces().Contains(typeof(IOtdSettingsSession)));

        // And the supported way in is still there.
        Assert.Contains(typeof(OtdSession).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            m => m.Name == nameof(OtdSession.OpenSettings));
    }

    /// <summary>
    /// One connection gets one settings authority, and a second is refused.
    ///
    /// This is behaviour, not visibility, and it is the gap making the implementation internal did NOT
    /// close: a host could not build its own session, but it could ask the old free-standing factory for
    /// two over the same connection. Two sessions are not two views of the same thing -- each has its own
    /// mutation gate, session generation, retry state and baseline -- so neither sees what the other is
    /// doing.
    ///
    /// Asserted as the loss it actually is rather than as an exception message: the surviving session
    /// serializes, and with the refusal removed the assertion below sees TWO settings RPCs in flight
    /// against one daemon at once, which is what the serialization exists to prevent.
    /// </summary>
    [Fact]
    public async Task OneConnectionGetsOneSettingsAuthority()
    {
        var daemon = new FakeDaemonTransport();
        var inFlight = 0;
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => { inFlight++; return held.Task; };

        var session = FakeSession.Over(daemon);
        var settings = Open(session);

        var refused = Assert.Throws<InvalidOperationException>(() => Open(session));
        Assert.Contains("already open", refused.Message);

        var a = settings.ApplyLiveOnlyAsync(Tablet("X"));
        var b = settings.ApplyLiveOnlyAsync(Tablet("Y"));
        await Task.Yield();
        Assert.Equal(1, inFlight);

        held.SetResult(true);
        await Task.WhenAll(a, b);
    }

    /// <summary>
    /// A host cannot bring its own connection, so it cannot bring one without a settings channel.
    ///
    /// This used to be a runtime check -- the library was handed a connection and cast it to the internal
    /// channel -- because the public connection interface can be implemented by anyone. Owning the
    /// connection makes it a compile-time fact instead, so what this asserts is that nothing offers the
    /// old way back in.
    /// </summary>
    [Fact]
    public void NoPublicEntryPointTakesAConnectionFromOutside()
    {
        var offenders = Library.GetExportedTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Concat(t.GetConstructors().Cast<MethodBase>())
                .Select(m => (Type: t, Method: m)))
            .Where(x => x.Method.GetParameters().Any(p => p.ParameterType == typeof(IDaemonTransport)))
            .Select(x => $"{x.Type.Name}.{x.Method.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// A disposed session starts nothing new, and says so.
    ///
    /// Its connection is gone, so there is nothing for an authority to be an authority over. Returning
    /// one anyway would hand back something whose every operation fails for a reason the caller cannot
    /// see from the object it was given.
    /// </summary>
    [Fact]
    public void ADisposedSession_RefusesToOpenSettings()
    {
        var session = FakeSession.Over(new FakeDaemonTransport());
        session.Dispose();

        var refused = Assert.Throws<InvalidOperationException>(() => Open(session));
        Assert.Contains("disposed", refused.Message);
    }

    /// <summary>
    /// What already happened stays readable after disposal. A host asking whether a change went unsaved
    /// is asking about the past, and the honest answer is available -- refusing it would replace a fact
    /// with an exception at exactly the moment the fact matters.
    /// </summary>
    [Fact]
    public async Task ADisposedSession_StillAnswersForWhatAlreadyHappened()
    {
        var daemon = new FakeDaemonTransport();
        var session = FakeSession.Over(daemon);
        var settings = Open(session);
        await settings.ApplyAndSaveAsync(Tablet("T"));

        session.Dispose();

        Assert.Equal("T", settings.GetCurrent()!.Settings.Profiles[0].Tablet);
    }

    /// <summary>Disposing twice is not an error; a host tearing down in an unexpected order should not
    /// have to know who got there first.</summary>
    [Fact]
    public void DisposingASessionTwice_IsHarmless()
    {
        var daemon = new FakeDaemonTransport();
        var session = FakeSession.Over(daemon);

        session.Dispose();
        session.Dispose();

        Assert.True(daemon.IsDisposed);
    }

    private static IOtdSettingsSession Open(OtdSession session) =>
        session.OpenSettings(() => "A/settings.json", () => true, _ => { });

    private static Settings Tablet(string name) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = name } } };

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
