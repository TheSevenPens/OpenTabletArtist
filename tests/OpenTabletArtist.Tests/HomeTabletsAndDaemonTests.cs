using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The two pieces of Home that moved with the two-column layout (#1050): the tablet list split into
/// plugged-in cards and a collapsed row of remembered ones, and the daemon in one line.
/// </summary>
public class HomeTabletsAndDaemonTests
{
    // --- Tablets ----------------------------------------------------------------------------------------

    [Fact]
    public void TheListSplitsIntoPluggedInAndRemembered()
    {
        var overview = Overview(Tablet("A", true), Tablet("B", false), Tablet("C", false), Tablet("D", true));

        Assert.Equal(["A", "D"], overview.Connected.Select(t => t.Name));
        Assert.Equal(["B", "C"], overview.Remembered.Select(t => t.Name));
        Assert.True(overview.HasConnected);
        Assert.True(overview.HasRemembered);
    }

    /// <summary>The reason for the split: a machine that has met many tablets must not push the rest of Home
    /// out of sight. The remembered ones stay closed until asked.</summary>
    [Fact]
    public void RememberedTablets_StartClosed_AndOpenOnRequest()
    {
        var overview = Overview(Tablet("A", true), Tablet("B", false));
        Assert.False(overview.IsRememberedExpanded);
        Assert.Equal("show", overview.RememberedHint);

        overview.ToggleRememberedCommand.Execute(null);

        Assert.True(overview.IsRememberedExpanded);
        Assert.Equal("hide", overview.RememberedHint);

        overview.ToggleRememberedCommand.Execute(null);
        Assert.False(overview.IsRememberedExpanded);
    }

    [Theory]
    [InlineData(1, "1 remembered tablet")]
    [InlineData(2, "2 remembered tablets")]
    [InlineData(8, "8 remembered tablets")]
    public void TheRememberedRowCountsThem(int remembered, string label)
    {
        var tablets = Enumerable.Range(0, remembered).Select(i => Tablet($"R{i}", false)).Append(Tablet("A", true)).ToArray();

        Assert.Equal(label, Overview(tablets).RememberedLabel);
    }

    /// <summary>Known tablets with none plugged in is said in words, not shown as an empty list. With no tablets
    /// at all it is the other empty state, so this stays false.</summary>
    [Fact]
    public void WithTabletsKnownButNoneConnected_SaysSo()
    {
        var overview = Overview(Tablet("B", false));
        Assert.True(overview.ShowNoneConnected);

        overview = Overview(Tablet("A", true), Tablet("B", false));
        Assert.False(overview.ShowNoneConnected);

        overview = Overview();
        Assert.False(overview.ShowNoneConnected);
    }

    [Fact]
    public void ARebuiltListReplacesBothHalves()
    {
        var overview = Overview(Tablet("A", true), Tablet("B", false));

        overview.Tablets = [Tablet("B", true)];

        Assert.Equal(["B"], overview.Connected.Select(t => t.Name));
        Assert.Empty(overview.Remembered);
        Assert.False(overview.HasRemembered);
    }

    private static TabletsOverviewViewModel Overview(params TabletOverviewItemViewModel[] tablets)
    {
        var overview = new TabletsOverviewViewModel();
        overview.Tablets = tablets.ToList();
        overview.HasTablets = tablets.Length > 0;
        return overview;
    }

    private static TabletOverviewItemViewModel Tablet(string name, bool detected) =>
        new(name, detected, detected ? "Detected" : "Not detected", null, () => { }, () => Task.CompletedTask);

    // --- The daemon in one line -------------------------------------------------------------------------

    [Fact]
    public void ConnectedToTheBundledDaemon_SaysWhichOne()
    {
        var daemon = Daemon(new FakeConnectionState { IsConnected = true, DaemonVersion = "0.6.8.1" });

        Assert.Equal("Connected", daemon.SummaryHeadline);
        Assert.Equal("OpenTabletDriver 0.6.8.1, bundled with OpenTabletArtist", daemon.SummaryDetail);
        Assert.True(daemon.SummaryIsOk);
        Assert.False(daemon.SummaryIsBad);
    }

    [Fact]
    public void ConnectedToSomeoneElsesDaemon_SaysItIsNotTheOneTheAppStarts()
    {
        var daemon = Daemon(new FakeConnectionState
        {
            IsConnected = true,
            DaemonVersion = "0.6.8.1",
            Ownership = DaemonOwnership.External,
        });

        Assert.Equal("OpenTabletDriver 0.6.8.1, not the one OpenTabletArtist starts", daemon.SummaryDetail);
    }

    [Fact]
    public void NotConnected_IsRed_AndSaysWhatIsWrong()
    {
        var daemon = Daemon(new FakeConnectionState { IsConnected = false });

        Assert.Equal("Not connected", daemon.SummaryHeadline);
        Assert.Equal("OpenTabletArtist can't reach OpenTabletDriver.", daemon.SummaryDetail);
        Assert.True(daemon.SummaryIsBad);
        Assert.False(daemon.SummaryIsOk);
    }

    /// <summary>While a connect is in flight the dot is neutral and the headline is the activity, not a verdict:
    /// every launch starts here, and a red dot for it would be false.</summary>
    [Fact]
    public void WhileConnecting_ItIsNeitherGreenNorRed()
    {
        var daemon = Daemon(new FakeConnectionState
        {
            IsConnected = false,
            ShowDaemonActivity = true,
            DaemonActivityText = "Connecting to daemon...",
        });

        Assert.Equal("Connecting to daemon...", daemon.SummaryHeadline);
        Assert.True(daemon.SummaryIsBusy);
        Assert.False(daemon.SummaryIsBad);
        Assert.False(daemon.SummaryIsOk);
    }

    [Fact]
    public void ARefusedDaemon_IsRed_WithItsReason()
    {
        var daemon = Daemon(new FakeConnectionState
        {
            IsConnected = false,
            IsDaemonRefused = true,
            DaemonRefusalReason = "OpenTabletDriver 0.6.7 is running.",
        });

        Assert.Equal("Wrong version", daemon.SummaryHeadline);
        Assert.Equal("OpenTabletDriver 0.6.7 is running.", daemon.SummaryDetail);
        Assert.True(daemon.SummaryIsBad);
    }

    /// <summary>The card must follow the connection, not show what it was when the page was built.</summary>
    [Fact]
    public void TheSummaryFollowsTheConnection()
    {
        var connection = new FakeConnectionState { IsConnected = true, DaemonVersion = "0.6.8.1" };
        var daemon = Daemon(connection);
        var changed = new List<string>();
        daemon.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        connection.IsConnected = false;

        Assert.Contains(nameof(DaemonStatusViewModel.SummaryHeadline), changed);
        Assert.Contains(nameof(DaemonStatusViewModel.SummaryDetail), changed);
        Assert.Contains(nameof(DaemonStatusViewModel.SummaryIsBad), changed);
        Assert.Equal("Not connected", daemon.SummaryHeadline);
    }

    private static DaemonStatusViewModel Daemon(FakeConnectionState connection) => new(connection);
}
