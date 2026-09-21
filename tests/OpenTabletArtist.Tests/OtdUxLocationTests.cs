using System;
using System.IO;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Finding OpenTabletDriver's own settings window beside the daemon OTA is connected to.
/// </summary>
///
/// <remarks>
/// Derived from the connected daemon rather than searched for, because the button's promise is to open
/// the window for <em>this</em> driver. A machine can hold several OpenTabletDriver copies, and opening
/// another one's UX would point the artist at a daemon that is not the one this page is about.
/// </remarks>
public class OtdUxLocationTests
{
    [Fact]
    public void TheUxBesideTheDaemonIsFound()
    {
        using var install = new TempInstall();
        var ux = install.Add(DaemonExePaths.UxExeName);

        Assert.Equal(ux, DaemonExePaths.UxBeside(install.Daemon));
    }

    /// <summary>A driver that ships no UX offers nothing, rather than a button that opens nothing.</summary>
    /// <remarks>
    /// The ordinary case for a daemon running from a build tree, and for packaged installs that split
    /// the UX into its own component.
    /// </remarks>
    [Fact]
    public void ADriverWithoutAUxOffersNothing()
    {
        using var install = new TempInstall();

        Assert.Null(DaemonExePaths.UxBeside(install.Daemon));
    }

    /// <summary>A UX belonging to a different install is not offered for this daemon.</summary>
    /// <remarks>
    /// The reason the lookup is anchored to the daemon at all. Both of these are real OpenTabletDriver
    /// installations; only one of them is the one answering.
    /// </remarks>
    [Fact]
    public void AnotherInstallsUxIsNotOffered()
    {
        using var connected = new TempInstall();
        using var elsewhere = new TempInstall();
        elsewhere.Add(DaemonExePaths.UxExeName);

        Assert.Null(DaemonExePaths.UxBeside(connected.Daemon));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoDaemonPathMeansNoUx(string? path) => Assert.Null(DaemonExePaths.UxBeside(path));

    /// <summary>An unusable path is simply no UX, not a throw.</summary>
    /// <remarks>
    /// A recorded daemon location outlives the machine it was recorded on, so this reads whatever was
    /// stored last. Answering "no UX" is right; faulting the page is not.
    /// </remarks>
    [Fact]
    public void AnUnusablePathIsJustNoUx() =>
        Assert.Null(DaemonExePaths.UxBeside("\0not a path\0"));

    private sealed class TempInstall : IDisposable
    {
        private readonly string _root;

        public TempInstall()
        {
            _root = Path.Combine(Path.GetTempPath(), $"ota-otdux-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            Daemon = Add(DaemonExePaths.DaemonExeName);
        }

        public string Daemon { get; }

        public string Add(string fileName)
        {
            var path = Path.Combine(_root, fileName);
            File.WriteAllText(path, "");
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch { /* a temp folder that outlives the test is not worth failing it over */ }
        }
    }
}
