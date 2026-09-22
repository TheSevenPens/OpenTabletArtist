using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// That the driver card follows the fact deciding its wording, without waiting for a reload (#882).
/// </summary>
///
/// <remarks>
/// <para>
/// The evaluator tests prove the right sentence is produced for given inputs, and the session tests
/// prove the flag is computed. Neither covers the wiring between them, and that is where this was
/// broken: <c>HealthService</c> reacted to <c>IsForeignDaemon</c> and not to the flag.
/// </para>
/// <para>
/// The case that exposes it is a daemon change with <b>no ownership change</b> — External to External,
/// someone else's install replaced by the bundled copy answering while a selection points elsewhere.
/// Nothing about ownership moves, so a card listening only for ownership never hears anything, and keeps
/// the previous wording. A later successful reload corrects it; a delayed or failed one leaves the very
/// sentence this change fixes stale on screen.
/// </para>
/// </remarks>
public class HealthServiceDaemonCardTests
{
    /// <summary>The wording follows the flag, with no reload and no explicit refresh.</summary>
    [Fact]
    public void WhenTheFlagChangesWithoutOwnershipMoving_TheCardRewordsItself()
    {
        var (health, connection) = Make();
        using var lifetime = health;

        Assert.Equal("An OpenTabletDriver you installed, not the bundled copy", DriverRow(health));

        // The bundled daemon starts answering while the user's selection points elsewhere. Still
        // External, so ownership does not move -- only this does.
        connection.DaemonIsManagedButNotSelected = true;

        Assert.Equal("Another copy of the driver this app ships", DriverRow(health));
    }

    /// <summary>And back, so the wording is not one-way.</summary>
    [Fact]
    public void AndBackAgain_WhenTheSelectedDaemonIsNoLongerTheOneAnswering()
    {
        var (health, connection) = Make();
        using var lifetime = health;

        connection.DaemonIsManagedButNotSelected = true;
        Assert.Equal("Another copy of the driver this app ships", DriverRow(health));

        connection.DaemonIsManagedButNotSelected = false;
        Assert.Equal("An OpenTabletDriver you installed, not the bundled copy", DriverRow(health));
    }

    // --- harness --------------------------------------------------------------------------------

    /// <summary>
    /// A connected session on someone else's daemon, which is the card's starting state.
    /// </summary>
    /// <remarks>
    /// <c>Reevaluate</c> is never called here by the test: every assertion below relies on the service
    /// having heard the property change itself. Calling <c>Refresh()</c> would make this pass against the
    /// defect it exists for.
    ///
    /// <para>
    /// Each caller disposes what it gets back. The service subscribes to <c>DeveloperSettings.Instance</c>,
    /// which is static and outlives the test, so an undisposed one stays reachable and a later test
    /// touching those settings makes it reevaluate.
    /// </para>
    /// </remarks>
    private static (HealthService, FakeConnectionState) Make()
    {
        var connection = new FakeConnectionState
        {
            IsConnected = true,
            Ownership = DaemonOwnership.External,
        };

        var health = new HealthService(connection, new FakeDeviceData(), new DriverConflictMonitor());
        return (health, connection);
    }

    private static string DriverRow(HealthService health) =>
        health.Issues.Single(i => i.Id == "otd.driver").Links!.Single().Setting;
}
