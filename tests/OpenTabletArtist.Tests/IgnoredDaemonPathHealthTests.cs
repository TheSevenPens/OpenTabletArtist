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

    /// <summary>With one stored, the row says what happened, and what still works.</summary>
    [Fact]
    public void WithAChosenLocation_ItSaysWhatChangedAndWhatToDo()
    {
        const string chosen = @"D:\portable-otd\OpenTabletDriver.Daemon.exe";
        var row = Row(chosen);

        Assert.NotNull(row);

        // The path itself, because "the one you chose" is not something an artist can be expected to
        // remember a year later.
        Assert.Contains(chosen, row!.Detail);

        // It still works, and starting it is the way back. This is the part that stops the row reading
        // as "your setup is broken".
        Assert.Contains("start it yourself", row.Detail);

        // And why their settings may look different, which is the symptom that sends them looking.
        Assert.Contains("plugins", row.Detail);
    }

    /// <summary>
    /// A recommendation, not a fault: nothing is broken, and OTA is doing what it now intends.
    /// </summary>
    [Fact]
    public void ItIsARecommendationPointingAtTheDaemonPage()
    {
        var row = Row(@"D:\portable-otd\OpenTabletDriver.Daemon.exe");

        Assert.Equal(HealthSeverity.Recommendation, row!.Severity);
        Assert.Equal(RemediationArea.Daemon, row.Remediation!.Area);
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
