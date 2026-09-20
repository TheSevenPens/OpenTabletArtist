using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OtdInterop.Tests;

/// <summary>
/// That this suite really is headless, rather than headless by nobody having looked.
/// </summary>
///
/// <remarks>
/// <para>
/// The claim #807 makes about the library is that a host with no UI framework can use it. The
/// dependency check proves nothing links a UI framework in; this proves the tests do not quietly rely on
/// one being there at runtime — an ambient <c>SynchronizationContext</c>, an installed dispatcher, a
/// platform that has to be initialised first.
/// </para>
/// <para>
/// It is the difference between "we removed the reference" and "it works without it". Those came apart
/// once already in this project: the library required an execution context precisely because capturing an
/// ambient one yields the thread pool when there is none, which looks like working and is not.
/// </para>
/// </remarks>
public class HeadlessTests
{
    /// <summary>How long any wait here may take before it is a failure rather than a wait.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// No synchronization context is installed for these tests by default.
    /// </summary>
    /// <remarks>
    /// Which is a statement about this suite, not a claim about the library. A host calling the
    /// asynchronous settings operations <b>does</b> need a synchronization context that returns
    /// continuations to its serialized context — <see cref="IOtdExecutionContext"/> says so and says why.
    /// What this pins is that nothing is installed behind these tests' backs, so a test that needs one
    /// has to supply it, which is what makes their orderings decidable rather than ambient.
    ///
    /// The earlier wording here said the library must not depend on continuations coming back anywhere.
    /// That is the opposite of the contract and would have sent a future host down an unsupported route.
    /// </remarks>
    [Fact]
    public void NothingHasInstalledASynchronizationContext()
    {
        Assert.Null(SynchronizationContext.Current);
    }

    /// <summary>
    /// A session works on a headless host that supplies what the contract asks for — including across an
    /// incomplete await answered from another thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to run against an inline context whose <c>IsCurrent</c> was always true, with fakes that
    /// answered synchronously. It therefore showed that nothing <em>crashed</em> without a UI framework,
    /// and nothing more: no await ever suspended, so confinement across one was never exercised. Calling
    /// that "works on a bare thread pool thread" invited a host to build exactly the arrangement
    /// <see cref="IOtdExecutionContext"/> rules out.
    /// </para>
    /// <para>
    /// It uses the switch-check tool's <c>PumpContext</c> instead, which is what a real headless host
    /// looks like: one thread, one queue, and a synchronization context that routes continuations back to
    /// it. The daemon's reply is held and completed from another thread, so there is a suspension to come
    /// back from.
    /// </para>
    /// <para>
    /// Deliberately not a second concurrency suite. That the continuation lands on the host's context is
    /// established by <c>DestinationReadinessTests.AfterAnAsynchronousLookup_TheRetryStaysOnTheHostContext</c>
    /// and the pump's own tests; what this adds is that the whole arrangement stands up with no UI
    /// framework anywhere near it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASessionWorksOnAHeadlessHostAcrossAnIncompleteAwait()
    {
        Assert.Null(SynchronizationContext.Current);

        var daemon = new FakeDaemonTransport { ServerProcessId = 1 };
        var store = new CountingStore();
        using var pump = new OtdDaemonSwitchCheck.PumpContext();

        using var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            NoPolicy.Instance, new FakeProcessLocator(), pump);

        // Held, and answered from a thread that is not the pump's, so the reload really suspends.
        var reply = new TaskCompletionSource<OpenTabletDriver.Desktop.Settings?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        daemon.GetSettingsHandler = () =>
        {
            readEntered.TrySetResult();
            return reply.Task;
        };

        var onTheHost = 0;
        var worked = false;
        Task? hostWork = null;

        try
        {
            hostWork = pump.RunAsync(async () =>
            {
                daemon.Reconnect();

                var settings = session.OpenSettings(() => true, _ => { });
                await settings.ReloadFromDaemonAsync();

                // After the suspension, and this is the part the old version could not reach.
                onTheHost = pump.IsCurrent ? 1 : 0;

                var outcome = await settings.ApplyAndSaveAsync(new OpenTabletDriver.Desktop.Settings());
                worked = outcome.Status == SettingsApplyStatus.AppliedAndSaved && store.Writes == 1;
            });

            // A handshake, not a sleep. The read has been entered...
            await readEntered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

            // ...and the pump has since run something else, which it can only do once the body has
            // yielded at the await. A delay established neither: a slow worker meant the reply could be
            // completed before the read was even reached, the whole operation then ran without
            // suspending, and this passed while demonstrating nothing. A longer sleep has the same hole.
            await pump.PostAsync(() => { }).WaitAsync(Bound, TestContext.Current.CancellationToken);

            Assert.False(hostWork.IsCompleted);      // still parked, so there is something to come back from

            reply.SetResult(new OpenTabletDriver.Desktop.Settings());
            await hostWork.WaitAsync(Bound, TestContext.Current.CancellationToken);
        }
        finally
        {
            // Nothing outstanding on the way out, however this ended.
            reply.TrySetResult(new OpenTabletDriver.Desktop.Settings());
            if (hostWork != null)
            {
                try { await hostWork.WaitAsync(Bound, TestContext.Current.CancellationToken); }
                catch (TimeoutException) { /* the assertion above is the report; do not mask it */ }
            }
        }

        Assert.Equal(1, onTheHost);
        Assert.True(worked);
    }

    /// <summary>
    /// And nothing loaded into this test process is a UI framework.
    /// </summary>
    /// <remarks>
    /// A snapshot, and worth keeping as one: it reads what actually got loaded, so it would catch
    /// something pulled in by reflection or by a test helper that no reference list mentions.
    ///
    /// It observes this moment only. A later test in the same process could load something and this would
    /// not know, so it is supplementary to the reference and restore checks rather than a replacement for
    /// either.
    /// </remarks>
    [Fact]
    public void NoUiFrameworkIsLoaded()
    {
        var all = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? "")
            .ToList();

        // Established first, because "found none" means nothing unless this looked at something. Without
        // it the check would pass just as well against an empty list, which is the shape of probe that
        // has caught me repeatedly in this project.
        Assert.Contains("OtdInterop", all);

        var loaded = all
            .Where(n => n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("OpenTabletArtist", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(loaded.Count == 0,
            "A UI framework or the application was loaded into the library's own test process: "
            + string.Join(", ", loaded));
    }

    /// <summary>A store that only counts, since where it wrote is another test's subject.</summary>
    private sealed class CountingStore : ISettingsFileStore
    {
        public int Writes { get; private set; }

        public void Save(OpenTabletDriver.Desktop.Settings settings, string path) => Writes++;

        public bool TrySave(OpenTabletDriver.Desktop.Settings settings, string path)
        {
            Writes++;
            return true;
        }

        public bool TryLoad(string path, out OpenTabletDriver.Desktop.Settings? settings)
        {
            settings = null;
            return false;
        }
    }
}
