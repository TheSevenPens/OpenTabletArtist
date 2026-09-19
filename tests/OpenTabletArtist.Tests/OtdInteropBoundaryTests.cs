using System;
using System.Linq;
using System.Reflection;
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
/// #807 Phase 6 asks for checks of this kind. These are the ones the Phase 4 move makes true, pulled
/// forward so the property is pinned at the commit that establishes it rather than several phases later.
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
    /// the same bypass by a longer route.
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
