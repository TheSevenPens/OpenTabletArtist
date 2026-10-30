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

    // --- The always-on button's lookup (#1043) ---------------------------------------------------------

    [Fact]
    public void WithADaemonRunning_ItsUxIsTheOneFound()
    {
        using var running = new TempInstall();
        var ux = running.Add(DaemonExePaths.UxExeName);
        using var app = new TempApp(withUx: true);

        var found = DaemonExePaths.FindUx(running.Daemon, app.BaseDirectory);

        Assert.Equal(ux, found.Path);
    }

    /// <summary>A running daemon with no window beside it is an answer, not a cue to open another install's.</summary>
    /// <remarks>
    /// The control that makes the rule visible: the bundled copy here DOES have a window, so a lookup that
    /// fell back to it would find one, and this asserts it does not.
    /// </remarks>
    [Fact]
    public void WithADaemonRunningThatShipsNoUx_TheBundledOneIsNotOpenedInItsPlace()
    {
        using var running = new TempInstall();
        using var app = new TempApp(withUx: true);

        var found = DaemonExePaths.FindUx(running.Daemon, app.BaseDirectory);

        Assert.Null(found.Path);
        var searched = Assert.Single(found.Searched);
        Assert.Equal(Path.GetDirectoryName(running.Daemon), searched.Folder);
        Assert.Contains("running", searched.Where);
    }

    [Fact]
    public void WithNothingRunning_TheBundledUxIsUsed()
    {
        using var app = new TempApp(withUx: true);

        var found = DaemonExePaths.FindUx("", app.BaseDirectory);

        Assert.Equal(Path.Combine(app.BundledFolder, DaemonExePaths.UxExeName), found.Path);
    }

    [Fact]
    public void WithNothingRunningAndNoUxAnywhere_ItSaysWhereItLooked()
    {
        using var app = new TempApp(withUx: false);

        var found = DaemonExePaths.FindUx(null, app.BaseDirectory);

        Assert.Null(found.Path);
        var message = found.NotFoundMessage();
        Assert.Contains(DaemonExePaths.UxExeName, message);
        Assert.Contains(app.BundledFolder, message);
        Assert.Contains("ships with", message);
    }

    /// <summary>With no folder to look in, the message says so rather than listing nothing.</summary>
    [Fact]
    public void WithNowhereToLook_TheMessageSaysThereIsNowhere()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"ota-nowhere-{Guid.NewGuid():N}");

        var found = DaemonExePaths.FindUx(null, empty);

        Assert.Null(found.Path);
        Assert.Empty(found.Searched);
        Assert.Contains("nowhere to look", found.NotFoundMessage());
    }

    /// <summary>An app folder as a release lays it out: <c>&lt;app&gt;/Daemon/&lt;exe&gt;</c>.</summary>
    private sealed class TempApp : IDisposable
    {
        private readonly string _root;

        public TempApp(bool withUx)
        {
            _root = Path.Combine(Path.GetTempPath(), $"ota-otdapp-{Guid.NewGuid():N}");
            BundledFolder = Path.GetDirectoryName(DaemonExePaths.BundledPath(_root))!;
            Directory.CreateDirectory(BundledFolder);
            File.WriteAllText(DaemonExePaths.BundledPath(_root), "");
            if (withUx) File.WriteAllText(Path.Combine(BundledFolder, DaemonExePaths.UxExeName), "");
        }

        public string BaseDirectory => _root;
        public string BundledFolder { get; }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch { /* a temp folder that outlives the test is not worth failing it over */ }
        }
    }

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
