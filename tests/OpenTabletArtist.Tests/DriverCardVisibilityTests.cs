using System.Threading.Tasks;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What the Daemon page says about the daemon, and what its OpenTabletDriver button does (#1043).
/// </summary>
///
/// <remarks>
/// <para>
/// This file began as a test that the driver card was on screen exactly when it had something in it
/// (#725, #900), after a composition bug in which both halves were unit-tested and correct and nothing
/// tested the two together. Most of that went with the daemon picker, and the rest with the page's
/// redesign: the card is now only the macOS install offer, and OpenTabletDriver's own window has a button
/// that is always on the page instead of one that came and went with the connected daemon.
/// </para>
/// <para>
/// So what is asserted here is the plain status the page shows and the one place the button can fail.
/// </para>
/// </remarks>
public class DriverCardVisibilityTests
{
    /// <summary>A card with nothing to say is not on screen. On Windows the install offer is never made
    /// (it is macOS-only), and an empty bordered card reads as something that failed to load.</summary>
    [Fact]
    public void WithNothingToInstall_TheCardIsHidden()
    {
        using var page = PageOn(new FakeConnectionState { IsConnected = true });

        if (System.OperatingSystem.IsMacOS()) return;   // the install offer exists there
        Assert.False(page.ShowDriverCard);
    }

    [Theory]
    [InlineData(true, false, "Connected")]
    [InlineData(false, true, "Running, but not connected: wrong version")]
    public void TheStatusLineFollowsWhatTheDaemonIsDoing(bool connected, bool refused, string expected)
    {
        var connection = new FakeConnectionState { IsConnected = connected, IsDaemonRefused = refused };
        using var page = PageOn(connection);

        Assert.Equal(expected, page.StatusText);
    }

    /// <summary>Not connected and not refused, the line falls back to whether a process exists at all. That
    /// is read off the machine, so the test asks the page what it saw rather than assuming no daemon is
    /// running on whatever box this runs on.</summary>
    [Fact]
    public void WithNothingConnected_TheLineSaysWhetherAProcessIsThere()
    {
        using var page = PageOn(new FakeConnectionState());

        Assert.Equal(page.Process.IsRunning ? "Running, not connected" : "Not running", page.StatusText);
    }

    /// <summary>The status follows the connection as it changes, rather than being decided once.</summary>
    [Fact]
    public void TheStatusLineIsRaisedWhenTheConnectionChanges()
    {
        var connection = new FakeConnectionState();
        using var page = PageOn(connection);
        var raised = 0;
        page.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DaemonViewModel.StatusText)) raised++; };

        connection.IsDaemonRefused = true;
        connection.IsConnected = true;

        Assert.True(raised >= 2, "both changes move the line, and the page has to be told");
    }

    /// <summary>A refused daemon is described too: it is the one the reader is being told about.</summary>
    [Theory]
    [InlineData(DaemonOwnershipKind.Owned, "Bundled with OpenTabletArtist")]
    [InlineData(DaemonOwnershipKind.External, "Another OpenTabletDriver, not the one OpenTabletArtist starts")]
    [InlineData(DaemonOwnershipKind.Unknown, "Couldn't be identified")]
    public void TheOwnerIsNamedForAConnectedOrRefusedDaemon(DaemonOwnershipKind kind, string expected)
    {
        var connection = new FakeConnectionState
        {
            IsDaemonRefused = true,
            Ownership = kind switch
            {
                DaemonOwnershipKind.Owned => OpenTabletArtist.Domain.DaemonOwnership.Owned,
                DaemonOwnershipKind.External => OpenTabletArtist.Domain.DaemonOwnership.External,
                _ => OpenTabletArtist.Domain.DaemonOwnership.Unknown,
            },
        };
        using var page = PageOn(connection);

        Assert.True(page.HasOwnerText);
        Assert.Equal(expected, page.OwnerText);
    }

    [Fact]
    public void NoDaemonMeansNoOwnerLine()
    {
        using var page = PageOn(new FakeConnectionState());

        Assert.False(page.HasOwnerText);
    }

    public enum DaemonOwnershipKind { Owned, External, Unknown }

    /// <summary>The one way the button can fail: nothing to open. It says so in a dialog, naming where it
    /// looked, and opens nothing.</summary>
    [Fact]
    public async Task WhenNoOpenTabletDriverWindowIsFound_AnErrorSaysSo()
    {
        var shown = new System.Collections.Generic.List<(string Title, string Message)>();
        var nowhere = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ota-nowhere-{System.Guid.NewGuid():N}");
        using var page = new DaemonViewModel(new DaemonStatusViewModel(new FakeConnectionState()),
            showError: (title, message) => { shown.Add((title, message)); return Task.CompletedTask; },
            baseDirectory: nowhere);

        await page.OpenOtdUxCommand.ExecuteAsync(null);

        var error = Assert.Single(shown);
        Assert.Equal("Can't open OpenTabletDriver", error.Title);
        Assert.Contains(OpenTabletArtist.Domain.DaemonExePaths.UxExeName, error.Message);
    }

    private static DaemonViewModel PageOn(FakeConnectionState connection) =>
        new(new DaemonStatusViewModel(connection));
}
