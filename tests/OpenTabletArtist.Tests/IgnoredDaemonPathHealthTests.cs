using System.Linq;
using OpenTabletArtist.Domain.Health;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Telling an artist that the daemon location they chose is no longer being used (#936).
/// </summary>
///
/// <remarks>
/// <para>
/// #930 stopped OTA launching anything but the copy it ships, and the setting behind the old picker is
/// simply no longer read. For most people that is invisible and correct: a daemon they start owns the
/// pipe and OTA connects to it exactly as before.
/// </para>
/// <para>
/// It is not invisible for one case. OpenTabletDriver prefers a <c>userdata</c> folder beside its own
/// executable when one exists, and a portable install has one; the copy OTA ships does not, so it reads
/// the shared location. An artist whose chosen path was a portable install opens OTA one day to a
/// different set of settings and plugins, with their own files intact but unused — and nothing connects
/// that to an upgrade.
/// </para>
/// <para>
/// A health row rather than a one-time notice, because the situation outlives the moment: it is
/// findable on the day they go looking, which is not the day they upgraded.
/// </para>
/// </remarks>
public class IgnoredDaemonPathHealthTests
{
    private static HealthIssue? Row(string ignoredPath) =>
        HealthEvaluator.Evaluate(new HealthInputs { IgnoredDaemonPath = ignoredPath })
            .FirstOrDefault(x => x.Id == "daemon.ignoredPath");

    /// <summary>Nobody who never used the picker hears about it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoChosenLocation_NothingIsSaid(string stored)
    {
        Assert.Null(Row(stored));
    }

    /// <summary>With one stored, the row says what happened, and what to do about it.</summary>
    [Fact]
    public void WithAChosenLocation_ItSaysWhatChangedAndWhatToDo()
    {
        const string chosen = @"D:\portable-otd\OpenTabletDriver.Daemon.exe";
        var row = Row(chosen);

        Assert.NotNull(row);

        // The path itself, because "the one you chose" is not something an artist can be expected to
        // remember a year later.
        Assert.Contains(chosen, row!.Detail);

        // What OTA did, not what is true of the disk: it cannot know the folder still exists.
        Assert.Contains("has not moved or deleted your previous OpenTabletDriver files", row.Detail);
        Assert.Contains("If that installation is still there", row.Detail);

        // And why their settings may look different, which is the symptom that sends them looking.
        Assert.Contains("plugins", row.Detail);
    }

    /// <summary>
    /// The way back has to be a sequence that works, and "just start it" is not one.
    /// </summary>
    /// <remarks>
    /// OpenTabletDriver is single-instance. By the time this row is read, OTA has started the copy it
    /// ships, so the old one cannot start alongside it — the advice has to stop this daemon and close
    /// OTA first, in that order, because closing OTA alone leaves its daemon running.
    /// </remarks>
    [Fact]
    public void TheWayBackAccountsForTheDaemonBeingSingleInstance()
    {
        var row = Row(@"D:\portable-otd\OpenTabletDriver.Daemon.exe");

        // "Quit and stop the daemon", not "close the window": closing hides OTA to the tray and
        // relaunching brings the same instance forward, so the ordinary reading of "close and reopen"
        // never produces the fresh session this needs (#941, #72).
        Assert.Contains("Quit and stop the daemon", row!.Detail);
        Assert.Contains("launch OpenTabletArtist again", row.Detail);
        Assert.Contains("only one daemon can run at a time", row.Detail);
    }

    /// <summary>Doing what it says finishes it — no click required.</summary>
    /// <remarks>
    /// Starting the old daemon and letting OTA connect to it is the situation resolving itself. A row
    /// that kept insisting afterwards would be telling the artist their settings may look different
    /// while they are looking at the very settings it meant.
    /// </remarks>
    [Fact]
    public void OnceTheOldDaemonIsTheOneAnswering_TheRowIsGone()
    {
        const string chosen = @"D:\portable-otd\OpenTabletDriver.Daemon.exe";

        var issues = HealthEvaluator.Evaluate(new HealthInputs
        {
            DaemonConnected = true,
            IgnoredDaemonPath = chosen,
            ConnectedDaemonPath = chosen,
        });

        Assert.DoesNotContain(issues, x => x.Id == "daemon.ignoredPath");
    }

    /// <summary>And so does saying you have read it.</summary>
    /// <remarks>
    /// The other way out, and the reason the row carries an action rather than a link: without one it is
    /// a recommendation that can never be satisfied by the artist who is content, which is a permanent
    /// entry in "Needs attention" for a decision they already made.
    /// </remarks>
    [Fact]
    public void OnceTheNoticeIsAcknowledged_TheRowIsGone()
    {
        var issues = HealthEvaluator.Evaluate(new HealthInputs
        {
            IgnoredDaemonPath = @"D:\portable-otd\OpenTabletDriver.Daemon.exe",
            LegacyPathNoticeAcknowledged = true,
        });

        Assert.DoesNotContain(issues, x => x.Id == "daemon.ignoredPath");
    }

    /// <summary>
    /// The action acknowledges and nothing else, and its label has to match that.
    /// </summary>
    /// <remarks>
    /// It was "Use the bundled one", which promises a switch this does not perform: the row can appear
    /// while OTA is connected to some <em>other</em> external copy, because its condition only excludes
    /// the old saved path. Pressing it then cleared the row and selected nothing, which is a button that
    /// lies about what it did (#941).
    /// </remarks>
    [Fact]
    public void TheRowOffersAWayToBeDoneWithIt()
    {
        var row = Row(@"D:\portable-otd\OpenTabletDriver.Daemon.exe");

        Assert.Equal(RemediationArea.AcknowledgeLegacyDaemonPath, row!.Remediation!.Area);

        // The label is the promise, so it is asserted rather than left to the area's name.
        Assert.Equal("Got it", row.Remediation.ActionLabel);
    }

    /// <summary>
    /// A recommendation, not a fault: nothing is broken, and OTA is doing what it now intends.
    /// </summary>
    [Fact]
    public void ItIsARecommendationRatherThanAFault()
    {
        var row = Row(@"D:\portable-otd\OpenTabletDriver.Daemon.exe");

        Assert.Equal(HealthSeverity.Recommendation, row!.Severity);
    }

    /// <summary>
    /// It does not wait for a connection.
    /// </summary>
    /// <remarks>
    /// The artist most likely to be confused is the one whose chosen daemon is <em>not</em> running,
    /// because that is exactly when OTA starts its own instead. A row that only appeared while connected
    /// would be missing in the case it exists for.
    /// </remarks>
    [Fact]
    public void ItShowsWhetherOrNotADaemonIsConnected()
    {
        foreach (var connected in new[] { true, false })
        {
            var issues = HealthEvaluator.Evaluate(new HealthInputs
            {
                DaemonConnected = connected,
                IgnoredDaemonPath = @"D:\portable-otd\OpenTabletDriver.Daemon.exe",
            });

            Assert.Contains(issues, x => x.Id == "daemon.ignoredPath");
        }
    }
}
