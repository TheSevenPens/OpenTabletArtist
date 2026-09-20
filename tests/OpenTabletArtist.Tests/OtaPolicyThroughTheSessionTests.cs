using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The application's own settings policy, exercised through the settings session that runs it.
/// </summary>
///
/// <remarks>
/// <para>
/// The library's suite tests the policy <em>hook</em> — that it runs against a private copy, that the
/// outcome carries what was sent — with a test double, which is right: which change a policy makes is the
/// host's business, and having the library's tests depend on OTA's filter rules was a coupling nobody
/// chose (#807 Phase 6).
/// </para>
/// <para>
/// But moving those tests took the real policy out of the picture entirely. <c>ProfileFilterMaintenance</c>
/// tests the rules in isolation, and <c>DaemonOwnershipTests</c> passes because <c>AppSession</c> cleans
/// filters on its <em>load</em> path — a different mechanism. Nothing exercised OtaSettingsPolicy through
/// the session that actually applies it on the way out, so this does.
/// </para>
/// <para>
/// Ownership-gated deliberately, and phrased as "is ours" rather than "is not theirs" (#465/#742): a
/// daemon OTA cannot identify is neither, and disabling a stranger's filters because we could not tell
/// whose they were is worse than leaving them alone.
/// </para>
/// </remarks>
public class OtaPolicyThroughTheSessionTests
{
    /// <summary>A real filter OTA does not approve, so the policy has something to act on.</summary>
    private const string UnapprovedFilter = "OpenTabletDriver.Filters.Noise.NoiseReduction";

    [Fact]
    public async Task OnAnOwnedDaemon_ThePolicyDisablesAnUnapprovedFilterOnTheWayOut()
    {
        var (session, settings, daemon, store) = Make(isOwnedDaemon: true);
        using var _ = session;

        var mine = WithUnapprovedFilter();
        var outcome = await settings.ApplyAndSaveAsync(mine);

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);

        // Disabled, not absent -- and the difference matters, because a check that only asked "is it
        // enabled" answers no to a filter that was removed, to settings that are null, and to a write
        // that never happened. Each is a different defect and none of them is what this asserts.
        Assert.False(TheUnapprovedFilterIn(Assert.Single(daemon.Applied)).Enable);
        Assert.False(TheUnapprovedFilterIn(outcome.Prepared!.Settings).Enable);
        Assert.False(TheUnapprovedFilterIn(store.Last).Enable);

        // And the caller's own object is untouched. Policy runs on a private copy; an editor that went on
        // showing its draft would be showing settings nobody has.
        Assert.True(TheUnapprovedFilterIn(mine).Enable);
    }

    [Fact]
    public async Task OnADaemonOtaDoesNotOwn_TheSameFilterIsLeftAlone()
    {
        var (session, settings, daemon, _) = Make(isOwnedDaemon: false);
        using var _s = session;

        var outcome = await settings.ApplyAndSaveAsync(WithUnapprovedFilter());

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.True(TheUnapprovedFilterIn(Assert.Single(daemon.Applied)).Enable);
        Assert.True(TheUnapprovedFilterIn(outcome.Prepared!.Settings).Enable);
    }

    private static (OtdSession, IOtdSettingsSession, FakeDaemonTransport, LastWriteStore) Make(
        bool isOwnedDaemon)
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = new Settings() };
        var store = new LastWriteStore();

        // The real policy, passed explicitly. The shared helper defaults to none, which is what the
        // library's own tests want and is exactly why this has to say otherwise.
        var session = FakeSession.Over(daemon, store, new FakeProcessLocator(), OtaSettingsPolicy.Instance);
        var settings = session.OpenSettings(() => isOwnedDaemon, _ => { });

        daemon.Reconnect();
        return (session, settings, daemon, store);
    }

    private static Settings WithUnapprovedFilter()
    {
        var profile = new Profile { Tablet = "T" };
        profile.Filters.Add(new PluginSettingStore(UnapprovedFilter)
        {
            Path = UnapprovedFilter,
            Enable = true,
        });
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    /// <summary>
    /// The unapproved filter in <paramref name="s"/>, failing the test if it is not there at all.
    /// </summary>
    /// <remarks>
    /// So that a missing filter, or missing settings, cannot masquerade as a disabled one. The policy is
    /// supposed to turn this filter off, not delete it and not skip the write, and a boolean helper that
    /// returned false for all three would have reported success for any of them.
    /// </remarks>
    private static PluginSettingStore TheUnapprovedFilterIn(Settings? s)
    {
        Assert.NotNull(s);
        Assert.NotEmpty(s.Profiles);

        foreach (var filter in s.Profiles[0].Filters)
            if (filter.Path == UnapprovedFilter)
                return filter;

        Assert.Fail($"The settings no longer carry {UnapprovedFilter} at all.");
        return null!;
    }

    /// <summary>Keeps the last thing written, since what reached disk is part of the question.</summary>
    private sealed class LastWriteStore : ISettingsFileStore
    {
        public Settings? Last { get; private set; }

        public void Save(Settings settings, string path) => Last = settings;

        public bool TrySave(Settings settings, string path)
        {
            Last = settings;
            return true;
        }

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = null;
            return false;
        }
    }
}
