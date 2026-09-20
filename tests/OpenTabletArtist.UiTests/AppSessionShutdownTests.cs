using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The application's half of #828: an exit that can wait does, and one that cannot still works.
/// </summary>
///
/// <remarks>
/// <para>
/// The library gained a settling close in #859 and nothing in OTA used it. Quitting disposed the
/// transport under whatever was running, which is what the whole of #828 is about: a settings write that
/// reached the daemon could fail on its way to disk with nothing left able to say whether it landed.
/// </para>
/// <para>
/// Two paths, deliberately different. The tray's Quit is asynchronous and already bounds its shutdown
/// steps, so it settles first. The window's <c>Closed</c> handler is synchronous — it is what runs when
/// something else ends the process — and it still cannot wait, which is why <c>Dispose</c> keeps saying
/// so rather than pretending.
/// </para>
/// </remarks>
public class AppSessionShutdownTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Closing waits for a settings write the session already admitted.
    /// </summary>
    /// <remarks>
    /// The assertion that the close has not finished is taken on the same thread with no await between,
    /// so it is not a question of timing: a close that did not wait would already be complete there.
    /// </remarks>
    [AvaloniaFact]
    public async Task ClosingWaitsForASettingsWriteAlreadyInFlight()
    {
        var (session, daemon, _) = Make();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => held.Task;

        var applying = session.ApplyAndSaveSettingsAsync(SettingsFor("Held"));

        var closing = session.CloseAsync(TimeSpan.FromSeconds(5));
        Assert.False(closing.IsCompleted);

        held.SetResult(true);

        Assert.True(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
        await applying.WaitAsync(Bound, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A daemon that stops answering does not hold the exit open.
    /// </summary>
    /// <remarks>
    /// The reason Quit passes a window rather than waiting for everything: the application has to be able
    /// to leave. A false answer is the honest one — the write may still have landed, and nothing here
    /// knows.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADaemonThatNeverAnswers_DoesNotHoldTheExitOpen()
    {
        var (session, daemon, _) = Make();

        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => never.Task;

        _ = session.ApplyAndSaveSettingsAsync(SettingsFor("Stuck"));

        Assert.False(await session.CloseAsync(TimeSpan.Zero)
            .WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Disposing after closing is what Quit actually does, and it does not throw.
    /// </summary>
    /// <remarks>
    /// Quit settles, then the window's <c>Closed</c> handler runs the ordinary teardown. That makes
    /// closing and disposing two calls in sequence where there used to be one, and cancelling an already
    /// disposed token source throws — so the teardown had to become idempotent.
    /// </remarks>
    [AvaloniaFact]
    public async Task ClosingThenDisposing_IsTheQuitPath_AndDoesNotThrow()
    {
        var (session, _, _) = Make();

        Assert.True(await session.CloseAsync(TimeSpan.FromSeconds(5))
            .WaitAsync(Bound, TestContext.Current.CancellationToken));

        session.Dispose();
        session.Dispose();
    }

    /// <summary>
    /// Closing after disposing — an exit that did not come through Quit — answers rather than throwing.
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingAfterDisposing_AnswersInsteadOfThrowing()
    {
        var (session, _, _) = Make();

        session.Dispose();

        await session.CloseAsync(TimeSpan.FromSeconds(5))
            .WaitAsync(Bound, TestContext.Current.CancellationToken);
    }

    // --- harness --------------------------------------------------------------------------------

    private sealed class StubLifecycle : IDaemonLifecycleService
    {
        public string? ExpectedExePath() => null;
        public bool IsAppManaged(string? path) => false;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => null;
        public bool IsRunning() => false;
        public string? Launch() => null;
        public bool Stop(int processId) => true;
        public void StopAll() { }
        public string? PathOf(int processId) => null;
        public string? SingleRunningDaemonPath() => null;
    }

    private sealed class RecordingStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }

        public bool TrySave(Settings settings, string path) => true;

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = null;
            return false;
        }
    }

    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    private static (AppSession session, FakeDaemonTransport daemon, RecordingStore store) Make()
    {
        var daemon = new FakeDaemonTransport { Settings = SettingsFor("Baseline"), ServerProcessId = 1 };
        var store = new RecordingStore();
        var otd = FakeSession.Over(daemon, store);
        daemon.Reconnect();

        return (new AppSession(otd, new StubLifecycle()), daemon, store);
    }
}
