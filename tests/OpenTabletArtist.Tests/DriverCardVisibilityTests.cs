using System;
using System.IO;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// That the driver card is on screen exactly when it has something in it (#725, #900).
/// </summary>
///
/// <remarks>
/// <para>
/// This exists because of a composition bug, and the bug is worth keeping in mind even though the
/// feature that carried it is gone. The card's condition was <c>ShowLocateCard || ShowInstallCard</c>,
/// and the offer it was meant to hold applied in exactly the state that made both false. So the button
/// rendered never, and the sentence saying the offer could not be taken yet rendered always. Both
/// halves were unit-tested and correct; nothing tested the two together.
/// </para>
/// <para>
/// Most of what this file used to cover went with the daemon picker (#daemon-bundled-only): OTA launches
/// only the copy it ships, so there is no chosen location, nothing to clear, and no way back to the
/// bundled copy to offer. What is left is the same question asked of a much smaller card.
/// </para>
/// </remarks>
public class DriverCardVisibilityTests
{
    /// <summary>A card with nothing to say is not on screen.</summary>
    /// <remarks>
    /// On Windows the install offer is never made (it is macOS-only), so a daemon that ships no settings
    /// window of its own leaves the card empty. An empty bordered card reads as something that failed to
    /// load.
    /// </remarks>
    [Fact]
    public void WithNothingToOffer_TheCardIsHidden()
    {
        using var install = new TempInstall();
        using var page = PageOn(install.Daemon);

        Assert.False(page.CanOpenOtdUx, "this daemon ships no settings window");
        Assert.False(page.ShowDriverCard);
    }

    /// <summary>And a card with one thing in it is.</summary>
    [Fact]
    public void WhenTheDriverShipsItsOwnWindow_TheCardIsOnScreen()
    {
        using var install = new TempInstall();
        install.Add(DaemonExePaths.UxExeName);
        using var page = PageOn(install.Daemon);

        Assert.True(page.CanOpenOtdUx);
        Assert.True(page.ShowDriverCard, "the button has nowhere to render without its card");
    }

    private static DaemonViewModel PageOn(string daemonPath)
    {
        var connection = new FakeConnectionState
        {
            IsConnected = true,
            DaemonSourcePath = daemonPath,
        };
        return new DaemonViewModel(new DaemonStatusViewModel(connection));
    }

    /// <summary>A folder holding a daemon, and whatever else a test wants beside it.</summary>
    private sealed class TempInstall : IDisposable
    {
        private readonly string _root;

        public TempInstall()
        {
            _root = Path.Combine(Path.GetTempPath(), $"ota-drivercard-{Guid.NewGuid():N}");
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
            catch (IOException) { /* a temp folder that outlives the test is not a failure */ }
        }
    }
}
