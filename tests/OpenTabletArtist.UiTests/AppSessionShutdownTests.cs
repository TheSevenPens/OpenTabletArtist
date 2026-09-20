using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Closing refuses the reload that an apply would otherwise start on its way out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cancelling the poll token stops the loops <em>starting</em> and nothing else. The host's own
    /// wrapper reloads after any apply that reached the daemon, and that reload reads the capabilities
    /// directly rather than through the settings session — so the library's admission control never sees
    /// it. The settling close would therefore finish the apply and the apply would immediately start
    /// fresh reads against the connection being closed.
    /// </para>
    /// <para>
    /// The earlier shutdown tests here could not see this: they assert that the apply and the close
    /// finish, which they do either way.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task ClosingRefusesTheReloadAnApplyWouldStart()
    {
        var (session, daemon, _) = Make();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => held.Task;

        var applying = session.ApplyAndSaveSettingsAsync(SettingsFor("Held"));

        var closing = session.CloseAsync(TimeSpan.FromSeconds(5));
        Assert.False(closing.IsCompleted);

        // From here on, nothing new may reach the daemon.
        var readsWhenClosing = daemon.Calls.Count;

        held.SetResult(true);

        Assert.True(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
        await applying.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(readsWhenClosing, daemon.Calls.Count);
    }

    /// <summary>
    /// A reload already running when the exit begins stops rather than publishing what it read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Refusing new reloads is not enough on its own. One already past the gate goes on reading a
    /// connection that is going away, and publishes into view models that are about to be disposed —
    /// so the load checks after every await, not only at its entry.
    /// </para>
    /// <para>
    /// It holds the settings read, which is the load's own, and asserts on something the load publishes
    /// <em>after</em> that point. My first version held <c>GetAppInfoAsync</c> and proved nothing: that
    /// first call is the library's destination discovery, not this load, so the load was never held and
    /// the test passed with every guard removed.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task AReloadAlreadyRunningWhenTheExitBegins_StopsBeforePublishing()
    {
        var (session, daemon, _) = Make();
        daemon.AppInfo = new AppInfo
        {
            AppDataDirectory = "x",
            SettingsFile = "settings.json",
            PluginDirectory = "plugins",
            PresetDirectory = "presets",
        };

        Assert.Equal("", session.PresetDirectory);

        var held = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetSettingsHandler = () =>
        {
            reached.TrySetResult();
            return held.Task;
        };

        // Past the gate and into the load, waiting on a read of its own.
        var loading = session.ReloadAsync();
        await reached.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = session.CloseAsync(TimeSpan.FromSeconds(5));

        held.SetResult(daemon.Settings);
        await loading.WaitAsync(Bound, TestContext.Current.CancellationToken);

        // Everything past the settings read is publication into an application that is leaving.
        Assert.Equal("", session.PresetDirectory);

        await closing.WaitAsync(Bound, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The daemon an exit stops is the one that was answering, decided before the session closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An exit has to stop the daemon <em>after</em> settling, or it kills the connection the writes need.
    /// But deferring the whole stop is the trap: the target is resolved by asking the live session which
    /// process is answering, and after a close there is nothing to ask — so it finds nothing and falls
    /// through to stopping every daemon on the machine, including ones this application never spoke to.
    /// </para>
    /// <para>
    /// So the decision happens while connected and the action happens after. This asserts both halves:
    /// the right process, and not the blanket stop.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task TheDaemonStoppedOnExit_IsTheOneThatWasAnswering()
    {
        var (session, _, _, lifecycle) = MakeWithLifecycle();

        var stop = await session.PrepareDaemonStopAsync();
        Assert.NotNull(stop);

        Assert.True(await session.CloseAsync(TimeSpan.FromSeconds(5))
            .WaitAsync(Bound, TestContext.Current.CancellationToken));

        await stop!().WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal([77], lifecycle.Stopped);
        Assert.False(lifecycle.StoppedEverything);
    }

    /// <summary>
    /// A load held at its <em>final</em> wait does not publish once the exit has begun.
    /// </summary>
    /// <remarks>
    /// <para>
    /// I put this one guard on the wrong side of its await. The persistence retry takes the mutation gate
    /// even when it has nothing to save, so a concurrent write holds the load there — for exactly the
    /// interval a close occupies — and everything after it publishes: <c>DataLoaded</c> has host
    /// subscribers that rebuild views and start refreshes of their own.
    /// </para>
    /// <para>
    /// The earlier test cannot see this: it pauses a read near the start of the load, so the load stops
    /// long before reaching here.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task ALoadHeldAtItsFinalRetry_DoesNotPublishOnceTheExitHasBegun()
    {
        var (session, daemon, _) = Make();
        daemon.AppInfo = new AppInfo
        {
            AppDataDirectory = "x",
            SettingsFile = "settings.json",
            PluginDirectory = "plugins",
            PresetDirectory = "presets",
        };

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            writing.TrySetResult();
            return held.Task;
        };

        // A host reacting to what the load publishes, which is ordinary: the write it starts then holds
        // the mutation gate the load's last step needs.
        Task? started = null;
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(session.PresetDirectory) && started == null)
                started = session.ApplyAndSaveSettingsAsync(SettingsFor("FromTheCallback"));
        };

        var published = 0;
        session.DataLoaded += () => published++;

        var loading = session.ReloadAsync();
        await writing.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = session.CloseAsync(TimeSpan.FromSeconds(5));

        held.SetResult(true);

        await loading.WaitAsync(Bound, TestContext.Current.CancellationToken);
        if (started != null) await started.WaitAsync(Bound, TestContext.Current.CancellationToken);
        await closing.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(0, published);
    }

    // --- harness --------------------------------------------------------------------------------

    /// <summary>Records which daemon it was asked to stop, and whether it was asked to stop them all.</summary>
    private sealed class StubLifecycle : IDaemonLifecycleService
    {
        public List<int> Stopped { get; } = [];

        public bool StoppedEverything { get; private set; }

        public string? ExpectedExePath() => null;
        public bool IsAppManaged(string? path) => false;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => null;
        public bool IsRunning() => false;
        public string? Launch() => null;
        public bool Stop(int processId) { Stopped.Add(processId); return true; }
        public void StopAll() => StoppedEverything = true;
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
        var (session, daemon, store, _) = MakeWithLifecycle();
        return (session, daemon, store);
    }

    private static (AppSession, FakeDaemonTransport, RecordingStore, StubLifecycle) MakeWithLifecycle()
    {
        var daemon = new FakeDaemonTransport { Settings = SettingsFor("Baseline"), ServerProcessId = 77 };
        var store = new RecordingStore();
        var lifecycle = new StubLifecycle();
        var otd = FakeSession.Over(daemon, store);
        daemon.Reconnect();

        return (new AppSession(otd, lifecycle), daemon, store, lifecycle);
    }
}
