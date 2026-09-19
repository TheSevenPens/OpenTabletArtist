using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// A persist retry writes the revision the daemon accepted, unchanged (#837).
///
/// Re-running policy on retry would write something the daemon never saw, reintroducing the exact
/// disagreement between disk and daemon that the retry exists to resolve.
///
/// The behaviour was already right; what was missing was any way to notice it changing. The existing
/// retry tests use a policy whose output is stable, so re-applying it produces identical bytes and every
/// one of them keeps passing. The policy here <b>changes what it writes on each invocation</b>, which is
/// what makes a second application visible at all.
/// </summary>
public class RetryPolicyTests
{
    /// <summary>
    /// Renames the tablet on every run, so running it twice is distinguishable from running it once.
    ///
    /// The trick is deliberate. A counter alone would prove policy ran; changing the OUTPUT proves which
    /// revision reached the disk, which is the property under test.
    /// </summary>
    private sealed class RenamingPolicy : IOtdSettingsPolicy
    {
        public int Runs { get; private set; }

        public void Apply(Settings workingCopy, SettingsPolicyContext context)
        {
            Runs++;
            foreach (var p in workingCopy.Profiles) p.Tablet = $"pass-{Runs}";
        }
    }

    /// <summary>A disk that refuses until told otherwise, and remembers what it was handed.</summary>
    private sealed class FussyStore : ISettingsFileStore
    {
        public bool Succeeds { get; set; }
        public List<string> Wrote { get; } = new();

        public void Save(Settings s, string path) => Wrote.Add(s.Profiles[0].Tablet);

        public bool TrySave(Settings s, string path)
        {
            Wrote.Add(s.Profiles[0].Tablet);
            return Succeeds;
        }

        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    [Fact]
    public async Task ARetryWritesTheRevisionTheDaemonAccepted_NotAFreshlyPolicedOne()
    {
        var (settings, daemon, store, policy) = Make();

        // Applied and accepted by the daemon; the disk refuses it.
        var applied = await settings.ApplyAndSaveAsync(Tablet("as the caller wrote it"));
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, applied.Status);
        Assert.Equal(1, policy.Runs);
        var accepted = Assert.Single(daemon.Applied).Profiles[0].Tablet;
        Assert.Equal("pass-1", accepted);

        // The disk recovers.
        store.Succeeds = true;
        var retry = await settings.RetryPersistAsync();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, retry.Status);
        Assert.Equal(1, policy.Runs);                       // policy did not run again
        Assert.Equal(accepted, store.Wrote[^1]);            // ...so the daemon and the disk agree
    }

    /// <summary>
    /// The same for the automatic retry, which shares the core and is what actually runs in the app --
    /// it is called on every reload, so a policy re-run there would be the common case rather than a rare
    /// one.
    /// </summary>
    [Fact]
    public async Task TheAutomaticRetryDoesNotRePolicyEither()
    {
        var (settings, daemon, store, policy) = Make();
        await settings.ApplyAndSaveAsync(Tablet("as the caller wrote it"));
        var accepted = Assert.Single(daemon.Applied).Profiles[0].Tablet;

        store.Succeeds = true;
        var retry = await settings.RetryPendingPersistAsync();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, retry.Status);
        Assert.Equal(1, policy.Runs);
        Assert.Equal(accepted, store.Wrote[^1]);
    }

    private static (IOtdSettingsSession, FakeDaemonTransport, FussyStore, RenamingPolicy) Make()
    {
        var daemon = new FakeDaemonTransport();
        var store = new FussyStore();
        var policy = new RenamingPolicy();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance, policy,
            new FakeProcessLocator());
        return (session.OpenSettings(() => "A/settings.json", () => true, _ => { }), daemon, store, policy);
    }

    private static Settings Tablet(string name) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = name } } };
}
