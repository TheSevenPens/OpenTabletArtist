using System;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The upgrade notice across the seam that reads and writes settings (#946).
/// </summary>
///
/// <remarks>
/// <para>
/// <see cref="IgnoredDaemonPathHealthTests"/> proves the evaluator produces the right row for given
/// inputs. It cannot prove that <see cref="HealthService"/> reads the keys it means, that the
/// acknowledgement survives a round trip through <c>settings.json</c>, or that acknowledging leaves the
/// artist's old path alone — and those are the joins where this would break quietly.
/// </para>
/// <para>
/// Not a rendered-view test, which is what Codex asked for. The dashboard's remediation dispatcher needs
/// a live <c>AppSession</c>, so a headless rendering of it would be building a connection loop to assert
/// two settings writes. This covers the same two outcomes one layer down, and what it does not cover is
/// the button's binding: that the row's action reaches this key at all. Codex verified that against the
/// real <c>DashboardView</c> in review, and the dispatcher case is three lines with no branching.
/// </para>
/// <para>
/// Settings writes are safe here: <c>TestUserDataRoot</c> redirects the whole data directory per process
/// (#738), so these never touch a real installation.
/// </para>
/// </remarks>
public class LegacyDaemonPathNoticeServiceTests : IDisposable
{
    private const string OldDaemon = @"D:\portable-otd\OpenTabletDriver.Daemon.exe";
    private const string LegacyPathKey = "daemon.userPath";

    public LegacyDaemonPathNoticeServiceTests() => Clear();

    public void Dispose() => Clear();

    private static void Clear()
    {
        AppSettings.Set(LegacyPathKey, "");
        AppSettings.Set(HealthService.LegacyPathNoticeAcknowledgedKey, "");
    }

    /// <summary>Acknowledging is remembered, and costs the artist nothing else.</summary>
    /// <remarks>
    /// The second assertion is the one worth having: the row is answered by writing a <em>different</em>
    /// key, so the location they chose is still there for a downgrade to find. A fix that silenced the
    /// row by clearing their choice would pass the first assertion and fail this one.
    /// </remarks>
    [Fact]
    public void Acknowledging_IsRememberedAndLeavesTheOldPathAlone()
    {
        AppSettings.Set(LegacyPathKey, OldDaemon);
        using var health = Service(connectedTo: "");

        Assert.True(HasNotice(health));

        // What the dashboard's remediation does for this row.
        AppSettings.Set(HealthService.LegacyPathNoticeAcknowledgedKey, "true");
        health.Refresh();

        Assert.False(HasNotice(health));
        Assert.Equal(OldDaemon, AppSettings.Get(LegacyPathKey));
    }

    /// <summary>Connecting to the old daemon answers it without anything being clicked.</summary>
    [Fact]
    public void ConnectingToTheOldDaemon_ClearsItWithoutAcknowledgement()
    {
        AppSettings.Set(LegacyPathKey, OldDaemon);
        using var health = Service(connectedTo: OldDaemon);

        Assert.False(HasNotice(health));
        Assert.Equal("", AppSettings.Get(HealthService.LegacyPathNoticeAcknowledgedKey) ?? "");
    }

    /// <summary>And the row is not conjured for someone who never used the picker.</summary>
    /// <remarks>
    /// The control. Without it the two tests above would pass just as well against a service that showed
    /// this row to everybody.
    /// </remarks>
    [Fact]
    public void WithNoStoredPath_ThereIsNoNotice()
    {
        using var health = Service(connectedTo: "");

        Assert.False(HasNotice(health));
    }

    private static HealthService Service(string connectedTo)
    {
        var connection = new FakeConnectionState
        {
            IsConnected = !string.IsNullOrEmpty(connectedTo),
            Ownership = DaemonOwnership.External,
            DaemonSourcePath = connectedTo,
        };
        return new HealthService(connection, new FakeDeviceData(), new DriverConflictMonitor());
    }

    private static bool HasNotice(HealthService health) =>
        health.Issues.Any(i => i.Id == "daemon.ignoredPath");
}
