using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What a host is told about a reconnect, and when (#828).
///
/// The host used to call <c>NoteConnectedDaemon</c> itself at the right moment, so calling it late or not
/// at all was possible and silent. The session subscribes to its own connection now and identifies the
/// daemon on the host's execution context.
///
/// These assert the ordering that makes that worth having, with the context <b>held</b> so that "while
/// identification is still pending" is a deliberate step rather than a race to lose.
/// </summary>
public class ReconnectOrderingTests
{
    /// <summary>
    /// A host hears about a connection only after the session has finished with it.
    ///
    /// The transport raises as soon as its channel is usable, which is before anything has looked at
    /// which daemon answered. A host acting on that would be acting while the session still described
    /// the previous one.
    /// </summary>
    [Fact]
    public void TheHostIsToldOnlyAfterIdentificationHasRun()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        h.Daemon.Reconnect();                   // the channel is up, and the transport has said so

        Assert.Empty(told);                   // ...and the host has not been told anything yet
        Assert.Equal(1, h.Context.Pending);

        h.Context.Drain();

        Assert.Equal("A/OpenTabletDriver.Daemon.exe", Assert.Single(told).ExecutablePath);
    }

    /// <summary>
    /// The change handed over says what the session did, and asking afterwards finds nothing left.
    ///
    /// The host is not sent to find out separately, and cannot get a different answer by asking later —
    /// which is what made the old arrangement fragile.
    /// </summary>
    [Fact]
    public async Task TheChangeHandedOverSaysWhatTheSessionDiscarded()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        // An edit the daemon took and the disk refused, so there is something to lose.
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await h.Settings.ApplyAndSaveAsync(Tablet("edited"))).Status);

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        var change = Assert.Single(told);
        Assert.True(change.Changed);
        Assert.True(change.DiscardedUnsavedChange);

        Assert.False(h.Session.NoteConnectedDaemon().Changed);
    }

    /// <summary>
    /// A to B to C, with nothing drained between them: every notification names the daemon that is
    /// answering <em>now</em>, and exactly one reports a change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not "B then C", which is what I first asserted and is not achievable. Identification reads the
    /// world when it runs, and by then B has already been replaced — so a notification naming B would be
    /// naming a daemon that no longer exists, which is worse than telling the host about C twice.
    /// </para>
    /// <para>
    /// Pinned because the tempting "fix" is to capture identity at the moment of the transition instead.
    /// That would report a daemon that has gone, and a host acting on it — showing its path, deciding
    /// whether it may be stopped — would be acting on something untrue.
    /// </para>
    /// </remarks>
    [Fact]
    public void RapidTransitions_AllNameTheDaemonAnsweringNow()
    {
        var h = Make();
        var told = new List<DaemonChange>();
        h.Session.Connected += told.Add;

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.MoveTo("C/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        Assert.Equal(2, told.Count);
        Assert.All(told, t => Assert.Equal("C/OpenTabletDriver.Daemon.exe", t.ExecutablePath));
        Assert.Single(told, t => t.Changed);       // one boundary crossed, however many notifications
    }

    /// <summary>
    /// A disposed session posts nothing further, so a drop arriving during teardown cannot run work
    /// against a session that has gone.
    /// </summary>
    [Fact]
    public void AfterDisposal_ATransitionPostsNothing()
    {
        var h = Make();
        h.Session.Dispose();

        h.Daemon.Reconnect();

        Assert.Equal(0, h.Context.Pending);
    }

    /// <summary>
    /// A subscriber that throws is reported, not swallowed.
    ///
    /// Posted work has no caller to throw to — this is reached from the transport's own notification —
    /// so without this the failure would vanish entirely. That is the whole reason
    /// <see cref="IOtdExecutionContext.PostAsync"/> returns a task, and discarding it at the one call
    /// site would have made the return value decorative.
    /// </summary>
    [Fact]
    public void ASubscriberThatThrows_IsReported()
    {
        var log = new RecordingLog();
        var h = Make(log);
        h.Session.Connected += _ => throw new InvalidOperationException("a bad subscriber");

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        Assert.Contains(log.Warnings, w => w.Contains("a bad subscriber"));
    }

    /// <summary>
    /// A subscriber that throws does not undo what the session already did.
    ///
    /// The identification and any invalidation happen before the host is told, so a bad subscriber can
    /// lose its own notification and nothing else.
    /// </summary>
    [Fact]
    public void ASubscriberThatThrows_DoesNotUndoTheInvalidation()
    {
        var h = Make();
        h.Session.Connected += _ => throw new InvalidOperationException("a bad subscriber");

        h.MoveTo("B/OpenTabletDriver.Daemon.exe");
        h.Context.Drain();

        // The session is on B, so asking again reports no further change.
        Assert.False(h.Session.NoteConnectedDaemon().Changed);
    }

    // --- harness --------------------------------------------------------------------------------

    private sealed class RefusingStore : ISettingsFileStore
    {
        public void Save(Settings s, string p) { }
        public bool TrySave(Settings s, string p) => false;
        public bool TryLoad(string p, out Settings? s) { s = null; return false; }
    }

    /// <summary>Everything a test needs to move the world underneath the session.</summary>
    private sealed record Harness(
        OtdSession Session,
        FakeDaemonTransport Daemon,
        FakeProcessLocator Locator,
        ControllableContext Context,
        IOtdSettingsSession Settings)
    {
        /// <summary>A new channel answering as a different executable: one reconnect, one identity change.</summary>
        public void MoveTo(string path)
        {
            Locator.Path = path;
            Daemon.Reconnect();
        }
    }

    private static Harness Make(IOtdLog? log = null)
    {
        var locator = new FakeProcessLocator { Path = "A/OpenTabletDriver.Daemon.exe" };
        var daemon = new FakeDaemonTransport { ServerProcessId = 1 };
        var context = new ControllableContext();
        var session = OtdSession.ForTesting(daemon, new RefusingStore(), log ?? NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, locator, context);
        var settings = session.OpenSettings(() => "A/settings.json", () => true, _ => { });
        session.NoteConnectedDaemon();              // establish A as the daemon this session knows
        return new Harness(session, daemon, locator, context, settings);
    }

    private static Settings Tablet(string n) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = n } } };
}
