using System;
using System.Linq;
using System.Reflection;
using OpenTabletDriver.Desktop;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// Nothing outside this library can write the daemon's settings except through the mediated session.
/// </summary>
///
/// <remarks>
/// <para>
/// Restored after the simplification removed <c>OtdInteropBoundaryTests</c> (#919). Most of what that
/// class asserted was about machinery that is gone and should stay gone; these three claims are not, and
/// they are architectural rather than incidental: the raw transport, the settings binding and the file
/// writer are the library's own, and the borrowed capabilities object must not be a route back to any of
/// them.
/// </para>
/// <para>
/// Accessibility already enforces this — the compiler will not let application code name these types. The
/// value here is that a later change which widens one of them fails a test instead of passing quietly.
/// Reflection is a regression guard, not a proof against every conceivable bypass; a determined caller
/// with a file path is outside what any of this can reach.
/// </para>
/// </remarks>
public class WriteBoundaryTests
{
    private static readonly Assembly Library = typeof(OtdSession).Assembly;

    [Theory]
    [InlineData("OtdInterop.IDaemonTransport")]
    [InlineData("OtdInterop.IDaemonSettingsChannel")]
    [InlineData("OtdInterop.IDaemonSettingsBinding")]
    [InlineData("OtdInterop.ISettingsFileStore")]
    [InlineData("OtdInterop.SettingsFileStore")]
    [InlineData("OtdInterop.DaemonClient")]
    [InlineData("OtdInterop.SettingsCoordinator")]
    public void TheRawWritersAreNotVisibleOutsideTheLibrary(string name)
    {
        var type = Library.GetType(name, throwOnError: false);
        Assert.True(type is not null, $"{name} was renamed or removed; this guard no longer checks it");
        Assert.False(type!.IsVisible, $"{name} is reachable from outside the library");
    }

    /// <summary>
    /// No exported type offers a settings write that skips <see cref="IOtdSettingsSession"/>.
    /// </summary>
    /// <remarks>
    /// Named by shape rather than by a list, so a new public type carrying a writer is caught by existing
    /// code rather than by somebody remembering to extend a list.
    /// </remarks>
    [Fact]
    public void NoExportedTypeOffersAnUnmediatedSettingsWrite()
    {
        var offenders = Library.GetExportedTypes()
            .Where(t => t != typeof(IOtdSettingsSession))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => (Type: t, Method: m)))
            // Not property accessors. A record that carries a Settings has an init setter, and
            // PreparedSettings — a detached snapshot handed to the caller — is exactly that. Holding a
            // copy of settings is the opposite of writing them somewhere.
            .Where(x => !x.Method.IsSpecialName
                        && x.Method.GetParameters().Any(p => p.ParameterType == typeof(Settings))
                        && (x.Method.Name.Contains("Set", StringComparison.Ordinal)
                            || x.Method.Name.Contains("Save", StringComparison.Ordinal)
                            || x.Method.Name.Contains("Write", StringComparison.Ordinal)))
            .Select(x => $"{x.Type.Name}.{x.Method.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(", ", offenders));
    }

    /// <summary>The borrowed capabilities object is not a way back to the connection it wraps.</summary>
    /// <remarks>
    /// It is handed to the host for reads, watches and plugin work while the library keeps the transport.
    /// Casting it back would hand over the connection itself; disposing it would let a borrower end a
    /// connection it does not own.
    /// </remarks>
    [Fact]
    public void TheBorrowedCapabilitiesCannotBeCastBackToTheConnection()
    {
        var daemon = new FakeDaemonTransport();
        using var session = OtdSession.ForTesting(daemon, null, NullOtdLog.Instance, new FakeProcessLocator());
        object capabilities = session.Capabilities;

        Assert.IsNotAssignableFrom<IDisposable>(capabilities);
        Assert.False(capabilities is IDaemonTransport, "the capabilities object exposes the transport");
        Assert.False(capabilities is IDaemonSettingsChannel, "the capabilities object exposes the settings channel");
        Assert.NotSame(daemon, capabilities);
    }
}
