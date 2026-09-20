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
        var (settings, daemon, store) = Make(isOwnedDaemon: true);

        var mine = WithUnapprovedFilter();
        var outcome = await settings.ApplyAndSaveAsync(mine);

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);

        // What the daemon was sent, and what the caller was handed back, both have it off.
        Assert.False(UnapprovedFilterEnabled(Assert.Single(daemon.Applied)));
        Assert.False(UnapprovedFilterEnabled(outcome.Prepared!.Settings));
        Assert.False(UnapprovedFilterEnabled(store.Last!));

        // And the caller's own object is untouched. Policy runs on a private copy; an editor that went on
        // showing its draft would be showing settings nobody has.
        Assert.True(UnapprovedFilterEnabled(mine));
    }

    [Fact]
    public async Task OnADaemonOtaDoesNotOwn_TheSameFilterIsLeftAlone()
    {
        var (settings, daemon, _) = Make(isOwnedDaemon: false);

        var outcome = await settings.ApplyAndSaveAsync(WithUnapprovedFilter());

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.True(UnapprovedFilterEnabled(Assert.Single(daemon.Applied)));
        Assert.True(UnapprovedFilterEnabled(outcome.Prepared!.Settings));
    }

    private static (IOtdSettingsSession, FakeDaemonTransport, LastWriteStore) Make(bool isOwnedDaemon)
    {
        var daemon = new FakeDaemonTransport { ServerProcessId = 1, Settings = new Settings() };
        var store = new LastWriteStore();

        // The real policy, passed explicitly. The shared helper defaults to none, which is what the
        // library's own tests want and is exactly why this has to say otherwise.
        var session = FakeSession.Over(daemon, store, new FakeProcessLocator(), OtaSettingsPolicy.Instance);
        var settings = session.OpenSettings(() => isOwnedDaemon, _ => { });

        daemon.Reconnect();
        return (settings, daemon, store);
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

    private static bool UnapprovedFilterEnabled(Settings? s)
    {
        if (s == null) return false;

        foreach (var filter in s.Profiles[0].Filters)
            if (filter.Path == UnapprovedFilter)
                return filter.Enable;

        return false;
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
