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
    /// <summary>
    /// No synchronization context is installed for these tests.
    /// </summary>
    /// <remarks>
    /// Which is the harder condition, and the one that matters: the library must not depend on
    /// continuations coming back anywhere by default. Where a host <em>does</em> need one is documented on
    /// <see cref="IOtdExecutionContext"/>, and it is the host's to supply — this suite supplies contexts
    /// explicitly, per test, which is why its orderings are decidable at all.
    /// </remarks>
    [Fact]
    public void NothingHasInstalledASynchronizationContext()
    {
        Assert.Null(SynchronizationContext.Current);
    }

    /// <summary>
    /// A session can be built, used and disposed on a bare thread pool thread.
    /// </summary>
    /// <remarks>
    /// End to end rather than by inspection: connect, identify, read, edit, and tear down, on a thread
    /// that has no context of any kind beyond the one handed to the session. A dependency that needed a
    /// dispatcher would surface here rather than in a reference list.
    /// </remarks>
    [Fact]
    public async Task ASessionWorksOnABareThreadPoolThread()
    {
        var worked = await Task.Run(async () =>
        {
            Assert.Null(SynchronizationContext.Current);

            var daemon = new FakeDaemonTransport
            {
                ServerProcessId = 1,
                Settings = new OpenTabletDriver.Desktop.Settings(),
            };
            var store = new CountingStore();
            var context = new InlineTestContext();

            using var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
                NoPolicy.Instance, new FakeProcessLocator(), context);
            var settings = session.OpenSettings(() => true, _ => { });

            daemon.Reconnect();
            await settings.ReloadFromDaemonAsync();
            var outcome = await settings.ApplyAndSaveAsync(new OpenTabletDriver.Desktop.Settings());

            return outcome.Status == SettingsApplyStatus.AppliedAndSaved && store.Writes == 1;
        });

        Assert.True(worked);
    }

    /// <summary>
    /// And nothing loaded into this test process is a UI framework.
    /// </summary>
    /// <remarks>
    /// The dependency check reads what the assemblies declare; this reads what actually got loaded,
    /// which would also catch something pulled in by reflection or by a test helper.
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

    /// <summary>Runs work where it was handed over, for a test that is not about ordering.</summary>
    private sealed class InlineTestContext : IOtdExecutionContext
    {
        public bool IsCurrent => true;

        public Task PostAsync(Action work)
        {
            try { work(); return Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }
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
