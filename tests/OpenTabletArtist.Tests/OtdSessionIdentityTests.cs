using System;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Which daemon is answering, and what a different one costs (#787, #803, #807 Phase 4).
///
/// Users run more than one OpenTabletDriver build, in folders of their choosing, and switch between them
/// while the app is open. Almost everything a settings session holds is a fact about one daemon — the
/// change it accepted but never wrote, the file that change was for, what its settings file last held,
/// whether it is running a transient override — and each one misleads if carried across.
///
/// This logic lived in the host until Phase 4 and had no direct coverage there: every lifecycle stub
/// reported "cannot see", so no test could observe a daemon change even in principle. These exercise the
/// two answers that are easy to confuse — a different daemon, and one we cannot identify.
/// </summary>
public class OtdSessionIdentityTests
{
    private const int AnyPid = 4321;

    /// <summary>The first look has nothing to compare against, so nothing is discarded.</summary>
    [Fact]
    public void TheFirstLook_IsNotAChange()
    {
        var (session, _, locator) = Make();
        locator.Path = "daemon-one/OpenTabletDriver.Daemon";

        var change = session.NoteConnectedDaemon();

        Assert.False(change.Changed);
        Assert.False(change.DiscardedUnsavedChange);
        Assert.Equal("daemon-one/OpenTabletDriver.Daemon", change.ExecutablePath);
    }

    /// <summary>Reconnecting to the same daemon is not a session boundary.</summary>
    [Fact]
    public void TheSameDaemonAgain_IsNotAChange()
    {
        var (session, _, locator) = Make();
        locator.Path = "daemon-one/OpenTabletDriver.Daemon";
        session.NoteConnectedDaemon();

        var change = session.NoteConnectedDaemon();

        Assert.False(change.Changed);
    }

    /// <summary>
    /// A different binary answering drops the state that described the one that has gone, and says so
    /// when that cost the user an edit the disk never received.
    /// </summary>
    [Fact]
    public async Task ADifferentDaemon_DropsTheOldOnesStateAndReportsWhatWasLost()
    {
        var (session, daemon, locator) = Make(new RefusingStore());
        var settings = Open(session);
        locator.Path = "daemon-one/OpenTabletDriver.Daemon";
        session.NoteConnectedDaemon();

        // Applied, and the write refused: live on the old daemon, and only there.
        var applied = await settings.ApplyAndSaveAsync(Tablet("T"));
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, applied.Status);
        Assert.Single(daemon.Applied);

        locator.Path = "daemon-two/OpenTabletDriver.Daemon";
        var change = session.NoteConnectedDaemon();

        Assert.True(change.Changed);
        Assert.True(change.DiscardedUnsavedChange);

        // And the pending write is genuinely gone, not merely reported: a retry now writes nothing,
        // because writing it here would put the old daemon's settings into the new daemon's file.
        var retry = await settings.RetryPersistAsync();
        Assert.Equal(SettingsApplyStatus.NoChange, retry.Status);
    }

    /// <summary>A change with nothing unsaved is still a change; it just cost the user nothing.</summary>
    [Fact]
    public void ADifferentDaemonWithNothingPending_CostsNothing()
    {
        var (session, _, locator) = Make();
        Open(session);
        locator.Path = "daemon-one/OpenTabletDriver.Daemon";
        session.NoteConnectedDaemon();

        locator.Path = "daemon-two/OpenTabletDriver.Daemon";
        var change = session.NoteConnectedDaemon();

        Assert.True(change.Changed);
        Assert.False(change.DiscardedUnsavedChange);
    }

    /// <summary>
    /// A daemon whose executable cannot be read is not a daemon that changed.
    ///
    /// This is the case that costs a user their work if it is got wrong. An elevated daemon, or another
    /// user's, is unreadable every single time — so treating "cannot see" as "it is different" would
    /// throw away an unsaved edit on an ordinary reconnect, over and over.
    /// </summary>
    [Fact]
    public async Task ADaemonThatCannotBeIdentified_IsNotTreatedAsAChange()
    {
        var (session, _, locator) = Make(new RefusingStore());
        var settings = Open(session);
        locator.Path = "daemon-one/OpenTabletDriver.Daemon";
        session.NoteConnectedDaemon();
        await settings.ApplyAndSaveAsync(Tablet("T"));

        locator.Path = null;                       // elevated, or another user's
        var change = session.NoteConnectedDaemon();

        Assert.False(change.Changed);
        Assert.False(change.DiscardedUnsavedChange);
        Assert.Null(change.ExecutablePath);

        // The unsaved edit survives, and is still aimed at the daemon it was made for.
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await settings.RetryPersistAsync()).Status);
    }

    /// <summary>
    /// An unreadable look does not erase what we last knew, so a real switch after one is still caught.
    ///
    /// Forgetting would be quiet and costly: the next look would have nothing to compare against, take
    /// itself for the first, and carry the old daemon's state onto a genuinely different one.
    ///
    /// Asserted through the switch rather than through the reconnect, deliberately. "Same daemon after an
    /// unreadable look reports no change" passes whether the path was remembered or forgotten — the trap
    /// this file exists to avoid.
    /// </summary>
    [Fact]
    public void AnUnreadableLook_DoesNotMakeTheNextDaemonLookLikeTheFirst()
    {
        var (session, _, locator) = Make();
        Open(session);
        locator.Path = "daemon-one/OpenTabletDriver.Daemon";
        session.NoteConnectedDaemon();

        locator.Path = null;                       // one reconnect we could not identify
        Assert.False(session.NoteConnectedDaemon().Changed);

        locator.Path = "daemon-two/OpenTabletDriver.Daemon";
        Assert.True(session.NoteConnectedDaemon().Changed);
    }

    // --- When the connection cannot say which process answered -----------------------------------
    //
    // The pipe-to-process-id lookup is Windows-only. Elsewhere the daemon is effectively a singleton, so
    // "the one that is running" is a sound answer where "the one that answered this pipe" is unavailable.
    // Every test above supplies a process id, so none of them reaches this branch -- which is why it is
    // covered separately rather than assumed.
    //
    // The call counts matter: "the fallback was skipped" and "the fallback ran and returned null" reach
    // the same answer, and only one of them is the behaviour being asserted.

    /// <summary>
    /// On Windows the fallback is not consulted at all.
    ///
    /// Exact pipe attribution is what distinguishes our daemon from a second OpenTabletDriver instance
    /// running beside it. Falling back to "whatever daemon is running" would discard that on the one
    /// platform where it is available, and could attribute the connection to the wrong process.
    /// </summary>
    [Fact]
    public void OnWindows_NoProcessId_MeansNoAttributionRatherThanAGuess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The pipe-to-process-id lookup is Windows-only.");

        var (session, daemon, locator) = Make();
        daemon.ServerProcessId = null;
        locator.OnlyDaemon = "some-other-daemon/OpenTabletDriver.Daemon";

        var change = session.NoteConnectedDaemon();

        Assert.Null(change.ExecutablePath);
        Assert.Equal(0, locator.FallbackCalls);
    }

    /// <summary>Elsewhere, the single running daemon is the answer.</summary>
    [Fact]
    public void OffWindows_NoProcessId_UsesTheSingleRunningDaemon()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows attributes the pipe exactly and never falls back.");

        var (session, daemon, locator) = Make();
        daemon.ServerProcessId = null;
        locator.OnlyDaemon = "daemon-one/OpenTabletDriver.Daemon";

        var change = session.NoteConnectedDaemon();

        Assert.Equal("daemon-one/OpenTabletDriver.Daemon", change.ExecutablePath);
        Assert.Equal(1, locator.FallbackCalls);
    }

    /// <summary>
    /// And when there is not exactly one, or its path cannot be read, that is "cannot see" like any
    /// other -- not a change, and not a reason to discard anything.
    /// </summary>
    [Fact]
    public void OffWindows_AnAmbiguousFallback_IsJustCannotSee()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows attributes the pipe exactly and never falls back.");

        var (session, daemon, locator) = Make();
        locator.Path = "daemon-one/OpenTabletDriver.Daemon";
        session.NoteConnectedDaemon();

        daemon.ServerProcessId = null;
        locator.OnlyDaemon = null;                 // two running, or unreadable
        var change = session.NoteConnectedDaemon();

        Assert.Null(change.ExecutablePath);
        Assert.False(change.Changed);
    }

    /// <summary>
    /// A process id that resolves to nothing does NOT fall through to the fallback, on any platform.
    ///
    /// The connection named a process; failing to read that one is "cannot see", and answering with some
    /// other daemon's path instead would be a confident wrong answer where an honest blank was available.
    /// </summary>
    [Fact]
    public void AProcessIdThatCannotBeRead_DoesNotFallBackToAnotherDaemon()
    {
        var (session, _, locator) = Make();
        locator.Path = null;                       // the named process is unreadable
        locator.OnlyDaemon = "some-other-daemon/OpenTabletDriver.Daemon";

        var change = session.NoteConnectedDaemon();

        Assert.Null(change.ExecutablePath);
        Assert.Equal(1, locator.PathOfCalls);
        Assert.Equal(0, locator.FallbackCalls);
    }

    /// <summary>
    /// Looking at the daemon before the settings authority exists is harmless.
    ///
    /// There is no queued apply, pending retry or baseline to invalidate yet, so remembering what was
    /// seen is the whole of the work. What must not happen is the opposite: the authority opening later
    /// and then reporting a discard for a switch that cost nothing.
    /// </summary>
    [Fact]
    public void LookingBeforeSettingsAreOpen_LeavesNothingToDiscardLater()
    {
        var (session, _, locator) = Make();

        locator.Path = "daemon-one/OpenTabletDriver.Daemon";
        session.NoteConnectedDaemon();
        locator.Path = "daemon-two/OpenTabletDriver.Daemon";
        Assert.True(session.NoteConnectedDaemon().Changed);

        Open(session);

        var change = session.NoteConnectedDaemon();
        Assert.False(change.Changed);
        Assert.False(change.DiscardedUnsavedChange);
    }

    // --- Harness ---------------------------------------------------------------------------------

    private static (OtdSession session, FakeDaemonTransport daemon, FakeProcessLocator locator) Make(
        ISettingsFileStore? store = null)
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = AnyPid };
        var locator = new FakeProcessLocator();
        return (FakeSession.Over(daemon, store ?? new AcceptingStore(), locator), daemon, locator);
    }

    private static IOtdSettingsSession Open(OtdSession session) =>
        session.OpenSettings(() => "A/settings.json", () => true, _ => { });

    private static Settings Tablet(string name) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = name } } };

    /// <summary>A disk that refuses every write, so an apply is live and nowhere else.</summary>
    private sealed class RefusingStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) => throw new System.IO.IOException("refused");
        public bool TrySave(Settings settings, string path) => false;
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    /// <summary>A disk that takes everything, so nothing is ever pending.</summary>
    private sealed class AcceptingStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }
        public bool TrySave(Settings settings, string path) => true;
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }
}
